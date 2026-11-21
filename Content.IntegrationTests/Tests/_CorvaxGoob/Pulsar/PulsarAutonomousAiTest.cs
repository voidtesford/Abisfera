using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Server.Chat.Systems;
using Content.Shared.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._CorvaxGoob.CCCVars;
using Content.Shared.Chat;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Radio.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Server.Console;
using Robust.Shared.Console;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 1: takeover/handoff, the memory map from camera vision, people, hearing.
/// </summary>
[TestFixture]
[TestOf(typeof(PulsarAutonomousAiSystem))]
public sealed class PulsarAutonomousAiTest
{
    private const string Core = "PlayerStationAi";

    private sealed class CaptureShell(IConsoleHost host) : IConsoleShell
    {
        public readonly List<string> Lines = new();
        public readonly List<string> Errors = new();
        public IConsoleHost ConsoleHost { get; } = host;
        public bool IsLocal => true;
        public bool IsServer => true;
        public ICommonSession? Player => null;
        public void ExecuteCommand(string command) => ConsoleHost.ExecuteCommand(command);
        public void RemoteExecuteCommand(string command) { }
        public void WriteLine(string text) => Lines.Add(text);
        public void WriteLine(FormattedMessage message) => Lines.Add(message.ToString());
        public void WriteError(string text) => Errors.Add(text);
        public void Clear() { }
    }

    private static CaptureShell Run(IServerConsoleHost host, string command, string args)
    {
        var shell = new CaptureShell(host);
        var argv = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        host.AvailableCommands[command].Execute(shell, args, argv);
        return shell;
    }

    private sealed class Ctx
    {
        public TestPair Pair = default!;
        public TestMapData Map = default!;
        public IEntityManager EntMan = default!;
        public EntityUid Core;

        public EntityCoordinates At(int x, int y) => new(Map.Grid.Owner, x + 0.5f, y + 0.5f);

        public Entity<PulsarAutonomousAiComponent> Pulsar => (Core, EntMan.GetComponent<PulsarAutonomousAiComponent>(Core));
    }

