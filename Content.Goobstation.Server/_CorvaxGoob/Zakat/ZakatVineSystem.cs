using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Server.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// Рост 4: predatory flesh vines burst out of the core and creep over the whole station.
/// They slip under airlocks, smash windows and stop only at walls and closed armored doors
/// (бронированные двери, blast doors and shutters). Everyone in them who isn't the flesh's own gets cut.
/// </summary>
public sealed partial class ZakatVineSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public static readonly DamageSpecifier VineDamage = new()
    {
        DamageDict = new() { ["Slash"] = 30 },
    };

    /// <summary>
    /// Enough to shatter any window the vines meet.
    /// </summary>
    public static readonly DamageSpecifier WindowBreak = new()
    {
        DamageDict = new() { ["Structural"] = 2000, ["Blunt"] = 2000 },
    };

    public static readonly TimeSpan StepTime = TimeSpan.FromSeconds(0.3);
    public static readonly TimeSpan DamageInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    public const int MaxSpawnsPerStep = 250;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> ArmoredDoorTag = "HighSecDoor";

    private static readonly Vector2i[] Directions = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private readonly List<Vector2i> _retry = new();

    public enum Entry : byte
    {
        Enter,
        Blocked,
        Broke,
    }

    /// <summary>
    /// The vines burst out of the core. Returns false when there is no core to burst from.
    /// </summary>
    public bool Release(Entity<ZakatComponent> ent)
    {
        if (ent.Comp.VinesReleased || ent.Comp.Core is not { } core || TerminatingOrDeleted(core))
            return false;

        var xform = Transform(core);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        ent.Comp.VinesReleased = true;
        ent.Comp.Victory = true;
        Dirty(ent);

        var vines = EnsureComp<ZakatVinesComponent>(ent);
        vines.Grid = gridUid;
        var start = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        Grow(ent, (gridUid, grid), vines, start);
        var now = _timing.CurTime;
        vines.NextStep = now + StepTime;
        vines.NextDamage = now + DamageInterval;
        vines.NextRetry = now + RetryInterval;

        _popup.PopupEntity(Loc.GetString("zakat-vines-burst"), core, PopupType.LargeCaution);
        return true;
    }

    private void Grow(Entity<ZakatComponent> ent, Entity<MapGridComponent> grid, ZakatVinesComponent vines, Vector2i tile)
    {
        if (!vines.Covered.Add(tile))
            return;

        var vine = Spawn(ent.Comp.VinePrototype, _map.GridTileToLocal(grid, grid.Comp, tile));
        EnsureComp<ZakatVineComponent>(vine).Zakat = ent;
        vines.Frontier.Enqueue(tile);
        vines.Blocked.Remove(tile);
    }

    /// <summary>
    /// Can the vines creep onto this tile? Windows are smashed on the way (the vines enter next time).
    /// </summary>
    public Entry TryEnter(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var tileRef = _map.GetTileRef(grid, grid.Comp, tile);
        if (tileRef.Tile.IsEmpty)
            return Entry.Blocked;

        var broke = false;
        foreach (var uid in _map.GetAnchoredEntities(grid, grid.Comp, tile))
        {
            // the flesh's own walls and doors are the vines' own
            if (HasComp<ZakatStructureComponent>(uid) || HasComp<ZakatCoreComponent>(uid))
                continue;

            if (TryComp<DoorComponent>(uid, out var door))
            {
                // they creep under any airlock; a closed armored door holds them
                if (IsArmoredDoor(uid, door) && door.State != DoorState.Open)
                    return Entry.Blocked;
                continue;
            }

            if (_tag.HasTag(uid, WindowTag))
            {
                _damageable.TryChangeDamage(uid, WindowBreak, ignoreResistances: true);
                broke = true;
                continue;
            }

            if (!TryComp<PhysicsComponent>(uid, out var physics) || !physics.Hard || !physics.CanCollide)
                continue;

            if (_tag.HasTag(uid, WallTag) || TryComp<AirtightComponent>(uid, out var airtight) && airtight.AirBlocked)
                return Entry.Blocked;
        }

        return broke ? Entry.Broke : Entry.Enter;
    }

    /// <summary>
    /// «бронированная дверь» (HighSecDoor and its kin), blast doors and shutters.
    /// </summary>
    public bool IsArmoredDoor(EntityUid uid, DoorComponent door)
    {
        return _tag.HasTag(uid, ArmoredDoorTag) || door.ClosedDrawDepth == (int) Content.Shared.DrawDepth.DrawDepth.BlastDoors;
    }

    /// <summary>
    /// Flesh and its creatures, all the infected and the disease itself never get cut.
    /// </summary>
    public bool IsSpared(EntityUid uid)
    {
        return HasComp<ZakatFleshFriendComponent>(uid) || HasComp<ZakatComponent>(uid) || HasComp<ZakatPraetorianComponent>(uid);
    }

    public bool IsCovered(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var query = EntityQueryEnumerator<ZakatVinesComponent>();
        while (query.MoveNext(out var vines))
        {
            if (vines.Grid == gridUid && vines.Covered.Contains(tile))
                return true;
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ZakatVinesComponent, ZakatComponent>();
        while (query.MoveNext(out var uid, out var vines, out var zakat))
        {
            if (vines.Grid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComp))
                continue;

            var grid = (gridUid, gridComp);
            if (now >= vines.NextStep)
            {
                vines.NextStep = now + StepTime;
                Step((uid, zakat), grid, vines);
            }

            if (now >= vines.NextRetry)
            {
                vines.NextRetry = now + RetryInterval;
                Retry((uid, zakat), grid, vines);
            }

            if (now >= vines.NextDamage)
            {
                vines.NextDamage = now + DamageInterval;
                Cut(gridUid, gridComp, vines);
            }
        }
    }

    /// <summary>
    /// One wave: every tile at the edge pushes into its free neighbours.
    /// </summary>
    public void Step(Entity<ZakatComponent> ent, Entity<MapGridComponent> grid, ZakatVinesComponent vines)
    {
        var spawned = 0;
        var count = vines.Frontier.Count;
        for (var i = 0; i < count; i++)
        {
            if (spawned >= MaxSpawnsPerStep)
                break;

            var tile = vines.Frontier.Dequeue();
            foreach (var dir in Directions)
            {
                var next = tile + dir;
                if (vines.Covered.Contains(next))
                    continue;

                switch (TryEnter(grid, next))
                {
                    case Entry.Enter:
                        Grow(ent, grid, vines, next);
                        spawned++;
                        break;
                    default:
                        vines.Blocked.Add(next);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Smashed windows, opened doors and torn down walls let the vines through later.
    /// </summary>
    public void Retry(Entity<ZakatComponent> ent, Entity<MapGridComponent> grid, ZakatVinesComponent vines)
    {
        _retry.Clear();
        _retry.AddRange(vines.Blocked);
        foreach (var tile in _retry)
        {
            if (TryEnter(grid, tile) == Entry.Enter)
                Grow(ent, grid, vines, tile);
        }
    }

    public void Cut(EntityUid gridUid, MapGridComponent grid, ZakatVinesComponent vines)
    {
        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out var mob, out var xform))
        {
            if (xform.GridUid != gridUid || _mobState.IsDead(uid, mob) || IsSpared(uid))
                continue;

            var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (!vines.Covered.Contains(tile))
                continue;

            _damageable.TryChangeDamage(uid, VineDamage, canMiss: false);
        }
    }
}
