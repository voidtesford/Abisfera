using System.Linq;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Shared.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.Pulsar.Units;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 5: drives the units linked to the station AI.
/// <para>Minibots keep their own HTN behaviour (inject, weld, douse, clean); the module only adds a high-priority
/// branch to their HTN (<c>PulsarGotoCompound</c>) through the blackboard key <see cref="GotoKey"/>: «go there
/// first». It opens access doors on the way (<see cref="NPCAccessDoorBlockedEvent"/>), brings a unit back into
/// its assigned area when it roams off, and sends free units to fires, patients and breaches.</para>
/// <para>Cyborgs without a mind are driven directly (NPC steering + a small task machine): what a borg can do
/// comes from the items of its modules (healing kits / hypospray → heal; welder → repair; anomaly scanner +
/// wrench → neutralise an anomaly with an A.P.E.). A borg with a mind is never driven.</para>
/// </summary>
public sealed partial class PulsarUnitsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedBorgSystem _borg = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly StationSystem _station = default!;

    /// <summary>Blackboard: where the module wants the minibot to go (EntityCoordinates).</summary>
    public const string GotoKey = "PulsarGoto";
    /// <summary>Blackboard: how close is "there" (float).</summary>
    public const string GotoRangeKey = "PulsarGotoRange";

    public static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);
    /// <summary>No progress on a walk for this long: give the order up.</summary>
    public static readonly TimeSpan StuckTime = TimeSpan.FromSeconds(25);
    /// <summary>A one-time visit ends this long after arriving.</summary>
    public static readonly TimeSpan VisitTime = TimeSpan.FromMinutes(3);
    /// <summary>An autonomous dispatch ends this long after arriving.</summary>
    public static readonly TimeSpan AutonomousVisitTime = TimeSpan.FromSeconds(45);
    public const float ArriveRange = 1.5f;

    private static readonly Dictionary<string, PulsarUnitKind> BotByRootTask = new()
    {
        ["MedibotCompound"] = PulsarUnitKind.Medibot,
        ["WeldbotCompound"] = PulsarUnitKind.Weldbot,
        ["FirebotCompound"] = PulsarUnitKind.Firebot,
        ["CleanbotCompound"] = PulsarUnitKind.Cleanbot,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PulsarLinkedComponent, NPCAccessDoorBlockedEvent>(OnDoorBlocked);
        SubscribeLocalEvent<PulsarLinkedComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<PulsarLinkedComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<PulsarLinkedComponent, ComponentShutdown>(OnLinkShutdown);

        InitializeConsole();
    }

    // ---- what is it ----

    public static bool IsMinibot(PulsarUnitKind kind) => kind <= PulsarUnitKind.Cleanbot;

    /// <summary>The kind of a unit the module can be linked to, or null.</summary>
    public PulsarUnitKind? GetKind(EntityUid uid)
    {
        if (TryComp<BorgChassisComponent>(uid, out var chassis))
            return BorgKind((uid, chassis));

        if (TryComp<HTNComponent>(uid, out var htn) && htn.RootTask.Task is { } task
            && BotByRootTask.TryGetValue(task.Id, out var kind))
            return kind;

        return null;
    }

    private PulsarUnitKind BorgKind(Entity<BorgChassisComponent> chassis)
    {
        // Science units are any chassis carrying an anomaly module (there is no science chassis type).
        if (HasModuleItem<Content.Shared.Anomaly.Components.AnomalyScannerComponent>(chassis))
            return PulsarUnitKind.BorgScience;

        if (TryComp<BorgSwitchableTypeComponent>(chassis, out var type) && type.SelectedBorgType is { } t)
        {
            return t.Id switch
            {
                "generic" => PulsarUnitKind.BorgGeneric,
                "engineering" => PulsarUnitKind.BorgEngineering,
                "medical" => PulsarUnitKind.BorgMedical,
                "service" => PulsarUnitKind.BorgService,
                "janitor" => PulsarUnitKind.BorgJanitor,
                "mining" => PulsarUnitKind.BorgMining,
                _ => PulsarUnitKind.BorgOther,
            };
        }

        return HasComp<BorgSwitchableTypeComponent>(chassis) ? PulsarUnitKind.BorgGeneric : PulsarUnitKind.BorgOther;
    }

    /// <summary>Whose staff outrank this unit.</summary>
    public static PulsarUnitDepartment Department(PulsarUnitKind kind) => kind switch
    {
        PulsarUnitKind.Medibot or PulsarUnitKind.BorgMedical => PulsarUnitDepartment.Medical,
        PulsarUnitKind.Weldbot or PulsarUnitKind.Firebot or PulsarUnitKind.BorgEngineering => PulsarUnitDepartment.Engineering,
        PulsarUnitKind.Cleanbot or PulsarUnitKind.BorgService or PulsarUnitKind.BorgJanitor => PulsarUnitDepartment.Service,
        PulsarUnitKind.BorgScience => PulsarUnitDepartment.Science,
        PulsarUnitKind.BorgMining => PulsarUnitDepartment.Cargo,
        _ => PulsarUnitDepartment.None,
    };

    public static string KindLoc(PulsarUnitKind kind) => kind switch
    {
        PulsarUnitKind.Medibot => "pulsar-unit-medibot",
        PulsarUnitKind.Weldbot => "pulsar-unit-weldbot",
        PulsarUnitKind.Firebot => "pulsar-unit-firebot",
        PulsarUnitKind.Cleanbot => "pulsar-unit-cleanbot",
        PulsarUnitKind.BorgGeneric => "pulsar-unit-borg-generic",
        PulsarUnitKind.BorgEngineering => "pulsar-unit-borg-engineering",
        PulsarUnitKind.BorgMedical => "pulsar-unit-borg-medical",
        PulsarUnitKind.BorgService => "pulsar-unit-borg-service",
        PulsarUnitKind.BorgJanitor => "pulsar-unit-borg-janitor",
        PulsarUnitKind.BorgMining => "pulsar-unit-borg-mining",
        PulsarUnitKind.BorgScience => "pulsar-unit-borg-science",
        _ => "pulsar-unit-borg-other",
    };

    /// <summary>A player is in it (borg brain with a mind, sentient bot, AI remote control): never drive it.</summary>
    public bool HasMind(EntityUid uid)
    {
        if (_mind.TryGetMind(uid, out _, out _))
            return true;

        if (TryComp<BorgChassisComponent>(uid, out var chassis) && chassis.BrainEntity is { } brain
            && _mind.TryGetMind(brain, out _, out _))
            return true;

        return false;
    }

    // ---- link / unlink ----

    /// <summary>The Pulsar core of the station the entity is on (the first one found), or null.</summary>
    public EntityUid? FindCore(EntityUid near)
    {
        var station = _station.GetOwningStation(near);
        EntityUid? fallback = null;
        var query = EntityQueryEnumerator<PulsarAutonomousAiComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (station != null && _station.GetOwningStation(uid) == station)
                return uid;
            fallback ??= uid;
        }

        return station == null ? fallback : null;
    }

    public bool IsCoreActive(EntityUid? core)
    {
        return core is { } c && TryComp<PulsarAutonomousAiComponent>(c, out var comp) && comp.State == PulsarState.Active;
    }

    public bool TryLink(EntityUid uid, EntityUid? core, out string failLoc)
    {
        failLoc = string.Empty;
        if (GetKind(uid) is not { } kind)
        {
            failLoc = "robotics-console-pulsar-fail-kind";
            return false;
        }

        if (IsMinibot(kind) && Transform(uid).Anchored)
        {
            failLoc = "robotics-console-pulsar-fail-anchored";
            return false;
        }

        core ??= FindCore(uid);
        if (core == null)
        {
            failLoc = "robotics-console-pulsar-fail-no-core";
            return false;
        }

        var link = EnsureComp<PulsarLinkedComponent>(uid);
        link.Core = core;
        link.Kind = kind;
        link.NextUpdate = _timing.CurTime;
        return true;
    }

    public void Unlink(EntityUid uid)
    {
        RemCompDeferred<PulsarLinkedComponent>(uid);
    }

    private void OnLinkShutdown(Entity<PulsarLinkedComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;
        Release(ent);
    }

    /// <summary>Lets go of the unit: its own HTN behaviour only, borg chassis back to how it was.</summary>
    private void Release(Entity<PulsarLinkedComponent> ent)
    {
        ClearBotGoto(ent);

        if (ent.Comp.Driving)
        {
            _steering.Unregister(ent);
            RemCompDeferred<ActiveNPCComponent>(ent);
            ent.Comp.Driving = false;
        }

        if (ent.Comp.Task is { } task)
            AbortTask(ent, task);
        ent.Comp.Task = null;

        if (ent.Comp.ActivatedChassis && TryComp<BorgChassisComponent>(ent, out var chassis) && !HasMind(ent))
            _borg.SetActive((ent, chassis), false);
        ent.Comp.ActivatedChassis = false;
    }

    private void OnAnchorChanged(Entity<PulsarLinkedComponent> ent, ref AnchorStateChangedEvent args)
    {
        // Wrenched down: the AI lets go of it.
        if (args.Anchored && IsMinibot(ent.Comp.Kind))
            Unlink(ent);
    }

    private void OnMindAdded(Entity<PulsarLinkedComponent> ent, ref MindAddedMessage args)
    {
        // A player took the unit: stop at once (the link stays, it only gets radio orders now).
        Release(ent);
        ent.Comp.Order = null;
    }

    // ---- orders ----

    /// <summary>Gives an order unless a higher-ranked one is active. Returns the blocking order otherwise.</summary>
    public bool TryOrder(Entity<PulsarLinkedComponent> ent, PulsarUnitOrder order, out PulsarUnitOrder? blocking)
    {
        blocking = ActiveOrder(ent.Comp);
        if (blocking != null && blocking.Rank > order.Rank)
            return false;

        // A one-time visit keeps the assignment to come back to it.
        if (!order.Assigned)
        {
            var current = ent.Comp.Order;
            order.Previous = current is { Assigned: true } ? current : current?.Previous;
        }

        if (ent.Comp.Task is { } task && order.Rank > PulsarOrderRank.Autonomous)
        {
            AbortTask(ent, task);
            ent.Comp.Task = null;
        }

        ent.Comp.Order = order;
        ent.Comp.Avoid.Clear();
        ent.Comp.Replans = 0;
        ent.Comp.BestDistance = float.MaxValue;
        ent.Comp.BestDistanceAt = _timing.CurTime;
        ent.Comp.NextUpdate = _timing.CurTime;
        blocking = null;
        return true;
    }

    /// <summary>The order that counts for ranks: the current one, or the assignment it will go back to.</summary>
    public static PulsarUnitOrder? ActiveOrder(PulsarLinkedComponent comp)
    {
        if (comp.Order == null)
            return null;
        var best = comp.Order;
        if (best.Previous is { } prev && prev.Rank > best.Rank)
            best = prev;
        return best;
    }

    /// <summary>Releases the assignment / order if the rank allows it.</summary>
    public bool TryRelease(Entity<PulsarLinkedComponent> ent, int rank, out PulsarUnitOrder? blocking)
    {
        blocking = ActiveOrder(ent.Comp);
        if (blocking != null && blocking.Rank > rank)
            return false;

        ent.Comp.Order = null;
        ClearBotGoto(ent);
        blocking = null;
        return true;
    }

    public IEnumerable<Entity<PulsarLinkedComponent>> LinkedUnits(EntityUid core)
    {
        var query = EntityQueryEnumerator<PulsarLinkedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Core == core)
                yield return (uid, comp);
        }
    }

    /// <summary>Can the module drive it right now (alive, no mind, not anchored, the core is active).</summary>
    public bool Controllable(Entity<PulsarLinkedComponent> ent)
    {
        if (!IsCoreActive(ent.Comp.Core) || HasMind(ent) || !_mobState.IsAlive(ent))
            return false;
        if (IsMinibot(ent.Comp.Kind) && Transform(ent).Anchored)
            return false;
        return true;
    }

    // ---- update ----

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PulsarLinkedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextUpdate)
                continue;
            comp.NextUpdate = now + UpdateInterval;

            var ent = (uid, comp);
            if (!Controllable(ent))
            {
                if (comp.Driving || comp.ActivatedChassis || comp.Task != null)
                    Release(ent);
                else
                    ClearBotGoto(ent);
                continue;
            }

            ExpireOrder(ent, now);
            UpdateRoute(ent, now);

            if (IsMinibot(comp.Kind))
                DriveBot(ent, now);
            else
                DriveBorg(ent, now);
        }

        UpdateHeldDoors(now);
        UpdateDispatch(now);
        UpdateConsoles(now);
        UpdateGroups(now);
    }

    private void ExpireOrder(Entity<PulsarLinkedComponent> ent, TimeSpan now)
    {
        var order = ent.Comp.Order;
        if (order == null || order.Assigned || !order.Arrived || now < order.Expires)
            return;

        // A borg still busy with what it was sent for finishes first.
        if (ent.Comp.Task != null)
            return;

        ent.Comp.Order = order.Previous;
        ent.Comp.BestDistance = float.MaxValue;
        ent.Comp.BestDistanceAt = now;
        ClearBotGoto(ent);
    }

    /// <summary>Distance to the order target, or null when on another map/grid it cannot reach.</summary>
    private float? DistanceTo(EntityUid uid, EntityCoordinates target)
    {
        if (!target.IsValid(EntityManager))
            return null;
        var a = _transform.GetMapCoordinates(uid);
        var b = _transform.ToMapCoordinates(target);
        if (a.MapId != b.MapId)
            return null;
        return (a.Position - b.Position).Length();
    }

    /// <summary>
    /// The order could not be carried out (no path, stuck after re-planning): an assignment is kept and tried again
    /// later; a visit is handed over to the nearest other free unit that can do it (stage 5.2).
    /// </summary>
    private void FailOrder(Entity<PulsarLinkedComponent> ent, TimeSpan now)
    {
        var order = ent.Comp.Order;
        if (order == null)
            return;

        Log.Debug($"Pulsar: {ToPrettyString(ent)} could not reach {order.Place}");
        ent.Comp.Replans = 0;
        ent.Comp.Avoid.Clear();
        if (order.Assigned)
        {
            // Keep the assignment, just try again later.
            ent.Comp.BestDistance = float.MaxValue;
            ent.Comp.BestDistanceAt = now + StuckTime;
        }
        else
        {
            ent.Comp.Order = order.Previous;
            ent.Comp.BestDistance = float.MaxValue;
            ent.Comp.BestDistanceAt = now;
        }

        ClearBotGoto(ent);
        if (TryComp<NPCSteeringComponent>(ent, out var steering))
            steering.AvoidTiles = null;
        if (!IsMinibot(ent.Comp.Kind))
            _steering.Unregister(ent);

        if (!order.Assigned && !order.Arrived)
            HandOver(ent, order, now);
    }

    // ---- minibots ----

    private void DriveBot(Entity<PulsarLinkedComponent> ent, TimeSpan now)
    {
        if (!TryComp<HTNComponent>(ent, out var htn))
            return;

        var order = ent.Comp.Order;
        if (order == null)
        {
            ClearBotGoto(ent, htn);
            return;
        }

        if (DistanceTo(ent, order.Target) is not { } dist)
        {
            FailOrder(ent, now);
            return;
        }

        if (!order.Arrived && dist <= ArriveRange)
            Arrive(ent, order, now);

        // Walk when not there yet, or when it roamed out of the area it serves.
        var walk = !order.Arrived || dist > order.Radius;
        if (!walk)
        {
            ClearBotGoto(ent, htn);
            ent.Comp.BestDistance = float.MaxValue;
            ent.Comp.BestDistanceAt = now;
            return;
        }

        if (!Progressing(ent, dist, now))
        {
            FailOrder(ent, now);
            return;
        }

        var bb = htn.Blackboard;
        var range = order.Arrived ? Math.Max(ArriveRange, order.Radius * 0.5f) : ArriveRange;
        if (!bb.TryGetValue<EntityCoordinates>(GotoKey, out var current, EntityManager) || !current.Equals(order.Target))
        {
            bb.SetValue(GotoKey, order.Target);
            bb.SetValue(GotoRangeKey, range);
            bb.SetValue(NPCBlackboard.NavAccess, true);
            if (TryComp<NPCSteeringComponent>(ent, out var steering))
                steering.Flags |= PathFlags.Access;
            _npc.WakeNPC(ent, htn);
        }
    }

    private void Arrive(Entity<PulsarLinkedComponent> ent, PulsarUnitOrder order, TimeSpan now)
    {
        order.Arrived = true;
        ent.Comp.Avoid.Clear();
        ent.Comp.Replans = 0;
        OnGroupArrived(ent, order);
        order.Expires = now + (order.Autonomous ? AutonomousVisitTime : VisitTime);
    }

    private void ClearBotGoto(Entity<PulsarLinkedComponent> ent, HTNComponent? htn = null)
    {
        if (!Resolve(ent, ref htn, false))
            return;
        var bb = htn.Blackboard;
        if (!bb.ContainsKey(GotoKey))
            return;

        // Stage 5.2: a plan made (or being made) with the key must not start without it: drop it, the bot replans.
        if (htn.PlanningJob != null)
        {
            htn.PlanningToken?.Cancel();
            htn.PlanningToken = null;
            htn.PlanningJob = null;
        }

        if (htn.Plan != null)
        {
            _htn.ShutdownTask(htn.Plan.CurrentOperator, bb, HTNOperatorStatus.Failed);
            _htn.ShutdownPlan(htn);
            htn.Plan = null;
        }

        bb.Remove<EntityCoordinates>(GotoKey);
        bb.Remove<float>(GotoRangeKey);
        bb.Remove<bool>(NPCBlackboard.NavAccess);
    }

    // ---- doors ----

    private void OnDoorBlocked(Entity<PulsarLinkedComponent> ent, ref NPCAccessDoorBlockedEvent args)
    {
        if (args.Handled || !Controllable(ent) || ent.Comp.Order == null && ent.Comp.Task == null)
            return;

        args.Handled = TryOpenFor(ent.Comp.Core!.Value, args.Door);
    }

    /// <summary>Opens a door for a unit the way the AI would (AI wire intact, powered, not bolted or welded).</summary>
    public bool TryOpenFor(EntityUid core, EntityUid door)
    {
        if (!TryComp<DoorComponent>(door, out var doorComp))
            return false;
        if (doorComp.State is DoorState.Open or DoorState.Opening)
            return true;
        if (doorComp.State == DoorState.Welded)
            return false;
        if (!TryComp<StationAiWhitelistComponent>(door, out var wl) || !wl.Enabled || !_power.IsPowered(door))
            return false;
        if (TryComp<DoorBoltComponent>(door, out var bolts) && bolts.BoltsDown)
            return false;

        // The module's own rules: it does not walk units into the armory or the captain's quarters.
        if (TryComp<PulsarAutonomousAiComponent>(core, out var comp) && IsForbiddenForUnits(door))
            return false;

        return _doors.TryOpen(door, doorComp, user: null);
    }

    private static readonly string[] UnitForbiddenAccess = { "Armory", "Captain" };

    private bool IsForbiddenForUnits(EntityUid door)
    {
        if (!TryComp<Content.Shared.Access.Components.AccessReaderComponent>(door, out var reader))
            return false;
        foreach (var set in reader.AccessLists)
        {
            foreach (var a in set)
            {
                if (UnitForbiddenAccess.Contains(a.Id))
                    return true;
            }
        }

        return false;
    }

    // ---- geometry helpers ----

    public EntityCoordinates? TileCoordinates(EntityUid grid, Vector2i tile)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return null;
        return new EntityCoordinates(grid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * gridComp.TileSize);
    }
}
