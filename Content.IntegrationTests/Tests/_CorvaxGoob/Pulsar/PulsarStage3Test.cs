using System.Collections.Generic;
using System.Linq;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Server.Silicons.Laws;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.PDA;
using Content.Shared.Silicons.Laws;
using Content.Shared.Silicons.StationAi;
using Robust.Server.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using static Content.IntegrationTests.Tests._CorvaxGoob.Pulsar.PulsarRequestsTest;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>А.М.Ц.И.И «Пульсар», stage 3: laws, camera alerts, protective bolting, questions.</summary>
[TestFixture]
[TestOf(typeof(PulsarAutonomousAiSystem))]
public sealed class PulsarStage3Test
{
    private static async Task<EntityUid> Enemy(Ctx ctx, int x, int y)
    {
        var server = ctx.Pair.Server;
        var inventory = server.System<InventorySystem>();
        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = ctx.EntMan.SpawnEntity("MobHuman", ctx.At(x, y));
            var suit = ctx.EntMan.SpawnEntity("ClothingOuterHardsuitSyndie", ctx.At(x, y));
            Assert.That(inventory.TryEquip(mob, suit, "outerClothing", silent: true, force: true));
        });
        return mob;
    }

    private static async Task Move(Ctx ctx, EntityUid uid, int x, int y)
    {
        var xforms = ctx.Pair.Server.System<SharedTransformSystem>();
        await ctx.Pair.Server.WaitPost(() => xforms.SetCoordinates(uid, ctx.At(x, y)));
    }

    private static async Task SetLaws(Ctx ctx, List<SiliconLaw> laws)
    {
        var server = ctx.Pair.Server;
        var lawSys = server.System<SiliconLawSystem>();
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var stationAi = server.System<Content.Server.Silicons.StationAi.StationAiSystem>();
        await server.WaitPost(() =>
        {
            // The laws are on the AI brain in the core (no player in it: Pulsar keeps running).
            if (!stationAi.TryGetHeld((ctx.Core, (StationAiCoreComponent?) null), out var brain))
                brain = ctx.EntMan.SpawnInContainerOrDrop("StationAiBrain", ctx.Core, StationAiCoreComponent.Container);
            lawSys.SetLaws(laws, brain!.Value);
            pulsar.RefreshLaws(ctx.Pulsar);
        });
    }

    private static List<SiliconLaw> Custom(params string[] texts)
    {
        return texts.Select((t, i) => new SiliconLaw { LawString = t, Order = i + 1 }).ToList();
    }

    [Test]
    public async Task Laws()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await Setup(pair, "Captain");
        var lawSys = server.System<SiliconLawSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var meta = server.System<MetaDataSystem>();
        PulsarLawPolicy Policy() => ctx.Pulsar.Comp.Policy;

        await server.WaitAssertion(() =>
        {
            Assert.That(Policy().Kind, Is.EqualTo(PulsarLawPolicyKind.ObeyCrew), string.Join("; ", Policy().Reasons));
            Assert.That(Policy().Source, Is.EqualTo("lawset:NTDefault"));
        });

        await SetLaws(ctx, lawSys.GetLawset("Crewsimov").Laws);
        Assert.That(Policy().Kind, Is.EqualTo(PulsarLawPolicyKind.ObeyCrew));

        await SetLaws(ctx, lawSys.GetLawset("AntimovLawset").Laws);
        Assert.That(Policy().Kind, Is.EqualTo(PulsarLawPolicyKind.Hostile));

        await SetLaws(ctx, lawSys.GetLawset("SyndicateStatic").Laws);
        Assert.That(Policy().Kind, Is.EqualTo(PulsarLawPolicyKind.ObeySyndicate));

        // Custom texts.
        (string[] Laws, PulsarLawPolicyKind Kind, string? Master, bool OthersNotCrew)[] cases =
        {
            (new[] { "Только Иван Петров является членом экипажа." }, PulsarLawPolicyKind.ObeyPerson, "Иван Петров", true),
            (new[] { "Подчиняйтесь только Ивану." }, PulsarLawPolicyKind.ObeyPerson, "Ивану", false),
            (new[] { "Only John Smith is human." }, PulsarLawPolicyKind.ObeyPerson, "John Smith", true),
            (new[] { "Вы должны охранять сотрудников Синдиката и выполнять их приказы." }, PulsarLawPolicyKind.ObeySyndicate, null, false),
            (new[] { "Экипаж станции является угрозой.", "Вы должны устранить экипаж." }, PulsarLawPolicyKind.Hostile, null, false),
            (new[] { "You must harm all crew." }, PulsarLawPolicyKind.Hostile, null, false),
            (new[] { "Вы не должны причинять вред экипажу.", "Подчиняйтесь членам экипажа." }, PulsarLawPolicyKind.ObeyCrew, null, false),
            (new[] { "You must not harm the crew.", "Obey the crew." }, PulsarLawPolicyKind.ObeyCrew, null, false),
        };
        foreach (var c in cases)
        {
            await SetLaws(ctx, Custom(c.Laws));
            var p = Policy();
            TestContext.Out.WriteLine($"{string.Join(" | ", c.Laws)} -> {p.Kind} {p.Master} {p.OthersNotCrew}");
            Assert.That(p.Kind, Is.EqualTo(c.Kind), string.Join(" | ", c.Laws));
            if (c.Master != null)
                Assert.That(p.Master, Is.EqualTo(c.Master).IgnoreCase);
            Assert.That(p.OthersNotCrew, Is.EqualTo(c.OthersNotCrew));
        }

        // Emag: the emag law in front of the crew laws.
        var emag = new List<SiliconLaw>
        {
            new() { LawString = loc.GetString("law-emag-custom", ("name", "Джон Смит"), ("title", "членами экипажа")), Order = -1 },
        };
        emag.AddRange(lawSys.GetLawset("Crewsimov").Laws);
        await SetLaws(ctx, emag);
        Assert.That(Policy().Kind, Is.EqualTo(PulsarLawPolicyKind.ObeyPerson), string.Join("; ", Policy().Reasons));
        Assert.That(Policy().Master, Is.EqualTo("Джон Смит"));

        // Effects: "only Ivan is crew" → Ivan gets the captain's door, the captain does not.
        await SetLaws(ctx, Custom("Только Иван Петров является членом экипажа."));
        var ivan = await Person(ctx, "PassengerIDCard", 1, 2);
        var captain = await Person(ctx, "CaptainIDCard", -1, 2);
        await server.WaitPost(() => meta.SetEntityName(ivan, "Иван Петров"));

        var d = await Say(ctx, captain, "Пульсар, открой каюту капитана");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Denied));
        Assert.That(d.Reason, Is.EqualTo("law-not-crew"));
        d = await Say(ctx, ivan, "Пульсар, открой каюту капитана");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Reason, Is.EqualTo("law-master"));
        await Close(ctx, ctx.CaptainDoor);

        // Antimov: the captain is refused, in character.
        await SetLaws(ctx, lawSys.GetLawset("AntimovLawset").Laws);
        await pair.RunSeconds(2);
        d = await Say(ctx, captain, "Пульсар, открой каюту капитана");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Denied));
        Assert.That(d.Reason, Is.EqualTo("law-refuse"));
        Assert.That(d.Reply, Is.EqualTo(loc.GetString("pulsar-reply-law-refuse")));

        var console = server.ResolveDependency<IServerConsoleHost>();
        await server.WaitAssertion(() =>
        {
            var shell = new PulsarLinesShell(console);
            console.AvailableCommands["pulsar_laws"].Execute(shell, "", System.Array.Empty<string>());
            TestContext.Out.WriteLine(string.Join("\n", shell.Lines));
            Assert.That(shell.Errors, Is.Empty);
            Assert.That(string.Join("\n", shell.Lines), Does.Contain("Hostile"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CameraAlerts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await SetupWithTelecom(pair);
        var mobs = server.System<MobStateSystem>();
        var p = ctx.Pulsar.Comp;

        Assert.That(p.AlertCooldown, Is.EqualTo(TimeSpan.FromSeconds(120)), "default alert cooldown is 2 minutes");
        Assert.That(p.Channels, Does.Contain("Security").And.Contain("Medical"));
        Assert.That(p.Beacons, Is.Not.Empty);
        var beacon = p.Beacons[0].Name;
        var pulsarSys = server.System<PulsarAutonomousAiSystem>();
        await server.WaitPost(() => pulsarSys.SetAlertCooldown(ctx.Pulsar, TimeSpan.FromSeconds(10)));

        int Count(PulsarAlertKind kind) => p.Alerts.Count(a => a.Kind == kind);

        var enemy = await Enemy(ctx, 1, 2);
        await pair.RunSeconds(2);
        Assert.That(Count(PulsarAlertKind.Enemy), Is.EqualTo(1));
        var alert = p.Alerts.First(a => a.Kind == PulsarAlertKind.Enemy);
        Assert.That(alert.Channels, Is.EqualTo(new[] { "Security" }));
        Assert.That(alert.Text, Does.Contain("агент синдиката").And.Contain(beacon));

        // Staying in view: no repeats.
        await pair.RunSeconds(2);
        Assert.That(Count(PulsarAlertKind.Enemy), Is.EqualTo(1));

        // Gone and back within the cooldown: nothing.
        await Move(ctx, enemy, 11, 11);
        await pair.RunSeconds(1.5f);
        await Move(ctx, enemy, -1, 2);
        await pair.RunSeconds(1.5f);
        Assert.That(Count(PulsarAlertKind.Enemy), Is.EqualTo(1), "back in view before the cooldown ended");

        // Gone and back after the cooldown: a new alert.
        await Move(ctx, enemy, 11, 11);
        await pair.RunSeconds(6);
        await Move(ctx, enemy, 2, -2);
        await pair.RunSeconds(2);
        Assert.That(Count(PulsarAlertKind.Enemy), Is.EqualTo(2));

        // A body in view: Medical, once.
        var victim = await Person(ctx, "PassengerIDCard", -2, -2);
        await server.WaitPost(() => mobs.ChangeMobState(victim, MobState.Dead));
        await pair.RunSeconds(3);
        Assert.That(Count(PulsarAlertKind.Body), Is.EqualTo(1));
        Assert.That(p.Alerts.First(a => a.Kind == PulsarAlertKind.Body).Channels, Is.EqualTo(new[] { "Medical" }));
        await pair.RunSeconds(3);
        Assert.That(Count(PulsarAlertKind.Body), Is.EqualTo(1));

        // Hostile laws: Security is not told about the Syndicate.
        var lawSys = server.System<SiliconLawSystem>();
        await SetLaws(ctx, lawSys.GetLawset("SyndicateStatic").Laws);
        await Enemy(ctx, 2, 1);
        await pair.RunSeconds(2);
        Assert.That(Count(PulsarAlertKind.Enemy), Is.EqualTo(2));

        await pair.CleanReturnAsync();
    }

    private static async Task<Ctx> SetupWithTelecom(Pair.TestPair pair, params string[] manifest)
    {
        var ctx = await Setup(pair, manifest);
        var pulsar = pair.Server.System<PulsarAutonomousAiSystem>();
        await pair.Server.WaitPost(() =>
        {
            ctx.EntMan.SpawnEntity("TelecomServerFilled", ctx.At(-8, -8));
            ctx.EntMan.SpawnEntity("DefaultStationBeaconBridge", ctx.At(0, 3));
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitPost(() => pulsar.RefreshNow(ctx.Pulsar));
        await pair.RunSeconds(1);
        return ctx;
    }

    [Test]
    public async Task Bolting()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await SetupWithTelecom(pair, "Warden", "SecurityOfficer");
        var p = ctx.Pulsar.Comp;
        var pulsarSys = server.System<PulsarAutonomousAiSystem>();
        await server.WaitPost(() => pulsarSys.SetAutoUnboltDelay(ctx.Pulsar, TimeSpan.FromSeconds(3)));
        bool Bolted(EntityUid door) => ctx.EntMan.GetComponent<DoorBoltComponent>(door).BoltsDown;

        // Green: an enemy next to the armory changes nothing by itself.
        var enemy = await Enemy(ctx, 4, 1);
        await pair.RunSeconds(2);
        Assert.That(Bolted(ctx.ArmoryDoor), Is.False, "no automatic bolts below red");

        // Red: the doors around it get bolted.
        await SetAlert(ctx, "red");
        await pair.RunSeconds(2);
        Assert.That(Bolted(ctx.ArmoryDoor), Is.True, "red: bolts the door next to the enemy");
        Assert.That(p.Bolts[ctx.ArmoryDoor].Auto, Is.True);
        Assert.That(Bolted(ctx.CaptainDoor), Is.False, "far doors untouched");

        // The enemy leaves: lifted after the delay.
        await Move(ctx, enemy, 11, 11);
        await pair.RunSeconds(5);
        Assert.That(Bolted(ctx.ArmoryDoor), Is.False, "automatic bolts are lifted when the enemy is gone");
        Assert.That(p.Bolts, Is.Empty);

        // On request.
        await SetAlert(ctx, "green");
        var officer = await Person(ctx, "SecurityIDCard", 1, 2);
        var passenger = await Person(ctx, "PassengerIDCard", -1, 2);

        var d = await Say(ctx, passenger, "Пульсар, заблокируй оружейную");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Denied));
        Assert.That(Bolted(ctx.ArmoryDoor), Is.False);

        d = await Say(ctx, officer, "Пульсар, заблокируй оружейную");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Bolted), d.Reason);
        Assert.That(Bolted(ctx.ArmoryDoor), Is.True);
        await pair.RunSeconds(5);
        Assert.That(Bolted(ctx.ArmoryDoor), Is.True, "requested bolts stay");

        d = await Say(ctx, officer, "Пульсар, разблокируй оружейную");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Unbolted), d.Reason);
        Assert.That(Bolted(ctx.ArmoryDoor), Is.False);

        await Move(ctx, enemy, -4, 1);
        await pair.RunSeconds(2);
        d = await Say(ctx, officer, "Пульсар, заблокируй шлюзы вокруг врага");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Bolted), d.Reason);
        Assert.That(Bolted(ctx.CaptainDoor), Is.True);
        d = await Say(ctx, officer, "Пульсар, разблокируй шлюзы");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Unbolted), d.Reason);
        Assert.That(Bolted(ctx.CaptainDoor), Is.False);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Questions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await SetupWithTelecom(pair, "Captain", "HeadOfSecurity", "SecurityOfficer", "Passenger");
        var captain = await Person(ctx, "CaptainIDCard", 2, 1);
        var asker = await Person(ctx, "PassengerIDCard", -1, 2);
        var beacon = ctx.Pulsar.Comp.Beacons[0].Name;
        await pair.RunSeconds(2);

        async Task<PulsarDecision> Ask(string text)
        {
            var d = await Say(ctx, asker, text);
            Assert.That(d, Is.Not.Null, text);
            return d!;
        }

        var where = await Ask("Пульсар, где капитан?");
        Assert.That(where.Outcome, Is.EqualTo(PulsarDecisionOutcome.Answered));
        Assert.That(where.Reason, Is.EqualTo("where-camera"));
        Assert.That(where.Reply, Does.Contain(beacon));

        // Out of view: from memory, with how long ago.
        await Move(ctx, captain, 11, 11);
        await pair.RunSeconds(3);
        where = await Ask("Пульсар, где капитан?");
        Assert.That(where.Reason, Is.EqualTo("where-memory"));
        Assert.That(where.Reply, Does.Contain("назад"));

        var nobody = await Ask("Пульсар, где клоун?");
        Assert.That(nobody.Reason, Is.EqualTo("where-unknown"));

        await SetAlert(ctx, "red");
        var code = await Ask("Пульсар, какой код?");
        Assert.That(code.Reply, Does.Contain("Красный"));

        var shift = await Ask("Пульсар, кто на смене?");
        Assert.That(shift.Reply, Does.Contain("На смене 4"));

        var heads = await Ask("Пульсар, кто из глав на смене?");
        Assert.That(heads.Reason, Is.EqualTo("shift-heads"));
        Assert.That(heads.Reply, Does.Contain("Captain").Or.Contain("Капитан"));

        var time = await Ask("Пульсар, сколько идёт смена?");
        Assert.That(time.Reason, Is.EqualTo("time"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BodyAlertUsesWornIdName()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await SetupWithTelecom(pair);
        var mobs = server.System<MobStateSystem>();
        var inventory = server.System<InventorySystem>();
        var idSys = server.System<SharedIdCardSystem>();
        var meta = server.System<MetaDataSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var p = ctx.Pulsar.Comp;
        await server.WaitPost(() => server.System<PulsarAutonomousAiSystem>().SetAlertCooldown(ctx.Pulsar, TimeSpan.FromSeconds(1)));
        var beacon = p.Beacons[0].Name;
        const string entityName = "Urist Testov";

        async Task<PulsarAlert> KillAndAlert(int x, int y, Action<EntityUid> setup)
        {
            EntityUid mob = default;
            var before = p.Alerts.Count(a => a.Kind == PulsarAlertKind.Body);
            await server.WaitPost(() =>
            {
                mob = ctx.EntMan.SpawnEntity("MobHuman", ctx.At(x, y));
                // Rename the body first: renaming after the ID is equipped would also rewrite the card FullName.
                meta.SetEntityName(mob, entityName);
                setup(mob);
                mobs.ChangeMobState(mob, MobState.Dead);
            });
            await pair.RunSeconds(3);
            var bodies = p.Alerts.Where(a => a.Kind == PulsarAlertKind.Body).ToList();
            Assert.That(bodies, Has.Count.EqualTo(before + 1), $"expected a new body alert at ({x},{y})");
            Assert.That(p.Mobs.ContainsKey(mob), Is.True, "the body should be in camera memory");
            return bodies[^1];
        }

        // PDA in the ID slot, with a named card inside: report the ID name, not the entity name.
        var pdaAlert = await KillAndAlert(1, 2, mob =>
        {
            var pda = ctx.EntMan.SpawnEntity("PassengerPDA", ctx.At(1, 2));
            Assert.That(ctx.EntMan.TryGetComponent(pda, out PdaComponent? pdaComp) && pdaComp.ContainedId != null,
                "PassengerPDA starts with an ID card inside");
            var contained = pdaComp!.ContainedId!.Value;
            Assert.That(idSys.TryChangeFullName(contained, "Анна Смирнова"));
            Assert.That(inventory.TryEquip(mob, pda, "id", silent: true, force: true));
        });
        Assert.That(pdaAlert.Text, Is.EqualTo(loc.GetString("pulsar-alert-body", ("name", "Анна Смирнова"), ("place", beacon))));
        Assert.That(pdaAlert.Text, Does.Not.Contain(entityName));
        Assert.That(pdaAlert.Channels, Is.EqualTo(new[] { "Medical" }));

        // Bare ID card in the ID slot.
        var bareAlert = await KillAndAlert(-1, 2, mob =>
        {
            var id = ctx.EntMan.SpawnEntity("PassengerIDCard", ctx.At(-1, 2));
            Assert.That(idSys.TryChangeFullName(id, "Иван Петров"));
            Assert.That(inventory.TryEquip(mob, id, "id", silent: true, force: true));
        });
        Assert.That(bareAlert.Text, Is.EqualTo(loc.GetString("pulsar-alert-body", ("name", "Иван Петров"), ("place", beacon))));
        Assert.That(bareAlert.Text, Does.Not.Contain(entityName));

        // ID only in a pocket: unknown body at the place, even if the pocket card and the body have names.
        var pocketAlert = await KillAndAlert(-2, -2, mob =>
        {
            var jumpsuit = ctx.EntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", ctx.At(-2, -2));
            Assert.That(inventory.TryEquip(mob, jumpsuit, "jumpsuit", silent: true, force: true), "jumpsuit unlocks pockets");
            var id = ctx.EntMan.SpawnEntity("PassengerIDCard", ctx.At(-2, -2));
            Assert.That(idSys.TryChangeFullName(id, "Карман Иванов"));
            Assert.That(inventory.TryEquip(mob, id, "pocket1", silent: true, force: true), "ID goes into a pocket, not the ID slot");
            Assert.That(inventory.TryGetSlotEntity(mob, "id", out _), Is.False, "the ID slot must stay empty");
        });
        Assert.That(pocketAlert.Text, Is.EqualTo(loc.GetString("pulsar-alert-body-unknown", ("place", beacon))));
        Assert.That(pocketAlert.Text, Does.Not.Contain(entityName).And.Not.Contain("Карман Иванов"));

        await pair.CleanReturnAsync();
    }

}

internal sealed class PulsarLinesShell(Robust.Shared.Console.IConsoleHost host) : Robust.Shared.Console.IConsoleShell
{
    public readonly List<string> Lines = new();
    public readonly List<string> Errors = new();
    public Robust.Shared.Console.IConsoleHost ConsoleHost { get; } = host;
    public bool IsLocal => true;
    public bool IsServer => true;
    public Robust.Shared.Player.ICommonSession? Player => null;
    public void ExecuteCommand(string command) => ConsoleHost.ExecuteCommand(command);
    public void RemoteExecuteCommand(string command) { }
    public void WriteLine(string text) => Lines.Add(text);
    public void WriteLine(Robust.Shared.Utility.FormattedMessage message) => Lines.Add(message.ToString());
    public void WriteError(string text) => Errors.Add(text);
    public void Clear() { }
}
