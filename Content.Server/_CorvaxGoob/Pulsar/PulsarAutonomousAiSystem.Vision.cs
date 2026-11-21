using Content.Server.Atmos.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Climbing.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.PDA;
using Content.Shared.Placeable;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationRecords;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Vision and the memory map. Uses <see cref="StationAiVisionSystem.GetView"/> (camera line of sight, the same
/// thing the AI player's static overlay uses) one window at a time.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly StationAiVisionSystem _vision = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> TableTag = "Table";
    private const string OuterSlot = "outerClothing";
    private const string IdSlot = "id";

    private readonly HashSet<Vector2i> _visibleScratch = new();
    private readonly List<EntityUid> _removeScratch = new();
    private readonly List<Vector2i> _tileRemoveScratch = new();

    /// <summary>Cache: outer clothing prototype id -> is a syndicate suit. Cleared on prototype reload.</summary>
    private readonly Dictionary<string, bool> _enemySuitCache = new();

    private EntityQuery<BroadphaseComponent> _broadphaseQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<DoorComponent> _doorQuery;
    private EntityQuery<AirlockComponent> _airlockQuery;
    private EntityQuery<DoorBoltComponent> _boltQuery;
    private EntityQuery<AccessReaderComponent> _accessQuery;
    private EntityQuery<OccluderComponent> _occluderQuery;

    private void InitializeVision()
    {
        _broadphaseQuery = GetEntityQuery<BroadphaseComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();
        _airlockQuery = GetEntityQuery<AirlockComponent>();
        _boltQuery = GetEntityQuery<DoorBoltComponent>();
        _accessQuery = GetEntityQuery<AccessReaderComponent>();
        _occluderQuery = GetEntityQuery<OccluderComponent>();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ =>
        {
            _enemySuitCache.Clear();
            ClearSpeechCaches();
        });
    }

    /// <summary>
    /// One vision step: picks the next window of the next grid, asks the AI vision system which tiles of it are
    /// seen by working cameras, refreshes the visibility mask of the window and re-classifies the seen tiles.
    /// </summary>
    private void ScanStep(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.Grids.Count == 0)
            return;

        if (comp.GridCursor >= comp.Grids.Count)
            comp.GridCursor = 0;

        var index = 0;
        foreach (var (gridUid, mem) in comp.Grids)
        {
            if (index++ != comp.GridCursor)
                continue;

            comp.GridCursor++;
            ScanWindow(ent, gridUid, mem, now);
            break;
        }
    }

    private void ScanWindow(Entity<PulsarAutonomousAiComponent> ent, EntityUid gridUid, PulsarGridMemory mem, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!_gridQuery.TryComp(gridUid, out var grid) || !_broadphaseQuery.TryComp(gridUid, out var broadphase))
            return;

        var size = Math.Max(PulsarGridMemory.ChunkSize, comp.ScanWindowTiles / PulsarGridMemory.ChunkSize * PulsarGridMemory.ChunkSize);

        if (mem.WindowCursor >= mem.Windows.Count)
        {
            if (mem.Windows.Count > 0)
                mem.Sweeps++;
            BuildWindows(grid, mem, size);
            mem.WindowCursor = 0;
            if (mem.Windows.Count == 0)
                return;
        }

        var origin = mem.Windows[mem.WindowCursor++];
        var localBox = new Box2(origin, origin + new Vector2i(size, size));
        var worldBox = _xforms.GetWorldMatrix(gridUid).TransformBox(localBox);

        _visibleScratch.Clear();
        lock (_vision)
        {
            _vision.GetView((gridUid, broadphase, grid), new Box2Rotated(worldBox), _visibleScratch);
        }

        comp.ScannedWindows++;
        var atmos = BeginAtmosSample(ent, gridUid, origin, size);

        for (var x = origin.X; x < origin.X + size; x++)
        {
            for (var y = origin.Y; y < origin.Y + size; y++)
            {
                var tile = new Vector2i(x, y);
                var visible = _visibleScratch.Contains(tile);
                mem.SetVisible(tile, visible);

                if (!visible)
                    continue;

                var kind = ClassifyTile(gridUid, grid, mem, tile, now);
                mem.SetTile(tile, kind, now);

                if (atmos != null && kind != PulsarTileKind.Space && kind != PulsarTileKind.Wall)
                    SampleAtmos(atmos, tile);
            }
        }

        if (atmos != null)
            EndAtmosSample(ent, atmos, now);
    }

    private static void BuildWindows(MapGridComponent grid, PulsarGridMemory mem, int size)
    {
        mem.Windows.Clear();
        var aabb = grid.LocalAABB;
        if (aabb.IsEmpty())
            return;

        var minX = (int) MathF.Floor(aabb.Left / size) * size;
        var minY = (int) MathF.Floor(aabb.Bottom / size) * size;
        var maxX = (int) MathF.Ceiling(aabb.Right);
        var maxY = (int) MathF.Ceiling(aabb.Top);

        for (var x = minX; x < maxX; x += size)
        {
            for (var y = minY; y < maxY; y += size)
            {
                mem.Windows.Add(new Vector2i(x, y));
            }
        }
    }

    private PulsarTileKind ClassifyTile(EntityUid gridUid, MapGridComponent grid, PulsarGridMemory mem, Vector2i tile, TimeSpan now)
    {
        if (!_maps.TryGetTileRef(gridUid, grid, tile, out var tileRef) || _turf.IsSpace(tileRef))
        {
            mem.Doors.Remove(tile);
            return PulsarTileKind.Space;
        }

        var kind = PulsarTileKind.Floor;
        EntityUid? door = null;

        var anchored = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            var found = ClassifyEntity(uid.Value);
            if (found is PulsarTileKind.Airlock or PulsarTileKind.Door)
                door = uid.Value;
            if (Priority(found) > Priority(kind))
                kind = found;
        }

        if (door != null && kind is PulsarTileKind.Airlock or PulsarTileKind.Door)
        {
            if (!mem.Doors.TryGetValue(tile, out var info) || info.Door != door.Value)
            {
                info = new PulsarDoorInfo { Door = door.Value, Tile = tile };
                mem.Doors[tile] = info;
            }

            UpdateDoor(info, now);
        }
        else
        {
            mem.Doors.Remove(tile);
        }

        return kind;
    }

    private PulsarTileKind ClassifyEntity(EntityUid uid)
    {
        if (_doorQuery.HasComp(uid))
            return _airlockQuery.HasComp(uid) ? PulsarTileKind.Airlock : PulsarTileKind.Door;

        if (_tags.HasTag(uid, WallTag))
            return PulsarTileKind.Wall;

        if (_tags.HasTag(uid, WindowTag))
            return PulsarTileKind.Window;

        if (_tags.HasTag(uid, TableTag) || HasComp<PlaceableSurfaceComponent>(uid) && HasComp<ClimbableComponent>(uid))
            return PulsarTileKind.Table;

        // Untagged solid walls (some special walls): an always-on occluder that is not a door.
        if (_occluderQuery.TryComp(uid, out var occluder) && occluder.Enabled && HasComp<AirtightComponent>(uid))
            return PulsarTileKind.Wall;

        return PulsarTileKind.Floor;
    }

    private static int Priority(PulsarTileKind kind)
    {
        return kind switch
        {
            PulsarTileKind.Airlock => 6,
            PulsarTileKind.Door => 5,
            PulsarTileKind.Wall => 4,
            PulsarTileKind.Window => 3,
            PulsarTileKind.Table => 2,
            PulsarTileKind.Floor => 1,
            _ => 0,
        };
    }

    private void UpdateDoor(PulsarDoorInfo info, TimeSpan now)
    {
        info.LastSeen = now;
        info.IsAirlock = _airlockQuery.HasComp(info.Door);

        if (_doorQuery.TryComp(info.Door, out var door))
            info.State = door.State;

        info.Bolted = _boltQuery.TryComp(info.Door, out var bolt) && bolt.BoltsDown;

        // Airlocks keep their access on the door electronics inside them; GetMainAccessReader resolves that.
        info.Access.Clear();
        if (_accessReader.GetMainAccessReader(info.Door, out var main) && main.Value.Comp.Enabled
            && _accessQuery.TryComp(info.Door, out var own) && own.Enabled)
        {
            foreach (var set in main.Value.Comp.AccessLists)
            {
                if (set.Count == 0)
                    continue;

                var levels = new string[set.Count];
                var i = 0;
                foreach (var level in set)
                    levels[i++] = level.Id;
                Array.Sort(levels, StringComparer.Ordinal);
                info.Access.Add(levels);
            }
        }
    }

    /// <summary>Doors currently in view get their open/closed/bolted state and access refreshed; deleted ones go.</summary>
    private void RefreshDoors(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        foreach (var mem in ent.Comp.Grids.Values)
        {
            _tileRemoveScratch.Clear();
            foreach (var (tile, info) in mem.Doors)
            {
                if (TerminatingOrDeleted(info.Door))
                {
                    _tileRemoveScratch.Add(tile);
                    continue;
                }

                if (mem.IsSeen(tile))
                    UpdateDoor(info, now);
            }

            foreach (var tile in _tileRemoveScratch)
                mem.Doors.Remove(tile);
        }
    }

    /// <summary>
    /// Humanoids standing on a tile a camera sees right now (by the last sweep's visibility mask) are remembered as
    /// crew, humanoids or enemies. People in containers (lockers, bags) are not seen.
    /// </summary>
    private void RefreshMobs(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;

        foreach (var mob in comp.Mobs.Values)
            mob.InView = false;

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } gridUid || !comp.Grids.TryGetValue(gridUid, out var mem))
                continue;

            if (!_gridQuery.TryComp(gridUid, out var grid))
                continue;

            if (_containers.IsEntityOrParentInContainer(uid, xform: xform))
                continue;

            var tile = _maps.LocalToTile(gridUid, grid, xform.Coordinates);
            if (!mem.IsSeen(tile))
                continue;

            if (!comp.Mobs.TryGetValue(uid, out var info))
            {
                info = new PulsarMobInfo { Entity = uid };
                comp.Mobs[uid] = info;
            }

            info.Grid = gridUid;
            info.Tile = tile;
            info.InView = true;
            info.LastSeen = now;
            info.Dead = _mobState.IsDead(uid);
            info.Critical = _mobState.IsCritical(uid);
            info.Name = string.Empty;
            info.Job = null;
            info.JobId = null;

            var identified = TryGetWornIdIdentity(uid, out var idName, out var job, out var jobId);
            if (identified)
            {
                info.Name = idName ?? string.Empty;
                info.Job = job;
                info.JobId = jobId;
            }

            if (IsWearingEnemySuit(comp, uid))
            {
                info.Kind = PulsarMobKind.Enemy;
            }
            else if (identified)
            {
                info.Kind = PulsarMobKind.Crew;
            }
            else
            {
                info.Kind = PulsarMobKind.Humanoid;
            }
        }

        _removeScratch.Clear();
        foreach (var (uid, info) in comp.Mobs)
        {
            if (TerminatingOrDeleted(uid) || now - info.LastSeen > comp.MobForgetTime)
                _removeScratch.Add(uid);
        }

        foreach (var uid in _removeScratch)
            comp.Mobs.Remove(uid);
    }

    public bool IsWearingEnemySuit(PulsarAutonomousAiComponent comp, EntityUid uid)
    {
        if (!_inventory.TryGetSlotEntity(uid, OuterSlot, out var suit))
            return false;

        if (MetaData(suit.Value).EntityPrototype is not { } proto)
            return false;

        if (_enemySuitCache.TryGetValue(proto.ID, out var cached))
            return cached;

        // Listed itself or through any parent (abstract ones too), unless an excluded parent is in the chain
        // (ERT hardsuits inherit the syndicate ones).
        var listed = false;
        var excluded = false;
        foreach (var (parentId, _) in _proto.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true))
        {
            listed |= comp.EnemySuits.Contains(parentId);
            excluded |= comp.EnemySuitExcludedParents.Contains(parentId);
        }

        var enemy = listed && !excluded;
        _enemySuitCache[proto.ID] = enemy;
        return enemy;
    }

    /// <summary>
    /// The ID card cameras can read: only the equipment ID slot, either a bare card or a PDA
    /// with a card inside. Cards in pockets or hands do not count.
    /// </summary>
    public bool TryGetWornId(EntityUid uid, out Entity<IdCardComponent> id)
    {
        id = default;
        if (!_inventory.TryGetSlotEntity(uid, IdSlot, out var slotUid))
            return false;

        if (TryComp(slotUid.Value, out IdCardComponent? idComp))
        {
            id = (slotUid.Value, idComp);
            return true;
        }

        if (TryComp(slotUid.Value, out PdaComponent? pda)
            && pda.ContainedId is { } contained
            && TryComp(contained, out idComp))
        {
            id = (contained, idComp);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Visual identification from the worn ID. Returns false when the ID slot is empty.
    /// <paramref name="name"/> is <see cref="IdCardComponent.FullName"/> or null when the card has no name.
    /// </summary>
    public bool TryGetWornIdIdentity(EntityUid uid, out string? name, out string? job, out string? jobId)
    {
        name = null;
        job = null;
        jobId = null;
        if (!TryGetWornId(uid, out var id))
            return false;

        if (id.Comp.FullName is { } full && !string.IsNullOrWhiteSpace(full))
            name = full.Trim();

        if (!string.IsNullOrWhiteSpace(id.Comp.LocalizedJobTitle))
            job = id.Comp.LocalizedJobTitle;

        jobId = JobFromWornId(id);
        return true;
    }

    /// <summary>ID FullName, or a localized "unknown" when the worn ID is missing or unnamed.</summary>
    public string VisualName(PulsarMobInfo info)
    {
        return string.IsNullOrWhiteSpace(info.Name)
            ? Loc.GetString("pulsar-unknown-person")
            : info.Name;
    }

    /// <summary>Job plus ID name, as used in «where is» answers. Job alone or "unknown" if the name is missing.</summary>
    public string VisualNameWithJob(PulsarMobInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.Name))
            return info.Job != null ? $"{info.Job} {info.Name}" : info.Name;

        return info.Job ?? Loc.GetString("pulsar-unknown-person");
    }

    /// <summary>Job prototype from this worn ID only: station record, then the card, then the job icon. Not hands or mind.</summary>
    private string? JobFromWornId(Entity<IdCardComponent> id)
    {
        if (TryComp<StationRecordKeyStorageComponent>(id, out var keyStorage) && keyStorage.Key is { } key
            && _records.TryGetRecord<GeneralStationRecord>(key, out var record) && !string.IsNullOrEmpty(record.JobPrototype))
            return record.JobPrototype;

        if (id.Comp.JobPrototype is { } jobProto)
            return jobProto.Id;

        return JobByIcon(id.Comp.JobIcon);
    }

}
