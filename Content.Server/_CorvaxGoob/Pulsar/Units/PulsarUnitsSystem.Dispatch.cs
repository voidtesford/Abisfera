using System.Linq;
using Content.Server.Research.Systems;
using Content.Shared.Anomaly.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Robotics;
using Content.Shared.Robotics.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._CorvaxGoob.Pulsar.Units;

public enum PulsarNeed : byte
{
    Fire,
    Patient,
    Breach,
    Anomaly,
    Silicon,
    /// <summary>Stage 5.3: a body to carry to medbay or the morgue.</summary>
    Corpse,
}

public enum ReportChannel : byte
{
    Engineering,
    Medical,
    Science,
    Service,
    Common,
}

/// <summary>
/// Stage 5: the module's own dispatch (fires, patients in critical condition, breaches, anomalies) and the
/// robotics console tabs «Юниты» / «Миниботы».
/// </summary>
public sealed partial class PulsarUnitsSystem
{
    [Dependency] private readonly PulsarAutonomousAiSystem _pulsar = default!;
    [Dependency] private readonly RoboticsConsoleSystem _console = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public static readonly TimeSpan DispatchInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DispatchMemory = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ConsoleRefresh = TimeSpan.FromSeconds(2);

    private TimeSpan _nextDispatch;
    private TimeSpan _nextConsoleRefresh;

    /// <summary>What was already sent where: (core, thing) → until when it is not sent again.</summary>
    private readonly Dictionary<(EntityUid Core, EntityUid Thing), TimeSpan> _dispatched = new();
    private readonly Dictionary<(EntityUid Core, string Key), TimeSpan> _dispatchedPlaces = new();

    public static bool Suits(PulsarNeed need, PulsarUnitKind kind) => need switch
    {
        PulsarNeed.Fire => kind == PulsarUnitKind.Firebot,
        PulsarNeed.Patient => kind is PulsarUnitKind.Medibot or PulsarUnitKind.BorgMedical,
        PulsarNeed.Breach => kind is PulsarUnitKind.BorgEngineering,
        PulsarNeed.Anomaly => kind is PulsarUnitKind.BorgScience,
        PulsarNeed.Silicon => kind is PulsarUnitKind.Weldbot or PulsarUnitKind.BorgEngineering,
        PulsarNeed.Corpse => kind is PulsarUnitKind.BorgMedical,
        _ => false,
    };

    /// <summary>
    /// Sends up to <paramref name="count"/> free linked units that suit the need, nearest first. Units under a
    /// person's order are not taken (the module's own dispatch has the lowest rank). Returns how many went.
    /// </summary>
    public int Dispatch(EntityUid core, PulsarNeed need, EntityCoordinates where, string place, EntityUid? focus = null, int count = 1, string? key = null)
    {
        var now = _timing.CurTime;
        if (!IsCoreActive(core))
            return 0;

        if (focus is { } f && _dispatched.TryGetValue((core, f), out var until) && until > now)
            return 0;
        if (key != null && _dispatchedPlaces.TryGetValue((core, key), out var placeUntil) && placeUntil > now)
            return 0;

        var target = _transform.ToMapCoordinates(where);
        var candidates = new List<(Entity<PulsarLinkedComponent> Unit, float Dist)>();
        foreach (var unit in LinkedUnits(core))
        {
            if (!Suits(need, unit.Comp.Kind) || !Controllable(unit) || unit.Comp.Task != null)
                continue;
            if (ActiveOrder(unit.Comp) is { } active && active.Rank > PulsarOrderRank.Autonomous)
                continue;
            if (unit.Comp.Order is { Autonomous: true, Arrived: false })
                continue; // already on its way somewhere else
            if (!IsMinibot(unit.Comp.Kind) && TryComp<BorgChassisComponent>(unit, out var chassis)
                && need == PulsarNeed.Patient && !CanHeal((unit, chassis)))
                continue;
            var pos = _transform.GetMapCoordinates(unit);
            if (pos.MapId != target.MapId)
                continue;
            candidates.Add((unit, (pos.Position - target.Position).Length()));
        }

        var sent = 0;
        var group = NewGroup(core, place, ReportChannel.Common, report: false);
        var taken = TakenSpots(core);
        foreach (var (unit, _) in candidates.OrderBy(c => c.Dist))
        {
            if (sent >= count)
                break;
            var order = new PulsarUnitOrder
            {
                Group = group,
                Need = need,
                Target = focus != null ? where : FreeSpot(core, where, taken),
                Place = place,
                Radius = 4f,
                Rank = PulsarOrderRank.Autonomous,
                Autonomous = true,
                Focus = focus,
            };
            if (TryOrder(unit, order, out _))
            {
                sent++;
                JoinGroup(group, unit);
            }
        }

        if (sent > 0)
        {
            if (focus is { } ff)
                _dispatched[(core, ff)] = now + DispatchMemory;
            if (key != null)
                _dispatchedPlaces[(core, key)] = now + DispatchMemory;
        }

        return sent;
    }

