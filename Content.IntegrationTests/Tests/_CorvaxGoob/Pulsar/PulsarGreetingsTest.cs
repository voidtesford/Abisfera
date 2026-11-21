using System.Collections.Generic;
using System.Linq;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Shared._CorvaxGoob.CCCVars;
using Content.Shared.Mind;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>А.М.Ц.И.И «Пульсар», stage 2.1: activation / reactivation greetings.</summary>
[TestFixture]
[TestOf(typeof(PulsarAutonomousAiSystem))]
public sealed class PulsarGreetingsTest
{
    private static async Task<EntityUid> SpawnCore(Pair.TestPair pair, bool telecom)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var mapSys = server.System<SharedMapSystem>();
        EntityUid core = default;
        await server.WaitPost(() =>
        {
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -6; x <= 6; x++)
            for (var y = -6; y <= 6; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            mapSys.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            if (telecom)
                server.EntMan.SpawnEntity("TelecomServerFilled", new EntityCoordinates(map.Grid.Owner, -4.5f, -4.5f));
            core = server.EntMan.SpawnEntity("PlayerStationAi", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        });
        return core;
    }

    private static List<PulsarGreeting> Greetings(Pair.TestPair pair, EntityUid core)
    {
        return pair.Server.EntMan.GetComponent<PulsarAutonomousAiComponent>(core).Greetings.ToList();
    }

    [Test]
    public async Task ActivationReactivationAndToggles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var mindSys = server.System<SharedMindSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var entMan = server.EntMan;
        var core = await SpawnCore(pair, telecom: false);
        Entity<PulsarAutonomousAiComponent> P() => (core, entMan.GetComponent<PulsarAutonomousAiComponent>(core));

        // Waits for the start-up delay, then greets once.
        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(Greetings(pair, core), Is.Empty, "not before the 3 s start-up delay"));
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var g = Greetings(pair, core);
            Assert.That(g, Has.Count.EqualTo(1));
            Assert.That(g[0].Kind, Is.EqualTo(PulsarGreetingKind.Activation));
            Assert.That(g[0].Text, Is.EqualTo(loc.GetString("pulsar-greeting-activation")));
            Assert.That(g[0].Channel, Is.Null, "stage 4: greetings are station announcements");
            var a = P().Comp.Announcements;
            Assert.That(a, Has.Count.EqualTo(1));
            Assert.That(a[0].Text, Is.EqualTo(g[0].Text));
            Assert.That(a[0].Sender, Is.EqualTo(loc.GetString("pulsar-announcement-sender")));
        });

        // Admin toggles without a player in between: no greeting.
        await server.WaitPost(() => cfg.SetCVar(AbisferaCVars.PulsarEnabled, false));
        await pair.RunSeconds(1);
        await server.WaitPost(() => cfg.SetCVar(AbisferaCVars.PulsarEnabled, true));
        await pair.RunSeconds(1);
        await server.WaitPost(() => pulsar.SetEnabled(P(), false));
        await pair.RunSeconds(1);
        await server.WaitPost(() => pulsar.SetEnabled(P(), true));
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(P().Comp.State, Is.EqualTo(PulsarState.Active));
            Assert.That(Greetings(pair, core), Has.Count.EqualTo(1), "re-enabling is not a reactivation");
        });

        // A player takes the AI and leaves: after the grace period, the reactivation greeting, once.
        EntityUid mind = default;
        await server.WaitPost(() =>
        {
            pulsar.SetVacancyGrace(P(), TimeSpan.FromSeconds(2));
            var brain = entMan.SpawnInContainerOrDrop("StationAiBrain", core, StationAiCoreComponent.Container);
            mind = mindSys.CreateMind(null, "test AI player");
            mindSys.TransferTo(mind, brain);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(P().Comp.State, Is.EqualTo(PulsarState.Suspended));
            Assert.That(Greetings(pair, core), Has.Count.EqualTo(1));
        });
        await server.WaitPost(() => mindSys.TransferTo(mind, null, createGhost: false));
        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(Greetings(pair, core), Has.Count.EqualTo(1), "nothing during the grace period"));
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var g = Greetings(pair, core);
            Assert.That(P().Comp.State, Is.EqualTo(PulsarState.Active));
            Assert.That(g, Has.Count.EqualTo(2));
            Assert.That(g[1].Kind, Is.EqualTo(PulsarGreetingKind.Reactivation));
            Assert.That(g[1].Text, Is.EqualTo(loc.GetString("pulsar-greeting-reactivation")));
        });

        // Toggling again afterwards: still no greeting.
        await server.WaitPost(() => cfg.SetCVar(AbisferaCVars.PulsarEnabled, false));
        await pair.RunSeconds(1);
        await server.WaitPost(() => cfg.SetCVar(AbisferaCVars.PulsarEnabled, true));
        await pair.RunSeconds(2);
        await server.WaitAssertion(() => Assert.That(Greetings(pair, core), Has.Count.EqualTo(2)));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GreetsByAnnouncement()
    {
        await using var pair = await PoolManager.GetServerClient();
        var core = await SpawnCore(pair, telecom: true);
        await pair.RunSeconds(4);
        await pair.Server.WaitAssertion(() =>
        {
            var g = Greetings(pair, core);
            Assert.That(g, Has.Count.EqualTo(1));
            Assert.That(g[0].Kind, Is.EqualTo(PulsarGreetingKind.Activation));
            var comp = pair.Server.EntMan.GetComponent<PulsarAutonomousAiComponent>(core);
            Assert.That(comp.Announcements, Has.Count.EqualTo(1), "an announcement, not a line on Common");
            Assert.That(comp.Announcements[0].Reason, Is.EqualTo("greeting-activation"));
            Assert.That(comp.Announcements[0].Sender, Is.EqualTo("А.М.Ц.И.И Пульсар"));
        });
        await pair.CleanReturnAsync();
    }
}
