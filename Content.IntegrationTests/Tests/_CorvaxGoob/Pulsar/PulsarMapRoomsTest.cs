using System.Collections.Generic;
using System.Linq;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.StationRecords;
using Content.Shared.Maps;
using Content.Shared.Inventory;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 5.3: «открой мостик», «открой медбей» and the other rooms of the table find the right doors on the real
/// station maps (Box first: the bridge was not found there).
/// </summary>
[TestFixture]
public sealed class PulsarMapRoomsTest
{
    /// <summary>The room, the line, and how the room's own doors are named on the maps.</summary>
    private static readonly (string Room, string Line, string DoorName)[] Lines =
    {
        ("Bridge", "открой мостик", "Bridge"),
        ("CaptainQuarters", "открой каюту капитана", "Captain"),
        ("Medbay", "открой медбей", "Medbay"),
        ("Brig", "открой бриг", "Brig"),
        ("Armory", "открой оружейную", "Armory"),
        ("Science", "открой рнд", "Research"),
        ("Cargo", "открой карго", "Cargo"),
        ("Engineering", "открой инженерный отдел", "Engineering"),
        ("Atmospherics", "открой атмос", "Atmos"),
        ("Bar", "открой бар", "Bar"),
        ("Kitchen", "открой кухню", "Kitchen"),
        ("Hydroponics", "открой гидропонику", "Hydroponics"),
        ("Chemistry", "открой химлабораторию", "Chemistry"),
        ("HeadOfPersonnelOffice", "открой кабинет главы персонала", "Head of Personnel"),
        ("Chapel", "открой церковь", "Chapel"),
        ("Janitor", "открой подсобку уборщика", "Janitor"),
        ("Salvage", "открой утилизаторскую", "Salvage"),
        ("Detective", "открой кабинет детектива", "Detective"),
    };

    [Test]
    [TestCase("Box")]
    [TestCase("Meta")]
    [TestCase("Bagel")]
    [TestCase("Fland")]
    public async Task RoomsResolveOnMap(string mapProto)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var inventory = server.System<InventorySystem>();
        var xforms = server.System<SharedTransformSystem>();

        MapId mapId = default;
        // Map data errors of the station maps themselves (bad device links) are not this test's business.
        var level = pair.ServerLogHandler.FailureLevel;
        pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
        await server.WaitPost(() =>
        {
            ticker.LoadGameMap(protos.Index<GameMapPrototype>(mapProto), out mapId,
                DeserializationOptions.Default with { InitializeMaps = true });
        });
        await pair.RunTicksSync(5);
        pair.ServerLogHandler.FailureLevel = level;

        Entity<PulsarAutonomousAiComponent> ai = default;
        await server.WaitPost(() =>
        {
            var q = entMan.EntityQueryEnumerator<PulsarAutonomousAiComponent, TransformComponent>();
            while (q.MoveNext(out var uid, out var comp, out var xform))
            {
                if (xform.MapID == mapId)
                    ai = (uid, comp);
            }
        });
        Assert.That(ai.Owner.Valid, $"{mapProto} has an AI core");

        await pair.RunSeconds(5);
        await server.WaitPost(() => pulsar.RefreshNow(ai));
        await pair.RunSeconds(20);

        EntityUid speaker = default;
        await server.WaitPost(() =>
        {
            speaker = entMan.SpawnEntity("MobHuman", entMan.GetComponent<TransformComponent>(ai).Coordinates);
            var id = entMan.SpawnEntity("CaptainIDCard", entMan.GetComponent<TransformComponent>(ai).Coordinates);
            inventory.TryEquip(speaker, id, "id", silent: true, force: true);
            // The captain is on the crew manifest of this station.
            var station = server.System<StationSystem>().GetOwningStation(ai.Owner);
            Assert.That(station, Is.Not.Null);
            server.System<StationRecordsSystem>().AddRecordEntry(station!.Value,
                new GeneralStationRecord { Name = "Тест Капитан", JobPrototype = "Captain" });
        });

