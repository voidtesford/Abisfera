using System.Linq;
using Robust.Shared.Map;

namespace Content.Server._CorvaxGoob.Pulsar.Units;

/// <summary>
/// Stage 5.2: leading the units. Units sent together form a group: they get separate spots (no jam on one tile or
/// in one doorway) and there is one report on the department channel when they are all there. A unit that cannot
/// get through hands its order to the nearest other free unit that can do the job; the module says so once.
/// Reports are rare: only for people's orders, at most one per event.
/// </summary>
public sealed partial class PulsarUnitsSystem
{
    private sealed class UnitGroup
    {
        public EntityUid Core;
        public string Place = string.Empty;
        public ReportChannel Channel;
        public bool Report;
        public readonly HashSet<EntityUid> Members = new();
        public readonly HashSet<EntityUid> Arrived = new();
        public bool Done;
        public TimeSpan Created;
    }

    private readonly Dictionary<int, UnitGroup> _groups = new();
    private int _nextGroup;
    private TimeSpan _nextGroupCleanup;

    public static ReportChannel ChannelFor(PulsarUnitKind kind) => kind switch
    {
        PulsarUnitKind.Medibot or PulsarUnitKind.BorgMedical => ReportChannel.Medical,
        PulsarUnitKind.Weldbot or PulsarUnitKind.Firebot or PulsarUnitKind.BorgEngineering => ReportChannel.Engineering,
        PulsarUnitKind.Cleanbot or PulsarUnitKind.BorgService or PulsarUnitKind.BorgJanitor => ReportChannel.Service,
        PulsarUnitKind.BorgScience => ReportChannel.Science,
        _ => ReportChannel.Common,
    };

    /// <summary>Starts a group of units sent together. <paramref name="report"/>: say on the radio when they are there.</summary>
    public int NewGroup(EntityUid core, string place, ReportChannel channel, bool report)
    {
        var id = ++_nextGroup;
        _groups[id] = new UnitGroup
        {
            Core = core,
            Place = place,
            Channel = channel,
            Report = report,
            Created = _timing.CurTime,
        };
        return id;
    }

    public void JoinGroup(int group, EntityUid unit)
    {
        if (_groups.TryGetValue(group, out var g))
            g.Members.Add(unit);
    }

    private void OnGroupArrived(Entity<PulsarLinkedComponent> ent, PulsarUnitOrder order)
    {
        if (order.Group == 0 || !_groups.TryGetValue(order.Group, out var group) || group.Done)
            return;

        group.Arrived.Add(ent);
        CheckGroup(group);
    }

    private void CheckGroup(UnitGroup group)
    {
        if (group.Done || group.Members.Count == 0 || !group.Members.All(group.Arrived.Contains))
            return;

        group.Done = true;
        if (!group.Report)
            return;

        var names = string.Join(", ", group.Members.Where(u => !TerminatingOrDeleted(u)).Select(UnitName));
        UnitReport(group.Core, Loc.GetString("pulsar-unit-report-arrived", ("units", names), ("place", group.Place),
            ("count", group.Members.Count)), group.Channel);
    }

    private void UpdateGroups(TimeSpan now)
    {
        if (now < _nextGroupCleanup)
            return;
        _nextGroupCleanup = now + TimeSpan.FromSeconds(30);

        foreach (var id in _groups.Where(g => g.Value.Done || now - g.Value.Created > TimeSpan.FromMinutes(10))
                     .Select(g => g.Key).ToList())
            _groups.Remove(id);
    }

    public string UnitName(EntityUid uid) => Name(uid);

    /// <summary>
    /// The unit could not get there: the nearest other free unit that can do the job takes the order over.
    /// One line on the department channel either way (people's orders only).
    /// </summary>
    private void HandOver(Entity<PulsarLinkedComponent> failed, PulsarUnitOrder order, TimeSpan now)
    {
        if (failed.Comp.Core is not { } core)
            return;

        _groups.TryGetValue(order.Group, out var group);
        group?.Members.Remove(failed);

        var target = _transform.ToMapCoordinates(order.Target);
        Entity<PulsarLinkedComponent>? best = null;
        var bestDist = float.MaxValue;
        foreach (var unit in LinkedUnits(core))
        {
            if (unit.Owner == failed.Owner || order.Tried.Contains(unit) || !Controllable(unit) || unit.Comp.Task != null)
                continue;
            var fits = order.Kinds?.Contains(unit.Comp.Kind) ?? order.Need is { } need && Suits(need, unit.Comp.Kind);
            if (!fits)
                continue;
            if (ActiveOrder(unit.Comp) is { } active && (active.Rank >= order.Rank || !active.Arrived || active.Group == order.Group))
                continue;
            var p = _transform.GetMapCoordinates(unit);
            if (p.MapId != target.MapId)
                continue;
            var d = (p.Position - target.Position).Length();
            if (d < bestDist)
            {
                bestDist = d;
                best = unit;
            }
        }

        string? other = null;
        if (best is { } next && TryOrder(next, order.Handover(failed), out _))
        {
            other = Name(next);
            group?.Members.Add(next);
        }

        if (order.Autonomous)
            return;

        var channel = group?.Channel ?? ChannelFor(failed.Comp.Kind);
        var text = other != null
            ? Loc.GetString("pulsar-unit-report-handover", ("unit", Name(failed)), ("place", order.Place), ("other", other))
            : Loc.GetString("pulsar-unit-report-failed", ("unit", Name(failed)), ("place", order.Place));
        UnitReport(core, text, channel);

        if (group != null)
            CheckGroup(group);
    }
}
