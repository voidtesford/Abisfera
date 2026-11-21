using System.Collections.Generic;
using System.Linq;
using Content.Goobstation.Common.CCVar;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Server.Gravity;
using Content.Shared.Gravity;
using Content.Server._CorvaxGoob.Pulsar.Units;
using Content.Shared.Doors.Components;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._CorvaxGoob.Pulsar.PulsarRequestsTest;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 5.2: every kind of unit linked to the AI really walks to an order,
/// through walls and airlocks it has no access to (the AI opens them on the route).
/// Three rooms: west (x &lt; -6), middle, east (x &gt; 6); airlocks at (-6, 0) and (6, 0).
/// </summary>
[TestFixture]
[NonParallelizable]
[TestOf(typeof(PulsarUnitsSystem))]
public sealed class PulsarMovementTest
{
    public static readonly string[] Units =
    {
        "BorgChassisGeneric", "BorgChassisEngineer", "BorgChassisMedical", "BorgChassisService",
        "BorgChassisJanitor", "BorgChassisMining",
        "MobMedibot", "MobWeldbot", "MobFireBot", "MobCleanBot",
    };

    internal static async Task<(EntityUid West, EntityUid East)> Rooms(Ctx ctx, bool northWestDoor = false)
    {
        var server = ctx.Pair.Server;
        var power = server.System<SharedPowerReceiverSystem>();
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        EntityUid west = default, east = default;
        await server.WaitPost(() =>
        {
            // The test pool turns NPC pathfinding off (Goob cvar); units need it, as on a live server. And gravity.
            server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, false);
            var gravity = ctx.EntMan.EnsureComponent<GravityComponent>(ctx.Map.Grid);
            server.System<GravitySystem>().EnableGravity(ctx.Map.Grid, gravity);

            for (var y = -12; y <= 12; y++)
            {
                if (y == 0)
                    continue;
                if (northWestDoor && y == 8)
                {
                    var spare = ctx.EntMan.SpawnEntity("AirlockEngineeringLocked", ctx.At(-6, y));
                    power.SetNeedsPower(spare, false);
                }
                else
                    ctx.EntMan.SpawnEntity("WallSolid", ctx.At(-6, y));
                ctx.EntMan.SpawnEntity("WallSolid", ctx.At(6, y));
            }

            west = ctx.EntMan.SpawnEntity("AirlockEngineeringLocked", ctx.At(-6, 0));
            east = ctx.EntMan.SpawnEntity("AirlockScienceLocked", ctx.At(6, 0));
            power.SetNeedsPower(west, false);
            power.SetNeedsPower(east, false);
        });
        await ctx.Pair.RunSeconds(2);
        await server.WaitPost(() => pulsar.RefreshNow(ctx.Pulsar));
        await ctx.Pair.RunSeconds(1);
        return (west, east);
    }

    internal static string Diag(IEntityManager em, EntityUid unit)
    {
        var s = $"activeNpc={em.HasComponent<Content.Shared.NPC.ActiveNPCComponent>(unit)}";
        if (em.TryGetComponent<Content.Server.NPC.Components.NPCSteeringComponent>(unit, out var st))
            s += $" steer={st.Status} path={st.CurrentPath.Count} flags={st.Flags} failed={st.FailedPathCount} pf={st.Pathfind}";
        else
            s += " steer=none";
        if (em.TryGetComponent<Content.Shared.Movement.Components.InputMoverComponent>(unit, out var im))
            s += $" canMove={im.CanMove}";
        if (em.TryGetComponent<Content.Shared.Movement.Components.MovementSpeedModifierComponent>(unit, out var ms))
            s += $" speed={ms.CurrentSprintSpeed:0.0}";
        if (em.TryGetComponent<Content.Shared.Silicons.Borgs.Components.BorgChassisComponent>(unit, out var ch))
            s += $" active={ch.Active}";
        if (em.TryGetComponent<PulsarLinkedComponent>(unit, out var l))
            s += $" task={l.Task?.Kind}/{l.Task?.Stage} order={(l.Order != null)} driving={l.Driving}";
        if (em.TryGetComponent<Content.Server.NPC.HTN.HTNComponent>(unit, out var htn))
            s += $" plan={htn.Plan?.CurrentOperator.GetType().Name} goto={htn.Blackboard.ContainsKey(PulsarUnitsSystem.GotoKey)}";
        return s;
    }

    [Test]
    [TestCaseSource(nameof(Units))]
    public async Task WalksThroughAirlocks(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var (west, east) = await Rooms(ctx);

        EntityUid unit = default;
        await server.WaitPost(() => unit = ctx.EntMan.SpawnEntity(proto, ctx.At(-10, 4)));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(units.TryLink(unit, ctx.Core, out var fail), fail));
        await pair.RunTicksSync(2);

        var target = ctx.At(10, -4);
        await server.WaitAssertion(() =>
        {
            var link = (unit, ctx.EntMan.GetComponent<PulsarLinkedComponent>(unit));
            Assert.That(units.TryOrder(link, new PulsarUnitOrder
            {
                Target = target, Place = "восточный отсек", Rank = PulsarOrderRank.Captain, By = "test",
            }, out _));
        });

        float Dist() => (xforms.GetWorldPosition(unit) - xforms.ToMapCoordinates(target).Position).Length();

        var start = 0f;
        await server.WaitPost(() => start = Dist());
        var reached = false;
        var trace = new List<string>();
        for (var i = 0; i < 90 && !reached; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var d = Dist();
                if (i % 5 == 0 || i < 3)
                    trace.Add($"{i}s: {xforms.GetWorldPosition(unit)} d={d:0.0} " +
                              $"west={ctx.EntMan.GetComponent<DoorComponent>(west).State} east={ctx.EntMan.GetComponent<DoorComponent>(east).State} " + Diag(ctx.EntMan, unit));
                reached = d < 2f;
            });
        }

        TestContext.Out.WriteLine($"{proto} ({units.GetKind(unit)}): start {start:0.0}\n" + string.Join("\n", trace));
        await pair.RunSeconds(3);
        await server.WaitPost(() => server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
        Assert.That(reached, $"{proto} did not reach the east room");
        await server.WaitAssertion(() =>
        {
            var link = ctx.EntMan.GetComponent<PulsarLinkedComponent>(unit);
            Assert.That(link.DoorsOpened, Is.GreaterThanOrEqualTo(2), "the AI opened both airlocks on the route");
            Assert.That(ctx.EntMan.GetComponent<DoorComponent>(west).State, Is.EqualTo(DoorState.Closed).Or.EqualTo(DoorState.Closing),
                "and closed them behind it");
            Assert.That(link.Order!.Arrived);
            Assert.That(link.Where, Is.Not.Empty, "it knows where it is");
        });

        await pair.CleanReturnAsync();
    }

    private static async Task<EntityUid> Linked(Ctx ctx, string proto, int x, int y)
    {
        var units = ctx.Pair.Server.System<PulsarUnitsSystem>();
        EntityUid unit = default;
        await ctx.Pair.Server.WaitPost(() => unit = ctx.EntMan.SpawnEntity(proto, ctx.At(x, y)));
        await ctx.Pair.RunTicksSync(5);
        await ctx.Pair.Server.WaitAssertion(() => Assert.That(units.TryLink(unit, ctx.Core, out var fail), fail));
        await ctx.Pair.RunTicksSync(2);
        return unit;
    }

    private static async Task Order(Ctx ctx, EntityUid unit, EntityCoordinates target, PulsarUnitKind? kind = null, int group = 0)
    {
        var units = ctx.Pair.Server.System<PulsarUnitsSystem>();
        await ctx.Pair.Server.WaitAssertion(() =>
        {
            var link = (unit, ctx.EntMan.GetComponent<PulsarLinkedComponent>(unit));
            Assert.That(units.TryOrder(link, new PulsarUnitOrder
            {
                Target = target, Place = "восточный отсек", Rank = PulsarOrderRank.Captain, By = "test", Group = group,
                Kinds = kind is { } k ? new HashSet<PulsarUnitKind> { k } : null,
            }, out _));
        });
    }

    private static async Task<bool> Reaches(Ctx ctx, EntityUid unit, EntityCoordinates target, int seconds)
    {
        var xforms = ctx.Pair.Server.System<SharedTransformSystem>();
        var reached = false;
        for (var i = 0; i < seconds && !reached; i++)
        {
            await ctx.Pair.RunSeconds(1);
            var n = i;
            await ctx.Pair.Server.WaitPost(() =>
            {
                reached = (xforms.GetWorldPosition(unit) - xforms.ToMapCoordinates(target).Position).Length() < 2f;
                if (n % 3 == 0 || reached)
                    TestContext.Out.WriteLine($"{n}s {ctx.EntMan.ToPrettyString(unit)} at {xforms.GetWorldPosition(unit)}: {Diag(ctx.EntMan, unit)}");
            });
        }

        return reached;
    }

    /// <summary>The direct airlock is bolted (the AI cannot open it): the route goes round through the other one.</summary>
    [Test]
    public async Task RoutesAroundBoltedAirlock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        var (west, _) = await Rooms(ctx, northWestDoor: true);
        await server.WaitPost(() =>
        {
            var bolts = ctx.EntMan.GetComponent<DoorBoltComponent>(west);
            server.System<Content.Shared.Doors.Systems.SharedDoorSystem>().SetBoltsDown((west, bolts), true);
        });

        var unit = await Linked(ctx, "MobMedibot", -10, -2);
        var target = ctx.At(-2, -2);
        await Order(ctx, unit, target);
        var reached = await Reaches(ctx, unit, target, 60);
        await server.WaitPost(() => server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
        Assert.That(reached, "went round through the northern airlock");
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.GetComponent<DoorComponent>(west).State, Is.EqualTo(DoorState.Closed), "the bolted one stayed shut");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An unpowered airlock (the AI cannot open it): a cyborg with a crowbar pries it open.</summary>
    [Test]
    public async Task BorgPriesUnpoweredAirlock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        var (west, _) = await Rooms(ctx);
        await server.WaitPost(() => server.System<SharedPowerReceiverSystem>().SetNeedsPower(west, true));
        await pair.RunTicksSync(5);

        var unit = await Linked(ctx, "BorgChassisEngineer", -10, 1);
        var target = ctx.At(-2, 2);
        await Order(ctx, unit, target);
        var reached = await Reaches(ctx, unit, target, 60);
        await server.WaitPost(() => server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
        Assert.That(reached, "pried the airlock and went through");
        await server.WaitAssertion(() =>
            Assert.That(ctx.EntMan.GetComponent<DoorComponent>(west).State, Is.EqualTo(DoorState.Open), "pried open (no power to close it)"));

        await pair.CleanReturnAsync();
    }

    /// <summary>A unit that cannot get there (shut in) hands the order to the nearest other free unit of the kind.</summary>
    [Test]
    public async Task HandsOverWhenStuck()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();
        var (west, _) = await Rooms(ctx);
        await server.WaitPost(() =>
        {
            var bolts = ctx.EntMan.GetComponent<DoorBoltComponent>(west);
            server.System<Content.Shared.Doors.Systems.SharedDoorSystem>().SetBoltsDown((west, bolts), true);
        });

        var shut = await Linked(ctx, "MobMedibot", -10, 4);
        var free = await Linked(ctx, "MobMedibot", 0, -6);
        var target = ctx.At(2, 6);
        var group = 0;
        await server.WaitPost(() =>
        {
            group = units.NewGroup(ctx.Core, "восточный отсек", ReportChannel.Medical, report: true);
            units.JoinGroup(group, shut);
        });
        await Order(ctx, shut, target, PulsarUnitKind.Medibot, group);

        var handed = false;
        for (var i = 0; i < 60 && !handed; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() => handed = ctx.EntMan.GetComponent<PulsarLinkedComponent>(free).Order != null);
        }

        Assert.That(handed, "the free medibot took the order over");
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.GetComponent<PulsarLinkedComponent>(shut).Order, Is.Null, "the shut-in one dropped it");
            Assert.That(ctx.EntMan.GetComponent<PulsarLinkedComponent>(free).Order!.Tried, Does.Contain(shut));
        });
        var reached = await Reaches(ctx, free, ctx.EntMan.GetComponent<PulsarLinkedComponent>(free).Order!.Target, 40);
        await server.WaitPost(() => server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
        Assert.That(reached);

        await pair.CleanReturnAsync();
    }

    /// <summary>Several units to one place get separate spots; the units' sensors feed the AI's map.</summary>
    [Test]
    public async Task SpotsAndSensors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "MedicalDoctor");
        var server = pair.Server;
        await Rooms(ctx);

        var bots = new List<EntityUid>();
        for (var i = 0; i < 3; i++)
            bots.Add(await Linked(ctx, "MobMedibot", -10, 2 + i * 2));
        var medic = await Person(ctx, "MedicalIDCard", 2, 2);

        await pair.RunSeconds(1.5f);
        // Sensors: what the bots in the west room see is on the AI's map, what is behind the wall is not.
        await server.WaitAssertion(() =>
        {
            var mem = ctx.Pulsar.Comp.Grids[ctx.Map.Grid.Owner];
            Assert.That(mem.UnitSeenTiles, Is.GreaterThan(0));
            Assert.That(mem.IsUnitSeen(new Vector2i(-9, 2)), "next to a bot");
            Assert.That(mem.IsUnitSeen(new Vector2i(-4, 2)), Is.False, "behind the wall");
            Assert.That(mem.IsUnitSeen(new Vector2i(-6, 1)), "the wall itself");
            Assert.That(ctx.EntMan.GetComponent<PulsarLinkedComponent>(bots[0]).Where, Is.Not.Empty);
        });

        var d = await Say(ctx, medic, "Пульсар, пришли трёх медиботов сюда");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsSent), d.Reply);

        var targets = new List<EntityCoordinates>();
        await server.WaitAssertion(() =>
        {
            foreach (var bot in bots)
                targets.Add(ctx.EntMan.GetComponent<PulsarLinkedComponent>(bot).Order!.Target);
            Assert.That(targets.Select(t => t.Position).Distinct().Count(), Is.EqualTo(3), "three different spots");
            var group = ctx.EntMan.GetComponent<PulsarLinkedComponent>(bots[0]).Order!.Group;
            Assert.That(group, Is.Not.Zero);
            Assert.That(bots.All(b => ctx.EntMan.GetComponent<PulsarLinkedComponent>(b).Order!.Group == group), "one group");
        });

        var allThere = true;
        for (var i = 0; i < bots.Count; i++)
            allThere &= await Reaches(ctx, bots[i], targets[i], 40);
        await server.WaitPost(() => server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
        Assert.That(allThere, "all three got there through the airlock without jamming");

        await pair.CleanReturnAsync();
    }

    /// <summary>Things in the way (a crate before the airlock, a person in the middle room): it still gets there.</summary>
    [Test]
    [TestCase("MobMedibot")]
    [TestCase("BorgChassisMedical")]
    public async Task GetsPastObstacles(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        await Rooms(ctx);
        await server.WaitPost(() =>
        {
            // A wall of crates across the straight line to the airlock and a person in the middle room.
            ctx.EntMan.SpawnEntity("CrateGenericSteel", ctx.At(-8, -1));
            ctx.EntMan.SpawnEntity("CrateGenericSteel", ctx.At(-8, 0));
            ctx.EntMan.SpawnEntity("CrateGenericSteel", ctx.At(-8, 1));
            ctx.EntMan.SpawnEntity("MobHuman", ctx.At(0, 0));
        });

        var unit = await Linked(ctx, proto, -10, 0);
        var target = ctx.At(10, 0);
        await Order(ctx, unit, target);
        var reached = await Reaches(ctx, unit, target, 90);
        await server.WaitPost(() => server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
        Assert.That(reached, $"{proto} got past the crates and the person");

        await pair.CleanReturnAsync();
    }

    [Test]
    [TestCase("BorgChassisMedical")]
    [TestCase("BorgChassisEngineer")]
    public async Task ShovesCrateOutOfDoorway(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        await Rooms(ctx);
        // The only way to the airlock is through the tile with the crate.
        await server.WaitPost(() => ctx.EntMan.SpawnEntity("CrateGenericSteel", ctx.At(-7, 0)));

        var unit = await Linked(ctx, proto, -10, 0);
        var target = ctx.At(10, 0);
        await Order(ctx, unit, target);
        var reached = await Reaches(ctx, unit, target, 90);
        await server.WaitPost(() => server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
        Assert.That(reached, $"{proto} shoved the crate out of the doorway");

        await pair.CleanReturnAsync();
    }
}
