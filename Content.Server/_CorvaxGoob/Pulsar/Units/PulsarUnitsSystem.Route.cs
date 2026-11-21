using System.Linq;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.NPC;
using Content.Shared.Prying.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._CorvaxGoob.Pulsar.Units;

/// <summary>
/// Stage 5.2: the route. The engine NPC pathfinder plans the path (access doors count as passable: the module opens
/// them); the module walks the path ahead of each unit by its own map, opens the airlocks on it before the unit gets
/// there and closes them behind it, keeps the route off airlocks it cannot open (bolted, welded, unpowered, the
/// armory…) and off tiles units got stuck on, and re-plans before it gives up.
/// </summary>
public sealed partial class PulsarUnitsSystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    /// <summary>Doors on the path this far ahead (tiles) are opened before the unit gets there.</summary>
    public const float DoorLookahead = 4.5f;
    /// <summary>No progress for this long: re-plan the route (then again after twice as long…).</summary>
    public static readonly TimeSpan ReplanTime = TimeSpan.FromSeconds(5);
    public const int MaxReplans = 3;
    /// <summary>A door the module opened for units is closed once no unit needed it for this long.</summary>
    public static readonly TimeSpan DoorHold = TimeSpan.FromSeconds(1.5);
    /// <summary>Gives up closing a door (someone stands in it) after this long; the airlock closes itself later.</summary>
    public static readonly TimeSpan DoorCloseGiveUp = TimeSpan.FromSeconds(10);

    private sealed class HeldDoor
    {
        public EntityUid Core;
        public TimeSpan Opened;
        public TimeSpan Needed;
    }

    /// <summary>Doors the module opened for its units: closed behind them.</summary>
    private readonly Dictionary<EntityUid, HeldDoor> _heldDoors = new();
    private TimeSpan _nextDoorCheck;

    public int HeldDoorCount => _heldDoors.Count;

    public void SetLocation(EntityUid uid, EntityUid grid, Vector2i tile, string where)
    {
        if (!TryComp<PulsarLinkedComponent>(uid, out var comp))
            return;
        comp.Grid = grid;
        comp.Tile = tile;
        comp.Where = where;
    }

    private static Vector2i PolyTile(PathPoly poly) => (Vector2i) poly.Box.Center.Floored();

    /// <summary>Opens the doors on the path ahead and keeps the path off what the module cannot open.</summary>
    private void UpdateRoute(Entity<PulsarLinkedComponent> ent, TimeSpan now)
    {
        if (!TryComp<NPCSteeringComponent>(ent, out var steering))
            return;

        // The pathfinder keeps off what this unit was told to avoid.
        steering.AvoidTiles = ent.Comp.Avoid.Count > 0 ? ent.Comp.Avoid : null;

        if (steering.CurrentPath.Count == 0 || ent.Comp.Core is not { } core)
            return;

        var pos = _transform.GetMapCoordinates(ent);
        var prev = pos.Position;
        var travelled = 0f;
        var polys = 0;
        EntityUid? lastDoor = null;
        foreach (var poly in steering.CurrentPath)
        {
            if (++polys > 64)
                break;

            var at = _transform.ToMapCoordinates(poly.Coordinates);
            if (at.MapId != pos.MapId)
                break;
            travelled += (at.Position - prev).Length();
            prev = at.Position;
            if (travelled > DoorLookahead)
                break;

            // Loose things (a crate) on the path right ahead: a cyborg shoves them aside.
            if ((poly.Data.Flags & PathfindingBreadcrumbFlag.Movable) != 0x0)
            {
                if (travelled <= ShoveRange && TryShove(ent, steering, poly.GraphUid, PolyTile(poly), now))
                    break;
                continue;
            }

            if ((poly.Data.Flags & PathfindingBreadcrumbFlag.Door) == 0x0)
                continue;

            var tile = PolyTile(poly);
            if (FindDoor(core, poly.GraphUid, tile) is not { } door || door == lastDoor)
                continue;
            lastDoor = door;

            if (!RouteDoor(ent, core, door, poly, tile, travelled, now))
                break; // re-planning
        }
    }

    /// <summary>A cyborg this close to a crate on its path shoves it aside.</summary>
    public const float ShoveRange = 1.4f;
    private static readonly TimeSpan ShoveCooldown = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Shoves the loose things off a path tile onto a free tile next to it, away from the cyborg and off the path
    /// ahead. Only cyborgs: minibots are too small and go round.
    /// </summary>
    private bool TryShove(Entity<PulsarLinkedComponent> ent, NPCSteeringComponent steering, EntityUid grid, Vector2i tile, TimeSpan now)
    {
        if (!HasComp<BorgChassisComponent>(ent) || now < ent.Comp.NextShove || ent.Comp.Core is not { } core
            || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var things = LooseThings(grid, gridComp, tile, ent);
        if (things.Count == 0)
            return false;

        var own = _maps.TileIndicesFor(grid, gridComp, _transform.GetMapCoordinates(ent));
        var ahead = new HashSet<Vector2i>();
        foreach (var poly in steering.CurrentPath)
        {
            if (poly.GraphUid == grid)
                ahead.Add(PolyTile(poly));
            if (ahead.Count > 8)
                break;
        }

        // Straight on first, then to either side; never back onto the cyborg or onto the path ahead.
        var push = tile - own;
        push = Math.Abs(push.X) >= Math.Abs(push.Y) ? new Vector2i(Math.Sign(push.X), 0) : new Vector2i(0, Math.Sign(push.Y));
        if (push == Vector2i.Zero)
            push = new Vector2i(1, 0);
        var side = new Vector2i(push.Y, push.X);
        foreach (var dir in new[] { push, side, -side })
        {
            var to = tile + dir;
            if (to == own || ahead.Contains(to) || !Walkable(core, grid, gridComp, to)
                || LooseThings(grid, gridComp, to, ent).Count > 0 || MobOnTile(grid, to))
                continue;

            var dest = _maps.GridTileToLocal(grid, gridComp, to);
            foreach (var thing in things)
            {
                _transform.SetCoordinates(thing, dest);
            }

            ent.Comp.NextShove = now + ShoveCooldown;
            ForceRepath(steering);
            return true;
        }

        return false;
    }

    /// <summary>Unanchored hard things (crates, lockers) on a tile, not people.</summary>
    private List<EntityUid> LooseThings(EntityUid grid, MapGridComponent gridComp, Vector2i tile, EntityUid self)
    {
        var found = new List<EntityUid>();
        var set = new HashSet<EntityUid>();
        _lookup.GetLocalEntitiesIntersecting(grid, tile, set, -0.1f, LookupFlags.Dynamic | LookupFlags.Approximate);
        foreach (var uid in set)
        {
            if (uid == self || !TryComp<PhysicsComponent>(uid, out var body) || body.BodyType != BodyType.Dynamic
                || !body.Hard || !body.CanCollide || Transform(uid).Anchored
                || HasComp<Content.Shared.Mobs.Components.MobStateComponent>(uid) || HasComp<DoorComponent>(uid))
                continue;
            found.Add(uid);
        }

        return found;
    }

    /// <summary>The door on a tile: from the module's map first (it knows where the airlocks are), else the tile.</summary>
    private EntityUid? FindDoor(EntityUid core, EntityUid grid, Vector2i tile)
    {
        if (_pulsar.KnownDoor(core, grid, tile) is { } info && !TerminatingOrDeleted(info.Door) && HasComp<DoorComponent>(info.Door))
            return info.Door;

        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return null;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<DoorComponent>(uid.Value))
                return uid.Value;
        }

        return null;
    }

    /// <summary>False when the route was dropped for a new one.</summary>
    private bool RouteDoor(Entity<PulsarLinkedComponent> ent, EntityUid core, EntityUid door, PathPoly poly, Vector2i tile, float distance, TimeSpan now)
    {
        var doorComp = Comp<DoorComponent>(door);
        if (doorComp.State is DoorState.Open or DoorState.Opening)
        {
            if (_heldDoors.TryGetValue(door, out var held))
                held.Needed = now;
            return true;
        }

        // A door it can open itself: its own access, or a plain door it bumps open.
        var needsAccess = (poly.Data.Flags & PathfindingBreadcrumbFlag.Access) != 0x0;
        if (needsAccess && _access.IsAllowed(ent, door))
            return true;

        if (TryOpenFor(core, door))
        {
            ent.Comp.DoorsOpened++;
            if (_heldDoors.TryGetValue(door, out var held))
                held.Needed = now;
            else
                _heldDoors[door] = new HeldDoor { Core = core, Opened = now, Needed = now };
            return true;
        }

        if (!needsAccess && doorComp.State != DoorState.Welded && !(TryComp<DoorBoltComponent>(door, out var b) && b.BoltsDown))
            return true; // it walks into it and opens it itself

        // A cyborg with a crowbar next to an unpowered airlock pries it open.
        if (TryComp<BorgChassisComponent>(ent, out var chassis)
            && doorComp.State == DoorState.Closed && !_power.IsPowered(door)
            && !(TryComp<DoorBoltComponent>(door, out var bolts) && bolts.BoltsDown) && CanPry((ent, chassis)))
        {
            // Walk up to it, then pry.
            if (distance <= 1.6f && ent.Comp.Task == null)
                StartTask(ent, PulsarBorgTaskKind.Pry, door, now);
            return true;
        }

        // Nobody can open it for this unit: route around it.
        AvoidTile(ent, poly.GraphUid, tile);
        return false;
    }

    private void AvoidTile(Entity<PulsarLinkedComponent> ent, EntityUid grid, Vector2i tile)
    {
        if (!ent.Comp.Avoid.Add((grid, tile)))
            return;
        if (TryComp<NPCSteeringComponent>(ent, out var steering))
        {
            steering.AvoidTiles = ent.Comp.Avoid;
            ForceRepath(steering);
        }
    }

    private static void ForceRepath(NPCSteeringComponent steering)
    {
        steering.CurrentPath.Clear();
        steering.PathfindToken?.Cancel();
        steering.PathfindToken = null;
        steering.FailedPathCount = 0;
        if (steering.Status == SteeringStatus.NoPath)
            steering.Status = SteeringStatus.Moving;
    }

    /// <summary>Closes the doors it opened once no unit needs them any more.</summary>
    private void UpdateHeldDoors(TimeSpan now)
    {
        if (now < _nextDoorCheck || _heldDoors.Count == 0)
            return;
        _nextDoorCheck = now + TimeSpan.FromSeconds(0.25);

        List<EntityUid>? done = null;
        foreach (var (door, held) in _heldDoors)
        {
            if (TerminatingOrDeleted(door) || !TryComp<DoorComponent>(door, out var doorComp)
                || doorComp.State is DoorState.Closed or DoorState.Welded)
            {
                (done ??= new()).Add(door);
                continue;
            }

            // A linked unit in or next to the doorway still needs it.
            var doorPos = _transform.GetMapCoordinates(door);
            foreach (var unit in LinkedUnits(held.Core))
            {
                var p = _transform.GetMapCoordinates(unit);
                if (p.MapId == doorPos.MapId && (p.Position - doorPos.Position).Length() < 1.3f)
                {
                    held.Needed = now;
                    break;
                }
            }

            if (now - held.Needed < DoorHold || now - held.Opened < DoorHold)
                continue;

            if (doorComp.State == DoorState.Open && _doors.TryClose(door, doorComp))
            {
                (done ??= new()).Add(door);
                continue;
            }

            if (now - held.Needed > DoorCloseGiveUp)
                (done ??= new()).Add(door);
        }

        if (done == null)
            return;
        foreach (var door in done)
            _heldDoors.Remove(door);
    }

    /// <summary>
    /// The stuck watchdog: true while it gets closer. Without progress it re-plans the route (what blocks it goes on
    /// the avoid list and on the module's map) up to <see cref="MaxReplans"/> times before the order is given up.
    /// </summary>
    private bool Progressing(Entity<PulsarLinkedComponent> ent, float distance, TimeSpan now)
    {
        var comp = ent.Comp;
        if (distance < comp.BestDistance - 0.5f)
        {
            comp.BestDistance = distance;
            comp.BestDistanceAt = now;
            comp.Replans = 0;
            return true;
        }

        var stuck = now - comp.BestDistanceAt;
        if (comp.Replans < MaxReplans && stuck >= ReplanTime * (comp.Replans + 1))
        {
            comp.Replans++;
            comp.LastReplan = now;
            Replan(ent);
            return true;
        }

        return stuck < StuckTime;
    }

    private void Replan(Entity<PulsarLinkedComponent> ent)
    {
        if (!TryComp<NPCSteeringComponent>(ent, out var steering))
            return;

        // What is right in front of it on the path.
        if (steering.CurrentPath.TryPeek(out var next) && ent.Comp.Core is { } core)
        {
            var tile = PolyTile(next);
            var grid = next.GraphUid;
            var here = _transform.GetMapCoordinates(ent);
            var there = _transform.ToMapCoordinates(next.Coordinates);
            if (here.MapId == there.MapId && (here.Position - there.Position).Length() < 2f)
            {
                if (FindDoor(core, grid, tile) is { } door)
                {
                    if (TryComp<DoorComponent>(door, out var d) && d.State is not (DoorState.Open or DoorState.Opening)
                        && !TryOpenFor(core, door) && !_access.IsAllowed(ent, door))
                        ent.Comp.Avoid.Add((grid, tile));
                }
                else if (TryComp<MapGridComponent>(grid, out var gridComp)
                         && _turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, gridComp)
                         && !MobOnTile(grid, tile)) // people move on; things do not
                {
                    ent.Comp.Avoid.Add((grid, tile));
                    _pulsar.MarkBlocked(core, grid, tile);
                }
            }
        }

        steering.AvoidTiles = ent.Comp.Avoid.Count > 0 ? ent.Comp.Avoid : null;
        ForceRepath(steering);
    }

    private bool MobOnTile(EntityUid grid, Vector2i tile)
    {
        if (TileCoordinates(grid, tile) is not { } coords)
            return false;
        foreach (var _ in _lookup.GetEntitiesInRange<Content.Shared.Mobs.Components.MobStateComponent>(coords, 0.6f))
            return true;
        return false;
    }

    // ---- several units at one place ----

    /// <summary>
    /// A free floor tile at or near <paramref name="center"/> nobody else was sent to, so several units do not
    /// jam one tile / doorway. Falls back to the centre.
    /// </summary>
    public EntityCoordinates FreeSpot(EntityUid core, EntityCoordinates center, HashSet<(EntityUid, Vector2i)> taken)
    {
        if (_transform.GetGrid(center) is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return center;

        var origin = _maps.LocalToTile(grid, gridComp, center);
        for (var r = 0; r <= 3; r++)
        {
            var ring = new List<Vector2i>();
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r)
                        ring.Add(origin + new Vector2i(dx, dy));
                }
            }

            foreach (var tile in ring.OrderBy(t => (t - origin).LengthSquared))
            {
                if (taken.Contains((grid, tile)) || !Walkable(core, grid, gridComp, tile))
                    continue;
                taken.Add((grid, tile));
                return r == 0 ? center : TileCoordinates(grid, tile) ?? center;
            }
        }

        return center;
    }

    /// <summary>Tiles the module's units are already going to / standing at for their orders.</summary>
    public HashSet<(EntityUid, Vector2i)> TakenSpots(EntityUid core)
    {
        var taken = new HashSet<(EntityUid, Vector2i)>();
        foreach (var unit in LinkedUnits(core))
        {
            if (unit.Comp.Order is not { } order || _transform.GetGrid(order.Target) is not { } grid
                || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;
            taken.Add((grid, _maps.LocalToTile(grid, gridComp, order.Target)));
        }

        return taken;
    }

    private bool Walkable(EntityUid core, EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        if (!_maps.TryGetTileRef(grid, gridComp, tile, out var tileRef) || _turf.IsSpace(tileRef))
            return false;
        if (_pulsar.KnownTile(core, grid, tile) is PulsarTileKind.Wall or PulsarTileKind.Window or PulsarTileKind.Table
            or PulsarTileKind.Airlock or PulsarTileKind.Door)
            return false;
        return !_turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, gridComp);
    }

    // ---- prying ----

    public bool CanPry(Entity<BorgChassisComponent> chassis)
    {
        return FindModuleItem(chassis, uid => HasComp<PryingComponent>(uid), p => p.TryGetComponent<PryingComponent>(out _, _factory)).Module != null;
    }

    private EntityUid? PickPryTool(Entity<BorgChassisComponent> chassis, EntityUid door)
    {
        return TakeItem(chassis, uid => HasComp<PryingComponent>(uid), p => p.TryGetComponent<PryingComponent>(out _, _factory));
    }

    private bool DoorClosed(EntityUid door)
    {
        return TryComp<DoorComponent>(door, out var d) && d.State is DoorState.Closed;
    }
}