    private static async Task<Ctx> Setup(TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var ctx = new Ctx { Pair = pair, Map = map, EntMan = server.EntMan };
        var mapSys = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            mapSys.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            ctx.Core = server.EntMan.SpawnEntity(Core, ctx.At(0, 0));
        });

        return ctx;
    }

    [Test]
    public async Task TakeoverAndHandoff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await Setup(pair);
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var mindSys = server.System<SharedMindSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var entMan = server.EntMan;
        var pulsarName = loc.GetString("pulsar-ai-name");
        EntityUid brain = default;
        EntityUid mind = default;

        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<PulsarAutonomousAiComponent>(ctx.Core), "the job-spawn AI core carries the module");
            Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Active), "an empty core is taken by the module");
            Assert.That(entMan.GetComponent<MetaDataComponent>(ctx.Core).EntityName, Is.EqualTo(pulsarName));
            Assert.That(entMan.HasComponent<ActiveRadioComponent>(ctx.Core), "listens to the radio while active");
        });

        // A brain without a player changes nothing.
        await server.WaitPost(() =>
        {
            brain = entMan.SpawnInContainerOrDrop("StationAiBrain", ctx.Core, StationAiCoreComponent.Container);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Active));
            Assert.That(entMan.GetComponent<MetaDataComponent>(ctx.Core).EntityName, Is.EqualTo(pulsarName),
                "keeps its name even though the inserted brain renamed the core");
        });

        // A player takes the AI: the module stops and the core gets the AI's name back.
        await server.WaitPost(() =>
        {
            mind = mindSys.CreateMind(null, "test AI player");
            mindSys.TransferTo(mind, brain);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(pulsar.IsPlayerControlled(ctx.Core), Is.True);
            Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Suspended));
            Assert.That(entMan.GetComponent<MetaDataComponent>(ctx.Core).EntityName,
                Is.EqualTo(entMan.GetComponent<MetaDataComponent>(brain).EntityName));
            Assert.That(entMan.HasComponent<ActiveRadioComponent>(ctx.Core), Is.False);
            Assert.That(entMan.GetComponent<Content.Shared.Mind.Components.MindContainerComponent>(brain).Mind, Is.EqualTo(mind),
                "the player's mind is never touched");
        });

        // The player leaves: grace period first, then the module comes back.
        await server.WaitPost(() =>
        {
            pulsar.SetVacancyGrace(ctx.Pulsar, TimeSpan.FromSeconds(2));
            mindSys.TransferTo(mind, null, createGhost: false);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Standby)));
        await pair.RunSeconds(2.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Active));
            Assert.That(entMan.GetComponent<MetaDataComponent>(ctx.Core).EntityName, Is.EqualTo(pulsarName));
        });

        // Global switch.
        await server.WaitPost(() => cfg.SetCVar(AbisferaCVars.PulsarEnabled, false));
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Disabled));
            Assert.That(entMan.GetComponent<MetaDataComponent>(ctx.Core).EntityName,
                Is.EqualTo(entMan.GetComponent<MetaDataComponent>(brain).EntityName));
        });
        await server.WaitPost(() => cfg.SetCVar(AbisferaCVars.PulsarEnabled, true));
        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(ctx.Pulsar.Comp.State, Is.EqualTo(PulsarState.Active)));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MemoryMapAndPeople()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await Setup(pair);
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var inventory = server.System<InventorySystem>();
        var doors = server.System<Content.Shared.Doors.Systems.SharedDoorSystem>();
        var mapSys = server.System<SharedMapSystem>();
        var console = server.ResolveDependency<IServerConsoleHost>();
        var entMan = server.EntMan;
        var grid = ctx.Map.Grid.Owner;
        EntityUid wall = default, crew = default, humanoid = default, enemy = default, ert = default, far = default, airlock = default;

        await server.WaitPost(() =>
        {
            mapSys.SetTile(ctx.Map.Grid, new Vector2i(2, -3), Tile.Empty);

            wall = entMan.SpawnEntity("WallSolid", ctx.At(3, 0));
            entMan.SpawnEntity("Table", ctx.At(0, 3));
            entMan.SpawnEntity("Window", ctx.At(-2, 3));
            airlock = entMan.SpawnEntity("AirlockCommandLocked", ctx.At(-3, 0));

            crew = entMan.SpawnEntity("MobHuman", ctx.At(1, 1));
            var id = entMan.SpawnEntity("PassengerIDCard", ctx.At(1, 1));
            Assert.That(inventory.TryEquip(crew, id, "id", silent: true, force: true));

            humanoid = entMan.SpawnEntity("MobHuman", ctx.At(-1, 1));

            enemy = entMan.SpawnEntity("MobHuman", ctx.At(1, -1));
            var suit = entMan.SpawnEntity("ClothingOuterHardsuitSyndie", ctx.At(1, -1));
            Assert.That(inventory.TryEquip(enemy, suit, "outerClothing", silent: true, force: true));

            ert = entMan.SpawnEntity("MobHuman", ctx.At(-1, -1));
            var ertSuit = entMan.SpawnEntity("ClothingOuterHardsuitERTSecurity", ctx.At(-1, -1));
            Assert.That(inventory.TryEquip(ert, ertSuit, "outerClothing", silent: true, force: true));

            far = entMan.SpawnEntity("MobHuman", ctx.At(11, 11));
        });

        await pair.RunSeconds(7);
        await server.WaitAssertion(() =>
        {
            var p = ctx.Pulsar;
            Assert.That(p.Comp.State, Is.EqualTo(PulsarState.Active));
            Assert.That(p.Comp.Grids.ContainsKey(grid), "watches the grid it stands on when there is no station");

            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(1, 0)), Is.EqualTo(PulsarTileKind.Floor));
            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(3, 0)), Is.EqualTo(PulsarTileKind.Wall));
            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(0, 3)), Is.EqualTo(PulsarTileKind.Table));
            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(-2, 3)), Is.EqualTo(PulsarTileKind.Window));
            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(-3, 0)), Is.EqualTo(PulsarTileKind.Airlock));
            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(2, -3)), Is.EqualTo(PulsarTileKind.Space));
            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(11, 11)), Is.EqualTo(PulsarTileKind.Unknown),
                "outside camera range nothing is known");
            Assert.That(pulsar.IsTileInView(p, grid, new Vector2i(1, 1)), Is.True);

            Assert.That(pulsar.TryGetDoor(p, grid, new Vector2i(-3, 0), out var door), Is.True);
            Assert.That(door!.Door, Is.EqualTo(airlock));
            Assert.That(door.IsAirlock, Is.True);
            Assert.That(door.Access.Any(set => set.Contains("Command")), Is.True, $"command airlock access: {string.Join(" | ", door.Access.Select(s => string.Join("+", s)))}");

            Assert.That(p.Comp.Mobs.TryGetValue(crew, out var crewInfo), Is.True);
            Assert.That(crewInfo!.Kind, Is.EqualTo(PulsarMobKind.Crew));
            Assert.That(crewInfo.Job, Is.Not.Null.And.Not.Empty);
            Assert.That(p.Comp.Mobs[humanoid].Kind, Is.EqualTo(PulsarMobKind.Humanoid));
            Assert.That(p.Comp.Mobs[enemy].Kind, Is.EqualTo(PulsarMobKind.Enemy));
            Assert.That(p.Comp.Mobs[ert].Kind, Is.Not.EqualTo(PulsarMobKind.Enemy), "ERT hardsuits are built on syndicate ones but are not enemies");
            Assert.That(p.Comp.Mobs.ContainsKey(far), Is.False, "nobody outside the cameras is seen");
        });

        // The map is dynamic: the wall goes away, the airlock opens.
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(wall);
            doors.StartOpening(airlock);
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            var p = ctx.Pulsar;
            Assert.That(pulsar.GetKnownTile(p, grid, new Vector2i(3, 0)), Is.EqualTo(PulsarTileKind.Floor));
            Assert.That(pulsar.TryGetDoor(p, grid, new Vector2i(-3, 0), out var door), Is.True);
            Assert.That(door!.State, Is.Not.EqualTo(Content.Shared.Doors.Components.DoorState.Closed));
        });

        // Commands run and print the map.
        await server.WaitAssertion(() =>
        {
            var status = Run(console, "pulsar_status", "");
            var map = Run(console, "pulsar_map", "12");
            var heard = Run(console, "pulsar_heard", "5");
            TestContext.Out.WriteLine(string.Join("\n", status.Lines));
            TestContext.Out.WriteLine(string.Join("\n", map.Lines));
            Assert.That(status.Errors, Is.Empty);
            Assert.That(map.Errors, Is.Empty);
            Assert.That(heard.Errors, Is.Empty);
            var text = string.Join("\n", map.Lines);
            Assert.That(text, Does.Contain("@").And.Contain("E").And.Contain("C").And.Contain("T").And.Contain("Command"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Hearing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var ctx = await Setup(pair);
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var chat = server.System<ChatSystem>();
        var radio = server.System<RadioSystem>();
        var entMan = server.EntMan;
        EntityUid near = default, far = default;

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("TelecomServerFilled", ctx.At(-6, -6));
            near = entMan.SpawnEntity("MobHuman", ctx.At(2, 2));
            far = entMan.SpawnEntity("MobHuman", ctx.At(9, 0));
            entMan.EnsureComponent<TelecomExemptComponent>(near);
        });

        await pair.RunSeconds(4);
        await server.WaitPost(() => pulsar.RefreshNow(ctx.Pulsar));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var p = ctx.Pulsar;
            Assert.That(p.Comp.State, Is.EqualTo(PulsarState.Active));
            Assert.That(p.Comp.Channels, Does.Contain("Command").And.Contain("Security").And.Contain("Common"),
                "channels come from the telecom server keys");
            var active = entMan.GetComponent<ActiveRadioComponent>(ctx.Core);
            Assert.That(active.Channels.Select(c => c.Id).ToHashSet().SetEquals(p.Comp.Channels));
            Assert.That(p.Comp.Holopads, Does.Contain(ctx.Core), "the core itself is a holopad");
        });

        await server.WaitPost(() =>
        {
            chat.TrySendInGameICMessage(near, "пульсар, приём", InGameICChatType.Speak, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            chat.TrySendInGameICMessage(near, "тихий секрет", InGameICChatType.Whisper, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            chat.TrySendInGameICMessage(far, "далеко от голопада", InGameICChatType.Speak, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            radio.SendRadioMessage(near, "сб, код красный", "Security", near);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var heard = ctx.Pulsar.Comp.Heard;
            var text = string.Join("\n", heard.Select(h => $"{h.Kind} {h.Channel} {h.SpeakerName}: {h.Message}"));
            Assert.That(heard.Any(h => h.Kind == PulsarHeardKind.Holopad && h.Message.Contains("приём")), Is.True, text);
            Assert.That(heard.Any(h => h.Message.Contains("секрет")), Is.False, "whispers are not heard: " + text);
            Assert.That(heard.Any(h => h.Message.Contains("далеко")), Is.False, "speech further than 5 tiles from a holopad: " + text);
            Assert.That(heard.Any(h => h.Kind == PulsarHeardKind.Radio && h.Channel == "Security" && h.Message.Contains("код красный")), Is.True, text);
        });

        await pair.CleanReturnAsync();
    }
}
