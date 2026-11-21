using Content.Server._CorvaxGoob.Pulsar.Units;
using Content.Shared.Examine;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 5.2: the linked units are the module's eyes where there are no cameras. Every second each unit's own
/// sensors (line of sight around it) refresh the memory map: tiles and doors (open / closed / bolted), people and
/// bodies, low pressure and fire. What a unit sees counts as «in view» for the rest of the module (alerts,
/// requests, bolts), and <c>pulsar_map</c> shows it.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;

    /// <summary>How far a unit's sensors reach (tiles).</summary>
    public const int UnitSensorRange = 6;

    public static readonly TimeSpan UnitSensorInterval = TimeSpan.FromSeconds(1);
    /// <summary>A hazard / blocked tile nobody has confirmed for this long is forgotten.</summary>
    public static readonly TimeSpan UnitMarkForget = TimeSpan.FromMinutes(2);

    private readonly Dictionary<(EntityUid Grid, Vector2i Origin), List<Vector2i>> _sensorWindows = new();

    private void UnitSensorStep(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (now < comp.NextUnitSense)
            return;
        comp.NextUnitSense = now + UnitSensorInterval;

        foreach (var mem in comp.Grids.Values)
        {
            mem.ClearUnitSeen();
            Forget(mem.Hazards, now, h => h.Seen);
            Forget(mem.Blocked, now, t => t);
        }

        foreach (var list in _sensorWindows.Values)
            list.Clear();

        var size = Math.Max(PulsarGridMemory.ChunkSize, comp.ScanWindowTiles / PulsarGridMemory.ChunkSize * PulsarGridMemory.ChunkSize);

        foreach (var unit in _units.LinkedUnits(ent))
        {
            if (!_mobState.IsAlive(unit))
                continue;

            var xform = Transform(unit);
            if (xform.GridUid is not { } gridUid || !comp.Grids.TryGetValue(gridUid, out var mem)
                || !_gridQuery.TryComp(gridUid, out var grid))
                continue;

            var center = _maps.LocalToTile(gridUid, grid, xform.Coordinates);
            var eye = _xforms.GetMapCoordinates(unit, xform);
            _units.SetLocation(unit, gridUid, center, PlaceName(ent, gridUid, center));

            for (var dx = -UnitSensorRange; dx <= UnitSensorRange; dx++)
            {
                for (var dy = -UnitSensorRange; dy <= UnitSensorRange; dy++)
                {
                    if (dx * dx + dy * dy > UnitSensorRange * UnitSensorRange)
                        continue;

                    var tile = center + new Vector2i(dx, dy);
                    if (mem.IsUnitSeen(tile))
                        continue;

                    var local = new EntityCoordinates(gridUid, (tile.X + 0.5f) * grid.TileSize, (tile.Y + 0.5f) * grid.TileSize);
                    var point = _xforms.ToMapCoordinates(local);
                    // A wall or a closed door itself is seen; what is behind it is not.
                    if (dx != 0 || dy != 0)
                    {
                        if (!_examine.InRangeUnOccluded(eye, point, UnitSensorRange + 1f, e => e == unit.Owner))
                            continue;
                    }

                    mem.SetUnitSeen(tile);
                    var kind = ClassifyTile(gridUid, grid, mem, tile, now);
                    mem.SetTile(tile, kind, now);

                    if (kind is PulsarTileKind.Space or PulsarTileKind.Wall)
                        continue;

                    var origin = new Vector2i(FloorDiv(tile.X, size) * size, FloorDiv(tile.Y, size) * size);
                    if (!_sensorWindows.TryGetValue((gridUid, origin), out var list))
                    {
                        list = new List<Vector2i>();
                        _sensorWindows[(gridUid, origin)] = list;
                    }

                    list.Add(tile);
                }
            }
        }

        // Air on what the units see: the same masks as the camera sweep, so alerts fire once whoever saw it first.
        foreach (var ((gridUid, origin), tiles) in _sensorWindows)
        {
            if (tiles.Count == 0 || !comp.Grids.TryGetValue(gridUid, out var mem))
                continue;

            var sample = BeginAtmosSample(ent, gridUid, origin, size);
            if (sample == null)
                continue;

            foreach (var tile in tiles)
            {
                SampleAtmos(sample, tile);
                var mixture = _atmos.GetTileMixture((gridUid, sample.Atmos, null), null, tile);
                if (mixture == null)
                    continue;
                if (mixture.Pressure < comp.LowPressure)
                    mem.Hazards[tile] = (PulsarHazardKind.LowPressure, now);
                else if (mixture.Temperature > comp.FireTemperature || _atmos.IsHotspotActive(gridUid, tile))
                    mem.Hazards[tile] = (PulsarHazardKind.Fire, now);
                else
                    mem.Hazards.Remove(tile);
            }

            EndAtmosSample(ent, sample, now);
        }
    }

    private static int FloorDiv(int v, int size)
    {
        return v >= 0 ? v / size : (v - size + 1) / size;
    }

    private static void Forget<T>(Dictionary<Vector2i, T> marks, TimeSpan now, Func<T, TimeSpan> seen)
    {
        List<Vector2i>? old = null;
        foreach (var (tile, value) in marks)
        {
            if (now - seen(value) > UnitMarkForget)
                (old ??= new List<Vector2i>()).Add(tile);
        }

        if (old == null)
            return;
        foreach (var tile in old)
            marks.Remove(tile);
    }

    /// <summary>Remembers a tile a unit could not get through (shown on the map, avoided by other units).</summary>
    public void MarkBlocked(EntityUid core, EntityUid grid, Vector2i tile)
    {
        if (TryComp<PulsarAutonomousAiComponent>(core, out var comp) && comp.Grids.TryGetValue(grid, out var mem))
            mem.Blocked[tile] = _timing.CurTime;
    }

    /// <summary>The door the module knows of on a tile (its memory map), if any.</summary>
    public PulsarDoorInfo? KnownDoor(EntityUid core, EntityUid grid, Vector2i tile)
    {
        return TryComp<PulsarAutonomousAiComponent>(core, out var comp) && comp.Grids.TryGetValue(grid, out var mem)
            && mem.Doors.TryGetValue(tile, out var info) ? info : null;
    }

    /// <summary>Tiles the module knows as blocked on a grid.</summary>
    public IEnumerable<Vector2i> KnownBlocked(EntityUid core, EntityUid grid)
    {
        if (TryComp<PulsarAutonomousAiComponent>(core, out var comp) && comp.Grids.TryGetValue(grid, out var mem))
            return mem.Blocked.Keys;
        return Array.Empty<Vector2i>();
    }

    public PulsarTileKind KnownTile(EntityUid core, EntityUid grid, Vector2i tile)
    {
        return TryComp<PulsarAutonomousAiComponent>(core, out var comp) && comp.Grids.TryGetValue(grid, out var mem)
            ? mem.GetTile(tile) : PulsarTileKind.Unknown;
    }
}
