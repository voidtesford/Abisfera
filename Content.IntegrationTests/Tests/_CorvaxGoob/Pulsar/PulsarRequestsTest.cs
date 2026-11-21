using Content.Shared.Station;
using Content.Shared.Medical.SuitSensors;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Server.AlertLevel;
using Content.Server.Medical.SuitSensors;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.StationRecords;
using Content.Server.Station;
using Robust.Server.Console;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 2: names, requests to open airlocks and the captain / armory rules.
/// </summary>
[TestFixture]
[TestOf(typeof(PulsarAutonomousAiSystem))]
public sealed class PulsarRequestsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: PulsarTestStation
  parent: [ BaseStation, BaseStationRecords, BaseStationAlertLevels ]
  categories: [ HideSpawnMenu ]
";

    internal sealed class Ctx
    {
        public TestPair Pair = default!;
        public TestMapData Map = default!;
        public IEntityManager EntMan = default!;
        public EntityUid Core;
        public EntityUid Station;
        public EntityUid CaptainDoor;
        public EntityUid ArmoryDoor;
        public readonly Dictionary<string, StationRecordKey> Manifest = new();

        public EntityCoordinates At(int x, int y) => new(Map.Grid.Owner, x + 0.5f, y + 0.5f);

        public Entity<PulsarAutonomousAiComponent> Pulsar => (Core, EntMan.GetComponent<PulsarAutonomousAiComponent>(Core));
    }

    internal static async Task<Ctx> Setup(TestPair pair, params string[] manifest)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var ctx = new Ctx { Pair = pair, Map = map, EntMan = server.EntMan };
        var mapSys = server.System<SharedMapSystem>();
        var stations = server.System<StationSystem>();
        var records = server.System<StationRecordsSystem>();
        var power = server.System<SharedPowerReceiverSystem>();

        await server.WaitPost(() =>
        {
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            mapSys.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);

            ctx.Station = stations.InitializeNewStation(
                new StationConfig { StationPrototype = "PulsarTestStation", StationComponentOverrides = new ComponentRegistry() },
                new[] { map.Grid.Owner }, "Pulsar Test");

            foreach (var job in manifest)
                ctx.Manifest[job] = records.AddRecordEntry(ctx.Station, new GeneralStationRecord { Name = job, JobPrototype = job });

            ctx.Core = server.EntMan.SpawnEntity("PlayerStationAi", ctx.At(0, 0));
            ctx.CaptainDoor = server.EntMan.SpawnEntity("AirlockCaptainLocked", ctx.At(-3, 0));
            ctx.ArmoryDoor = server.EntMan.SpawnEntity("AirlockArmoryLocked", ctx.At(3, 0));
            power.SetNeedsPower(ctx.CaptainDoor, false);
            power.SetNeedsPower(ctx.ArmoryDoor, false);
        });

        await pair.RunSeconds(5);
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        await server.WaitPost(() => pulsar.RefreshNow(ctx.Pulsar));
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Active));
            Assert.That(ctx.Pulsar.Comp.StationGrids, Does.Contain(map.Grid.Owner), "the test grid is the AI's station");
            Assert.That(pulsar.TryGetDoor(ctx.Pulsar, map.Grid.Owner, new Vector2i(-3, 0), out _), "captain door seen");
            Assert.That(pulsar.TryGetDoor(ctx.Pulsar, map.Grid.Owner, new Vector2i(3, 0), out _), "armory door seen");
        });

        return ctx;
    }

    internal static async Task<EntityUid> Person(Ctx ctx, string idCard, int x = 0, int y = 2)
    {
        var server = ctx.Pair.Server;
        var inventory = server.System<InventorySystem>();
        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = ctx.EntMan.SpawnEntity("MobHuman", ctx.At(x, y));
            var id = ctx.EntMan.SpawnEntity(idCard, ctx.At(x, y));
            Assert.That(inventory.TryEquip(mob, id, "id", silent: true, force: true));
        });
        return mob;
    }

    /// <summary>Says a line to Pulsar (at the core holopad or on a radio channel) and returns its decision.</summary>
    internal static async Task<PulsarDecision?> Say(Ctx ctx, EntityUid speaker, string text, string? channel = null)
    {
        var server = ctx.Pair.Server;
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var before = 0;
        await server.WaitPost(() =>
        {
            before = ctx.Pulsar.Comp.Decisions.Count;
            pulsar.HandleSpeech(ctx.Pulsar, speaker, text, channel, channel == null ? ctx.Core : null);
        });
        await ctx.Pair.RunTicksSync(3);
        PulsarDecision? result = null;
        await server.WaitPost(() =>
        {
            var d = ctx.Pulsar.Comp.Decisions;
            if (d.Count > before)
                result = d[^1];
        });
        if (result != null)
            TestContext.Out.WriteLine($"«{text}» -> {result.Outcome} [{result.Reason}] {result.Reply}");
        else
            TestContext.Out.WriteLine($"«{text}» -> (no decision)");
        return result;
    }

    internal static async Task<bool> IsOpen(Ctx ctx, EntityUid door)
    {
        var open = false;
        await ctx.Pair.RunSeconds(1.5f);
        await ctx.Pair.Server.WaitPost(() =>
        {
            open = ctx.EntMan.GetComponent<DoorComponent>(door).State is DoorState.Open or DoorState.Opening;
        });
        return open;
    }

    internal static async Task Close(Ctx ctx, EntityUid door)
    {
        var doors = ctx.Pair.Server.System<SharedDoorSystem>();
        await ctx.Pair.Server.WaitPost(() => doors.TryClose(door));
        await ctx.Pair.RunSeconds(2);
        await ctx.Pair.Server.WaitAssertion(() =>
            Assert.That(ctx.EntMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Closed)));
    }

    internal static async Task SetAlert(Ctx ctx, string level)
    {
        var alerts = ctx.Pair.Server.System<AlertLevelSystem>();
        await ctx.Pair.Server.WaitAssertion(() =>
        {
            alerts.SetLevel(ctx.Station, level, false, false, force: true);
            Assert.That(alerts.GetLevel(ctx.Station), Is.EqualTo(level));
        });
    }

    private static async Task Expect(Ctx ctx, EntityUid speaker, string text, PulsarDecisionOutcome outcome, EntityUid door, bool open, string? channel = null)
    {
        var d = await Say(ctx, speaker, text, channel);
        Assert.That(d, Is.Not.Null, text);
        Assert.That(d!.Outcome, Is.EqualTo(outcome), $"{text}: {d.Reason}");
        Assert.That(await IsOpen(ctx, door), Is.EqualTo(open), text);
    }

    [Test]
    public async Task NameVariants()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        EntityUid core = default;
        await server.WaitPost(() => core = server.EntMan.SpawnEntity("PlayerStationAi", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));

        await server.WaitAssertion(() =>
        {
            var ent = new Entity<PulsarAutonomousAiComponent>(core, server.EntMan.GetComponent<PulsarAutonomousAiComponent>(core));
            string[] yes =
            {
                "Пульсар, открой", "ПУЛЬСАР!", "нет пульсара", "пульсару сообщение", "Пульсаром", "А.М.Ц.И.И, приём",
                "а.м.ц.и.и", "АМЦИИ открой", "ЦИИ!", "ции, открой", "ИИ, открой дверь", "ии", "Ии.", "AI open",
                "Pulsar", "Пyльcap", "PULSAR open", "ИИ-шка? ии!", "эй, искин",
            };
            string[] no =
            {
                "линии связи", "химии", "Мии", "пульс в норме", "сии", "аиста", "ИИсус", "открой дверь",
                "энергии нет", "Пульс",
            };
            foreach (var line in yes)
                Assert.That(pulsar.IsAddressed(ent, line), Is.True, line);
            foreach (var line in no)
                Assert.That(pulsar.IsAddressed(ent, line), Is.False, line);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OwnAccess()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain");
        var passenger = await Person(ctx, "PassengerIDCard", 1, 2);
        var captain = await Person(ctx, "CaptainIDCard", -1, 2);

        await Expect(ctx, passenger, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Denied, ctx.CaptainDoor, false);
        var reply = ctx.Pulsar.Comp.Decisions[^1].Reply;
        Assert.That(reply, Is.Not.Null.And.Contains("доступа"), "polite refusal in character");

        // Not addressed and not talking to it: nothing happens.
        var stranger = await Person(ctx, "PassengerIDCard", 2, 2);
        Assert.That(await Say(ctx, stranger, "открой каюту капитана"), Is.Null);

        await Expect(ctx, captain, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Opened, ctx.CaptainDoor, true);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("own-access"));
        await Close(ctx, ctx.CaptainDoor);

        // A follow-up inside the conversation window needs no name; "this door" = the door next to the speaker.
        var nearDoor = await Person(ctx, "CaptainIDCard", -2, 0);
        Assert.That((await Say(ctx, nearDoor, "Пульсар"))!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Acknowledged));
        await Expect(ctx, nearDoor, "открой этот шлюз", PulsarDecisionOutcome.Opened, ctx.CaptainDoor, true);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reply, Is.Not.Null, "a reply held by the rate limit is said later, not lost");
        await pair.Server.WaitAssertion(() => Assert.That(ctx.Pulsar.Comp.DeferredReplies, Is.Empty, "the held reply was said"));
        await Close(ctx, ctx.CaptainDoor);

        // Bolted doors are reported, never unbolted.
        var doors = pair.Server.System<SharedDoorSystem>();
        await pair.Server.WaitPost(() =>
            doors.SetBoltsDown((ctx.CaptainDoor, ctx.EntMan.GetComponent<DoorBoltComponent>(ctx.CaptainDoor)), true));
        await Expect(ctx, captain, "ИИ, открой каюту капитана", PulsarDecisionOutcome.NotControllable, ctx.CaptainDoor, false);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Does.Contain("bolted"));
        await pair.Server.WaitAssertion(() =>
            Assert.That(ctx.EntMan.GetComponent<DoorBoltComponent>(ctx.CaptainDoor).BoltsDown, Is.True));

        // The debug commands.
        var console = pair.Server.ResolveDependency<IServerConsoleHost>();
        await pair.Server.WaitAssertion(() =>
        {
            var shell = new LinesShell(console);
            console.AvailableCommands["pulsar_decisions"].Execute(shell, "10", new[] { "10" });
            TestContext.Out.WriteLine(string.Join("\n", shell.Lines));
            Assert.That(shell.Errors, Is.Empty);
            Assert.That(string.Join("\n", shell.Lines), Does.Contain("own-access"));

            var status = new LinesShell(console);
            console.AvailableCommands["pulsar_status"].Execute(status, "", Array.Empty<string>());
            Assert.That(status.Errors, Is.Empty);

            var request = new LinesShell(console);
            var net = ctx.EntMan.GetNetEntity(passenger).ToString();
            console.AvailableCommands["pulsar_request"].Execute(request, $"{net} :Command Пульсар открой оружейную",
                new[] { net, ":Command", "Пульсар", "открой", "оружейную" });
            Assert.That(request.Errors, Is.Empty);
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
        {
            var d = ctx.Pulsar.Comp.Decisions[^1];
            Assert.That(d.Requester, Is.EqualTo(passenger));
            Assert.That(d.Outcome, Is.EqualTo(PulsarDecisionOutcome.Denied));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaptainQuarters()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await Setup(pair, "Captain", "ResearchDirector");
        var rd = await Person(ctx, "RDIDCard", 1, 2);
        var passenger = await Person(ctx, "PassengerIDCard", 2, 2);
        var captain = await Person(ctx, "CaptainIDCard", -6, -6);
        var inventory = server.System<InventorySystem>();
        var sensors = server.System<SuitSensorSystem>();
        var mobs = server.System<MobStateSystem>();
        var records = server.System<StationRecordsSystem>();
        EntityUid suit = default;
        await server.WaitPost(() =>
        {
            suit = ctx.EntMan.SpawnEntity("ClothingUniformJumpsuitCaptain", ctx.At(-6, -6));
            Assert.That(inventory.TryEquip(captain, suit, "jumpsuit", silent: true, force: true));
            sensors.SetSensor((suit, ctx.EntMan.GetComponent<SuitSensorComponent>(suit)), SuitSensorMode.SensorVitals, captain);
        });

        // RD, captain in the manifest and alive, green: no.
        await Expect(ctx, rd, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Denied, ctx.CaptainDoor, false);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("captain-on-shift"));

        // Red, captain alive: no.
        await SetAlert(ctx, "red");
        await Expect(ctx, rd, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Denied, ctx.CaptainDoor, false);

        // Captain dead on the sensors, but green: no.
        await SetAlert(ctx, "green");
        await server.WaitPost(() => mobs.ChangeMobState(captain, MobState.Dead));
        await Expect(ctx, rd, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Denied, ctx.CaptainDoor, false);

        // Red and the captain is dead on the sensors: yes.
        await SetAlert(ctx, "red");
        await Expect(ctx, rd, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Opened, ctx.CaptainDoor, true);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("red-alert-captain-dead"));
        await Close(ctx, ctx.CaptainDoor);

        // Not a head: still no.
        await Expect(ctx, passenger, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Denied, ctx.CaptainDoor, false);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("no-access"));

        // Sensors off: not "dead on sensors".
        await server.WaitPost(() =>
            sensors.SetSensor((suit, ctx.EntMan.GetComponent<SuitSensorComponent>(suit)), SuitSensorMode.SensorOff, captain));
        await Expect(ctx, rd, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Denied, ctx.CaptainDoor, false);

        // No captain in the manifest: any head, any level.
        await SetAlert(ctx, "green");
        await server.WaitPost(() => records.RemoveRecord(ctx.Manifest["Captain"]));
        await Expect(ctx, rd, "Пульсар, открой каюту капитана", PulsarDecisionOutcome.Opened, ctx.CaptainDoor, true);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("captain-not-in-manifest"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Armory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await Setup(pair, "Warden", "HeadOfSecurity", "SecurityOfficer");
        var officer = await Person(ctx, "SecurityIDCard", 1, 2);
        var warden = await Person(ctx, "WardenIDCard", 2, 2);
        var passenger = await Person(ctx, "PassengerIDCard", -1, 2);
        var records = server.System<StationRecordsSystem>();

        // Warden on shift: ask the warden. Even on red.
        await SetAlert(ctx, "red");
        await Expect(ctx, officer, "Пульсар, открой оружейную", PulsarDecisionOutcome.Denied, ctx.ArmoryDoor, false, "Security");
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("warden-or-hos-on-shift"));
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reply, Is.Not.Null);

        // Only the HoS left: still no.
        await server.WaitPost(() => records.RemoveRecord(ctx.Manifest["Warden"]));
        await Expect(ctx, officer, "Пульсар, открой оружейную", PulsarDecisionOutcome.Denied, ctx.ArmoryDoor, false, "Security");

        // Both gone, red: open.
        await server.WaitPost(() => records.RemoveRecord(ctx.Manifest["HeadOfSecurity"]));
        await Expect(ctx, officer, "Пульсар, открой оружейную", PulsarDecisionOutcome.Opened, ctx.ArmoryDoor, true, "Security");
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("armory-red-or-higher"));
        await Close(ctx, ctx.ArmoryDoor);

        foreach (var level in new[] { "gamma", "delta" })
        {
            await SetAlert(ctx, level);
            await Expect(ctx, officer, "А.М.Ц.И.И, откройте арсенал", PulsarDecisionOutcome.Opened, ctx.ArmoryDoor, true);
            await Close(ctx, ctx.ArmoryDoor);
        }

        // Green: "specify the threat", then a threat phrase opens.
        await SetAlert(ctx, "green");
        await Expect(ctx, officer, "Пульсар, открой оружейную", PulsarDecisionOutcome.AskThreat, ctx.ArmoryDoor, false, "Security");
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reply, Is.EqualTo("Уточните уровень угрозы."));
        await pair.RunSeconds(2);
        await Expect(ctx, officer, "на станции слизни, много", PulsarDecisionOutcome.Opened, ctx.ArmoryDoor, true, "Security");
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("armory-threat-named"));
        await Close(ctx, ctx.ArmoryDoor);

        // Blue, threat already in the request: open at once.
        await SetAlert(ctx, "blue");
        await Expect(ctx, officer, "Пульсар, открой оружейную, агент синдиката в бриге", PulsarDecisionOutcome.Opened, ctx.ArmoryDoor, true);
        await Close(ctx, ctx.ArmoryDoor);

        // Yellow, no threat in the follow-up: it keeps waiting.
        await SetAlert(ctx, "yellow");
        await Expect(ctx, officer, "Пульсар, открой оружейную", PulsarDecisionOutcome.AskThreat, ctx.ArmoryDoor, false);
        Assert.That(await Say(ctx, officer, "ну очень надо"), Is.Null);
        Assert.That(await IsOpen(ctx, ctx.ArmoryDoor), Is.False);
        await Expect(ctx, officer, "король крыс в техах", PulsarDecisionOutcome.Opened, ctx.ArmoryDoor, true);
        await Close(ctx, ctx.ArmoryDoor);

        // Not security: no.
        await SetAlert(ctx, "red");
        await Expect(ctx, passenger, "Пульсар, открой оружейную", PulsarDecisionOutcome.Denied, ctx.ArmoryDoor, false);

        // The warden has armory access on the ID: rule 1, whatever the manifest says.
        await server.WaitPost(() =>
            ctx.Manifest["Warden"] = records.AddRecordEntry(ctx.Station, new GeneralStationRecord { Name = "W", JobPrototype = "Warden" }));
        await SetAlert(ctx, "green");
        await Expect(ctx, warden, "Пульсар, открой оружейную", PulsarDecisionOutcome.Opened, ctx.ArmoryDoor, true);
        Assert.That(ctx.Pulsar.Comp.Decisions[^1].Reason, Is.EqualTo("own-access"));

        await pair.CleanReturnAsync();
    }

    private sealed class LinesShell(IConsoleHost host) : IConsoleShell
    {
        public readonly List<string> Lines = new();
        public readonly List<string> Errors = new();
        public IConsoleHost ConsoleHost { get; } = host;
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
}