    private void UpdateDispatch(TimeSpan now)
    {
        if (now < _nextDispatch)
            return;
        _nextDispatch = now + DispatchInterval;

        foreach (var k in _dispatched.Where(p => p.Value <= now).Select(p => p.Key).ToList())
            _dispatched.Remove(k);
        foreach (var k in _dispatchedPlaces.Where(p => p.Value <= now).Select(p => p.Key).ToList())
            _dispatchedPlaces.Remove(k);

        var query = EntityQueryEnumerator<PulsarAutonomousAiComponent>();
        while (query.MoveNext(out var core, out var comp))
        {
            if (comp.State != PulsarState.Active || !comp.Policy.ServesCrew)
                continue;

            // Patients in critical condition on camera: a medibot / medical unit.
            foreach (var (mob, info) in comp.Mobs)
            {
                if (!info.InView || !info.Critical || info.Dead || info.Kind == PulsarMobKind.Enemy || TerminatingOrDeleted(mob))
                    continue;
                if (TileCoordinates(info.Grid, info.Tile) is not { } coords)
                    continue;
                Dispatch(core, PulsarNeed.Patient, coords, _pulsar.PlaceName((core, comp), info.Grid, info.Tile), mob);
            }

            // Stage 5.3: bodies on camera: a medical unit carries them.
            foreach (var (mob, info) in comp.Mobs)
            {
                if (!info.InView || !info.Dead || TerminatingOrDeleted(mob) || !BodyNeedsCare(mob))
                    continue;
                if (TileCoordinates(info.Grid, info.Tile) is not { } coords)
                    continue;
                Dispatch(core, PulsarNeed.Corpse, coords, _pulsar.PlaceName((core, comp), info.Grid, info.Tile), mob);
            }

            // Anomalies on the station: a science unit (not those of the generator or kept by science).
            var anomalies = EntityQueryEnumerator<AnomalyComponent, TransformComponent>();
            while (anomalies.MoveNext(out var anomaly, out _, out var xform))
            {
                if (xform.GridUid is not { } grid || !comp.StationGrids.Contains(grid) || LeftToScience(anomaly))
                    continue;
                Dispatch(core, PulsarNeed.Anomaly, xform.Coordinates, string.Empty, anomaly);
            }

            Rebalance(core, now);

            // Damaged units: a weldbot / engineering unit.
            foreach (var unit in LinkedUnits(core))
            {
                if (unit.Comp.Kind is PulsarUnitKind.Weldbot || !TryComp<DamageableComponent>(unit, out var dmg) || dmg.TotalDamage < 20)
                    continue;
                Dispatch(core, PulsarNeed.Silicon, Transform(unit).Coordinates, string.Empty, unit);
            }
        }
    }

