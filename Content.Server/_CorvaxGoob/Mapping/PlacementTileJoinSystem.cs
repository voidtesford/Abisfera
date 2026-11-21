using System.Linq;
using System.Numerics;
using Robust.Shared.Placement;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._CorvaxGoob.Mapping;

/// <summary>
/// t559u: admin tile placement in space (line / fill modes) made one grid per tile.
/// The engine's <c>PlacementManager.PlaceNewTile</c> only joins an existing grid when the point is exactly over one of its
/// tiles, otherwise it creates a new one-tile grid; a line or fill started in open space sends every tile in map
/// coordinates, so every tile became its own grid. Here a freshly created one-tile grid whose tile touches (N/E/S/W) a
/// tile of another grid is folded into that grid, so the batch grows the first grid instead.
/// </summary>
public sealed class PlacementTileJoinSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private static readonly Vector2i[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    private List<Entity<MapGridComponent>> _grids = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlacementTileEvent>(OnTilePlaced);
    }

    private void OnTilePlaced(PlacementTileEvent ev)
    {
        // Erasing, or placed in a grid's own frame (the engine already used that grid).
        if (ev.TileType == 0 || !ev.Coordinates.IsValid(EntityManager) || HasComp<MapGridComponent>(ev.Coordinates.EntityId))
            return;

        TryJoin(_xform.ToMapCoordinates(ev.Coordinates));
    }

    /// <summary>
    /// If the tile at <paramref name="pos"/> is the only tile of its grid and touches another grid, move it into that grid.
    /// </summary>
    public bool TryJoin(MapCoordinates pos)
    {
        if (!_maps.TryFindGridAt(pos, out var placedUid, out var placed) || HasComp<MapComponent>(placedUid))
            return false;

        Vector2i? single = null;
        var enumerator = _maps.GetAllTilesEnumerator(placedUid, placed);
        while (enumerator.MoveNext(out var tileRef))
        {
            if (single != null)
                return false; // not a fresh one-tile grid: leave it alone
            single = tileRef.Value.GridIndices;
        }

        if (single is not { } placedIndex || Transform(placedUid).ChildCount > 0)
            return false;

        var tile = _maps.GetTileRef(placedUid, placed, placedIndex).Tile;

        // the biggest touching grid wins
        _grids.Clear();
        _maps.FindGridsIntersecting(pos.MapId, Box2.CenteredAround(pos.Position, new Vector2(3f, 3f)), ref _grids, approx: true, includeMap: false);
        Entity<MapGridComponent>? best = null;
        var bestCount = 0;
        Vector2i bestIndex = default;
        foreach (var other in _grids)
        {
            if (other.Owner == placedUid)
                continue;

            var index = _maps.WorldToTile(other.Owner, other.Comp, pos.Position);
            if (_maps.TryGetTileRef(other.Owner, other.Comp, index, out var existing) && !existing.Tile.IsEmpty)
                continue; // the tile overlaps that grid already

            var touches = false;
            foreach (var side in Sides)
            {
                if (_maps.TryGetTileRef(other.Owner, other.Comp, index + side, out var n) && !n.Tile.IsEmpty)
                {
                    touches = true;
                    break;
                }
            }

            if (!touches)
                continue;

            var count = _maps.GetAllTiles(other.Owner, other.Comp).Count();
            if (best != null && count <= bestCount)
                continue;

            best = other;
            bestCount = count;
            bestIndex = index;
        }

        if (best is not { } target)
            return false;

        Del(placedUid);
        _maps.SetTile(target.Owner, target.Comp, bestIndex, tile);
        return true;
    }
}
