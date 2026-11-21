// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Robust.Shared.Map.Components;

// ReSharper disable once CheckNamespace
namespace Content.Server.Atmos.EntitySystems;

// Corvax-Goob: lavaland ruin performance.
public sealed partial class AtmosphereSystem
{
    private readonly Queue<Vector2i> _prefillQueue = new();
    private readonly HashSet<Vector2i> _prefillVisited = new();

    /// <summary>
    /// Replaces the air of every tile of a grid that is connected to the outside (a cardinal neighbour with no grid tile,
    /// through non-airtight tiles) with the gas mixture of the map the grid sits on.
    /// Meant to run once, right after a ruin grid is placed on a planet and before the first atmos tick, so the
    /// station air saved in the map does not have to equalize with the planet air over a long atmos transient.
    /// Sealed rooms are left untouched.
    /// </summary>
    /// <returns>The number of tiles that got the map air.</returns>
    public int PrefillOpenTilesWithMapAtmosphere(EntityUid gridUid)
    {
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var gridAtmos)
            || !TryComp<MapGridComponent>(gridUid, out var mapGrid)
            || Transform(gridUid).MapUid is not { } mapUid
            || !_mapAtmosQuery.TryComp(mapUid, out var mapAtmos)
            || mapAtmos.Space)
            return 0;

        var mixture = mapAtmos.Mixture;
        _prefillQueue.Clear();
        _prefillVisited.Clear();

        // Seeds: tiles with air that can exchange gas with an empty (non-grid) neighbour.
        foreach (var (pos, tile) in gridAtmos.Tiles)
        {
            if (tile.Air == null || tile.NoGridTile || !_mapSystem.TryGetTileRef(gridUid, mapGrid, pos, out var own) || own.Tile.IsEmpty)
                continue;

            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var dir = (AtmosDirection) (1 << i);
                var neighbour = pos.Offset(dir);
                if (_mapSystem.TryGetTileRef(gridUid, mapGrid, neighbour, out var tileRef) && !tileRef.Tile.IsEmpty)
                    continue;

                if (IsTileAirBlocked(gridUid, pos, dir, mapGrid))
                    continue;

                if (_prefillVisited.Add(pos))
                    _prefillQueue.Enqueue(pos);
                break;
            }
        }

        // Flood fill through tiles that are not air-blocked between each other.
        while (_prefillQueue.TryDequeue(out var pos))
        {
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var dir = (AtmosDirection) (1 << i);
                var neighbour = pos.Offset(dir);
                if (_prefillVisited.Contains(neighbour)
                    || !gridAtmos.Tiles.TryGetValue(neighbour, out var tile)
                    || tile.Air == null
                    || tile.NoGridTile)
                    continue;

                if (IsTileAirBlocked(gridUid, pos, dir, mapGrid)
                    || IsTileAirBlocked(gridUid, neighbour, dir.GetOpposite(), mapGrid))
                    continue;

                _prefillVisited.Add(neighbour);
                _prefillQueue.Enqueue(neighbour);
            }
        }

        var count = 0;
        foreach (var pos in _prefillVisited)
        {
            if (!gridAtmos.Tiles.TryGetValue(pos, out var tile) || tile.Air is not { Immutable: false } air)
                continue;

            var volume = air.Volume;
            air.CopyFrom(mixture);
            // Keep the tile volume; scale moles if the map mixture uses a different volume.
            if (!MathHelper.CloseTo(volume, air.Volume))
            {
                air.Multiply(volume / air.Volume);
                air.Volume = volume;
            }

            count++;
        }

        _prefillQueue.Clear();
        _prefillVisited.Clear();
        return count;
    }
}