        await server.WaitAssertion(() =>
        {
            var doors = ai.Comp.Grids.Values.Sum(m => m.Doors.Count);
            var seen = ai.Comp.Grids.Values.Sum(m => m.Doors.Values.Count(d => m.IsSeen(d.Tile)));
            TestContext.Out.WriteLine($"{mapProto}: doors known {doors}, seen {seen}, beacons {ai.Comp.Beacons.Count}");
            string Desc(EntityUid d) => d.Valid ? $"{entMan.GetComponent<MetaDataComponent>(d).EntityName}@{xforms.GetWorldPosition(d)}" : "-";
            string Proto(EntityUid d) => entMan.GetComponent<MetaDataComponent>(d).EntityPrototype?.ID ?? string.Empty;
            string NameOf(EntityUid d) => entMan.GetComponent<MetaDataComponent>(d).EntityName;
            // Doors seen on camera so far (the AI only knows those).
            var known = ai.Comp.Grids.Values.SelectMany(m => m.Doors.Values).Select(d => d.Door)
                .Where(d => !entMan.Deleted(d)).ToList();
            var found = 0;
            foreach (var (room, line, doorName) in Lines)
            {
                var r = pulsar.ResolveDoorRequest(ai, speaker, line);
                TestContext.Out.WriteLine($"  {room}: {r.Result} '{r.Target}' door={Desc(r.Door)} all=[{string.Join("; ", r.All.Select(Desc))}]");
                if (r.Result is "Found" or "Ambiguous")
                    found++;
                var picked = r.All.Append(r.Door).Where(d => d.Valid).ToList();
                foreach (var d in picked)
                    Assert.That(Proto(d), Does.Not.Contain("Firelock"), $"{mapProto} {room}: a firelock is not an entrance");

                // A door named after the room is known: that is the one (or one of those offered).
                bool Main(EntityUid d) => !Proto(d).Contains("Firelock") && !Proto(d).Contains("Maint") && !Proto(d).Contains("Windoor")
                                          && !Proto(d).Contains("Shutter") && !NameOf(d).Contains("Maint") && !NameOf(d).Contains("Desk");
                var named = known.Where(d => NameOf(d).Contains(doorName) && Main(d)).ToList();
                if (named.Count > 0)
                    Assert.That(picked.Any(d => NameOf(d).Contains(doorName)), $"{mapProto} {room}: expected a «{doorName}» door, got {string.Join("; ", picked.Select(Desc))}");
            }

            Assert.That(found, Is.GreaterThanOrEqualTo(Lines.Length * 2 / 3), $"{mapProto}: most rooms found");

            // The bridge: a door of the bridge itself (Command access, the «Мостик» beacon or a «Bridge» name),
            // an airlock, not a maintenance hatch or the briefing room.
            var bridge = pulsar.ResolveDoorRequest(ai, speaker, "открой мостик");
            Assert.That(bridge.Result, Is.EqualTo("Found").Or.EqualTo("Ambiguous"), $"{mapProto}: bridge");
            var bridgeDoor = bridge.Door.Valid ? bridge.Door : bridge.All[0];
            Assert.That(Proto(bridgeDoor), Does.Contain("Command").And.Not.Contain("Maint"), $"{mapProto}: {Desc(bridgeDoor)}");
            var bridgeNamed = ai.Comp.Grids.Values.SelectMany(m => m.Doors.Values)
                .Where(d => entMan.GetComponent<MetaDataComponent>(d.Door).EntityName.Contains("Bridge")
                            && !Proto(d.Door).Contains("Maint")).Select(d => d.Door).ToList();
            if (bridgeNamed.Count > 0)
                Assert.That(bridgeNamed, Does.Contain(bridgeDoor), $"{mapProto}: a «Bridge» door, got {Desc(bridgeDoor)}");
        });

        // And really: «Пульсар, открой мостик» from the captain opens the bridge entrance (both leaves).
        var before = 0;
        await server.WaitPost(() =>
        {
            before = ai.Comp.Decisions.Count;
            pulsar.HandleSpeech(ai, speaker, "Пульсар, открой мостик", null, ai.Owner);
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var d = ai.Comp.Decisions.Skip(before).LastOrDefault();
            Assert.That(d, Is.Not.Null, $"{mapProto}: a decision");
            TestContext.Out.WriteLine($"{mapProto}: «открой мостик» -> {d!.Outcome} {d.Reason} «{d.Reply}»");
            if (d.Outcome == PulsarDecisionOutcome.Opened)
            {
                var door = entMan.GetComponent<Content.Shared.Doors.Components.DoorComponent>(d.Door!.Value);
                Assert.That(door.State, Is.Not.EqualTo(Content.Shared.Doors.Components.DoorState.Closed), $"{mapProto}: the bridge door is open");
                var pos = xforms.GetWorldPosition(d.Door!.Value);
                var partners = entMan.AllEntities<Content.Shared.Doors.Components.DoorComponent>()
                    .Where(p => p.Owner != d.Door && xforms.GetMapCoordinates(p.Owner).MapId == mapId
                                && entMan.GetComponent<MetaDataComponent>(p.Owner).EntityName == entMan.GetComponent<MetaDataComponent>(d.Door!.Value).EntityName
                                && (xforms.GetWorldPosition(p.Owner) - pos).Length() <= 3.2f)
                    .ToList();
                foreach (var partner in partners)
                    Assert.That(partner.Comp.State, Is.Not.EqualTo(Content.Shared.Doors.Components.DoorState.Closed), $"{mapProto}: the other leaf too");
            }
            else
            {
                Assert.That(d.Outcome, Is.EqualTo(PulsarDecisionOutcome.Clarify).Or.EqualTo(PulsarDecisionOutcome.NotControllable),
                    $"{mapProto}: {d.Outcome} {d.Reason}");
            }
        });

        await pair.CleanReturnAsync();
    }
}
