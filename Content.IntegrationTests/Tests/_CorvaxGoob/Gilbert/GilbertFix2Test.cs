using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._CorvaxGoob.ProcAnim;
using Content.Server._CorvaxGoob.Gilbert.Finisher;
using Content.Server._CorvaxGoob.ProcAnim;
using Content.Shared._CorvaxGoob.ProcAnim;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Gilbert;

/// <summary>
/// fix2 (real-client diagnosis): the server can end one scene and start the next on the same actors in one tick
/// (the rage leap straight into the rage finisher). The client then never sees the component shut down, only its
/// fields change: the old play state (puppet, hidden originals, event cursor, view) must be restored exactly once and
/// rebuilt, and everything must come back after the last scene.
/// </summary>
[TestFixture]
public sealed class GilbertFix2Test
{
    private static async Task RunSeconds(Pair.TestPair pair, double seconds)
    {
        var rate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        await pair.RunTicksSync((int) Math.Ceiling(seconds * rate) + 1);
    }

    [Test]
    public async Task SceneSwitchInOneTickRebuildsAndRestores()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var sEnt = server.EntMan;
        var cEnt = client.EntMan;

        EntityUid a = default, v = default;
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            for (var x = -6; x <= 6; x++)
            for (var y = -6; y <= 6; y++)
                mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            a = GilbertTestUtil.SpawnActive(sEnt, "MobGilbert", map.GridCoords);
            v = sEnt.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1.3f, 0.2f)));
        });
        await RunSeconds(pair, 0.3);

        var ca = pair.ToClientUid(a);
        var cv = pair.ToClientUid(v);
        List<SpriteComponent.Layer> originalVisible = new();
        await client.WaitPost(() =>
        {
            var sprite = cEnt.GetComponent<SpriteComponent>(ca);
            originalVisible = sprite.AllLayers.OfType<SpriteComponent.Layer>().Where(l => l.Visible).ToList();
        });

        await server.WaitPost(() => Assert.That(server.System<GilbertFinisherSystem>().TryStart(a, v, out var err), Is.True, err));
        var intro = 0f;
        await server.WaitPost(() => intro = sEnt.GetComponent<ProcAnimComponent>(a).Intro);
        await RunSeconds(pair, intro + 0.4);

        SpriteComponent.Layer? firstTorso = null;
        await client.WaitAssertion(() =>
        {
            firstTorso = cEnt.System<ProcAnimVisualsSystem>().GetPuppetLayer(ca, ProcAnimPuppetGroup.Torso);
            Assert.That(firstTorso, Is.Not.Null, "first scene puppeted");
        });

        // End the scene and start the next one in the same server tick.
        await server.WaitPost(() =>
        {
            server.System<ProcAnimSystem>().Stop(a);
            Assert.That(server.System<GilbertFinisherSystem>().TryStart(a, v, out var err, GilbertFinisherSystem.DefaultFinisher), Is.True, err);
        });
        await server.WaitPost(() => intro = sEnt.GetComponent<ProcAnimComponent>(a).Intro);
        await RunSeconds(pair, intro + 0.4);

        await client.WaitAssertion(() =>
        {
            var vis = cEnt.System<ProcAnimVisualsSystem>();
            var sprite = cEnt.GetComponent<SpriteComponent>(ca);
            var torso = vis.GetPuppetLayer(ca, ProcAnimPuppetGroup.Torso);
            Assert.That(torso, Is.Not.Null, "second scene puppeted");
            Assert.That(torso, Is.Not.SameAs(firstTorso), "old puppet restored and rebuilt, not carried into the new scene");
            Assert.That(sprite.AllLayers.OfType<SpriteComponent.Layer>().Contains(firstTorso!), Is.False, "old puppet layer removed");
            var puppetLayers = sprite.AllLayers.OfType<SpriteComponent.Layer>()
                .Count(l => vis.GetPuppetLayer(ca, ProcAnimPuppetGroup.Torso) == l);
            Assert.That(puppetLayers, Is.EqualTo(1), "exactly one torso puppet, no second figure");
        });

        await server.WaitPost(() => server.System<ProcAnimSystem>().Stop(a));
        await RunSeconds(pair, 0.5);

        await client.WaitAssertion(() =>
        {
            var vis = cEnt.System<ProcAnimVisualsSystem>();
            Assert.That(cEnt.HasComponent<ProcAnimComponent>(ca), Is.False);
            Assert.That(vis.GetPuppetLayer(ca, ProcAnimPuppetGroup.Torso), Is.Null);
            Assert.That(vis.GetPuppetHiddenLayers(ca), Is.Empty);
            var sprite = cEnt.GetComponent<SpriteComponent>(ca);
            Assert.That(sprite.Visible, Is.True);
            Assert.That(sprite.Color.A, Is.EqualTo(1f).Within(1e-3f));
            foreach (var layer in originalVisible)
                if (sprite.AllLayers.Contains(layer))
                    Assert.That(layer.Visible, Is.True, $"original layer {layer.ActualState?.StateId.Name} visible again");
            Assert.That(sprite.EnableDirectionOverride, Is.False, "view lock released");
            Assert.That(cEnt.GetComponent<SpriteComponent>(cv).Visible, Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