    /// <summary>Called by the module when it reports a fire / a breach.</summary>
    public void OnHazard(EntityUid core, PulsarNeed need, EntityUid grid, Vector2i tile, string place, string key)
    {
        if (TileCoordinates(grid, tile) is not { } coords)
            return;
        Dispatch(core, need, coords, place, count: need == PulsarNeed.Fire ? 2 : 1, key: key);
    }

    public void UnitReport(EntityUid core, string text, ReportChannel channel)
    {
        var name = channel switch
        {
            ReportChannel.Engineering => "Engineering",
            ReportChannel.Medical => "Medical",
            ReportChannel.Science => "Science",
            ReportChannel.Service => "Service",
            _ => "Common",
        };
        _pulsar.UnitRadio(core, text, name);
    }

    // ---- robotics console ----

    private void InitializeConsole()
    {
    }

    private void UpdateConsoles(TimeSpan now)
    {
        if (now < _nextConsoleRefresh)
            return;
        _nextConsoleRefresh = now + ConsoleRefresh;

        var query = EntityQueryEnumerator<RoboticsConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, RoboticsConsoleUiKey.Key))
                _console.UpdateUserInterface((uid, comp));
        }
    }

    /// <summary>Fills the Pulsar part of the console state: link flags of the borgs and the minibot list.</summary>
    public void FillConsoleState(EntityUid console, RoboticsConsoleState state, IReadOnlyDictionary<string, EntityUid> borgs)
    {
        var core = FindCore(console);
        state.PulsarAvailable = core != null;

        foreach (var address in state.Cyborgs.Keys.ToList())
        {
            var data = state.Cyborgs[address];
            if (borgs.TryGetValue(address, out var borg) && !TerminatingOrDeleted(borg) && HasComp<BorgChassisComponent>(borg))
            {
                data.PulsarLinkable = true;
                data.PulsarLinked = TryComp<PulsarLinkedComponent>(borg, out var link);
                data.PulsarTask = link != null ? TaskText((borg, link)) : string.Empty;
            }
            else
            {
                data.PulsarLinkable = false;
                data.PulsarLinked = false;
                data.PulsarTask = string.Empty;
            }

            state.Cyborgs[address] = data;
        }

        var station = _station.GetOwningStation(console);
        var consoleMap = Transform(console).MapID;
        var list = new List<RoboticsMinibotData>();
        var query = EntityQueryEnumerator<Content.Server.NPC.HTN.HTNComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (GetKind(uid) is not { } kind || !IsMinibot(kind))
                continue;
            if (station != null ? _station.GetOwningStation(uid, xform) != station : xform.MapID != consoleMap)
                continue;

            var hp = 1f;
            if (TryComp<DamageableComponent>(uid, out var dmg))
                hp = Math.Clamp(1f - dmg.TotalDamage.Float() / 100f, 0f, 1f);

            TryComp<PulsarLinkedComponent>(uid, out var link);
            list.Add(new RoboticsMinibotData
            {
                Entity = GetNetEntity(uid),
                Name = meta.EntityName,
                Kind = Loc.GetString(KindLoc(kind)),
                Prototype = meta.EntityPrototype?.ID ?? string.Empty,
                HpPercent = hp,
                Linked = link != null,
                Anchored = xform.Anchored,
                Sentient = HasMind(uid),
                Task = link != null ? TaskText((uid, link)) : string.Empty,
            });
        }

        state.Minibots = list.OrderBy(m => m.Kind).ThenBy(m => m.Name).ToList();
    }

    /// <summary>What the unit is doing, for the console.</summary>
    public string TaskText(Entity<PulsarLinkedComponent> ent)
    {
        var text = TaskOnly(ent);
        // Stage 5.2: where it is by the module's map.
        return string.IsNullOrEmpty(ent.Comp.Where) || !IsCoreActive(ent.Comp.Core) || HasMind(ent)
            ? text
            : Loc.GetString("pulsar-unit-where", ("task", text), ("where", ent.Comp.Where));
    }

    private string TaskOnly(Entity<PulsarLinkedComponent> ent)
    {
        if (!IsCoreActive(ent.Comp.Core))
            return Loc.GetString("pulsar-unit-task-core-offline");
        if (HasMind(ent))
            return Loc.GetString("pulsar-unit-task-sentient");
        if (ent.Comp.Task is { } task)
            return Loc.GetString("pulsar-unit-task-" + task.Kind.ToString().ToLowerInvariant());
        if (ent.Comp.Order is { } order)
        {
            var place = string.IsNullOrEmpty(order.Place) ? Loc.GetString("pulsar-place-unknown") : order.Place;
            return Loc.GetString(order.Assigned ? "pulsar-unit-task-assigned" : "pulsar-unit-task-going", ("place", place));
        }

        return Loc.GetString("pulsar-unit-task-free");
    }

    /// <summary>Console button: link or unlink a borg (by address) or a minibot.</summary>
    public bool ConsoleLink(EntityUid console, EntityUid unit, bool link, out string failLoc)
    {
        failLoc = string.Empty;
        if (!link)
        {
            if (HasComp<PulsarLinkedComponent>(unit))
                Unlink(unit);
            return true;
        }

        return TryLink(unit, FindCore(console), out failLoc);
    }

    /// <summary>A free unit has to be this much nearer (tiles, and in share of the way) to take a job over.</summary>
    private const float RebalanceGain = 8f;
    private const float RebalanceShare = 0.6f;

    /// <summary>
    /// Stage 5.3: a unit still on its way to a job of the module's own (a patient, a body, an anomaly...) hands it
    /// over to a free unit that can do it and is much nearer, and becomes free itself. Orders of people are never
    /// moved; a unit already at work keeps its job.
    /// </summary>
    private void Rebalance(EntityUid core, TimeSpan now)
    {
        var units = LinkedUnits(core).ToList();
        foreach (var busy in units)
        {
            if (busy.Comp.Order is not { Autonomous: true, Arrived: false, Focus: { } focus, Need: { } need } order
                || TerminatingOrDeleted(focus))
                continue;
            var busyDist = DistanceTo(busy, Transform(focus).Coordinates);
            if (busyDist is not { } far || far < RebalanceGain)
                continue;
            // Already working on it (next to it): keep.
            if (busy.Comp.Task is { } task && (task.Target != focus || far <= 3f))
                continue;

            Entity<PulsarLinkedComponent>? best = null;
            var bestDist = far;
            foreach (var free in units)
            {
                if (free.Owner == busy.Owner || !Suits(need, free.Comp.Kind) || !Controllable(free) || free.Comp.Task != null
                    || order.Tried.Contains(free))
                    continue;
                if (ActiveOrder(free.Comp) is { } active && (active.Rank > PulsarOrderRank.Autonomous || !active.Arrived))
                    continue;
                if (!IsMinibot(free.Comp.Kind) && TryComp<BorgChassisComponent>(free, out var chassis)
                    && need == PulsarNeed.Patient && !CanHeal((free, chassis)))
                    continue;
                if (DistanceTo(free, Transform(focus).Coordinates) is not { } d || d > far - RebalanceGain || d > far * RebalanceShare)
                    continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = free;
                }
            }

            if (best is not { } next || !TryOrder(next, order.Handover(busy), out _))
                continue;

            if (busy.Comp.Task != null)
                EndTask(busy, now, failed: false);
            busy.Comp.Order = order.Previous;
            busy.Comp.BestDistance = float.MaxValue;
            busy.Comp.BestDistanceAt = now;
            _steering.Unregister(busy);
            ClearBotGoto(busy);
            Log.Debug($"Pulsar: {ToPrettyString(next)} takes over {need} from {ToPrettyString(busy)}");
        }
    }
}
