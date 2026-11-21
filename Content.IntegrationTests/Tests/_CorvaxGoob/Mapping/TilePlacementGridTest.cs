using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Robust.Server.Placement;
using Robust.Shared;
using Robust.Shared.Enums;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Mapping;

/// <summary>
/// t559u: tiles placed in space with the admin tile placement (line / fill modes) end up as several grids.
/// The batches are built the way the client builds them (PlacementMode.LineCoordinates/GridCoordinates: the start point
/// plus whole steps, in the start point's frame) and sent through the real server handler.
/// </summary>
[TestFixture]
public sealed class TilePlacementGridTest
{
    private sealed record Ctx(TestPair Pair, MapId Map, EntityUid MapUid, Robust.Shared.Player.ICommonSession Session, int Tile);

    private static async Task<Ctx> Setup()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        Ctx ctx = null!;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var mapUid = maps.CreateMap(out var mapId);
            var session = server.PlayerMan.Sessions.First();
            var mob = server.EntMan.SpawnEntity(null, new MapCoordinates(0, 0, mapId));
            server.PlayerMan.SetAttachedEntity(session, mob);
            var tile = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            ctx = new Ctx(pair, mapId, mapUid, session, tile);
        });
        return ctx;
    }

    private static void Place(Ctx ctx, EntityCoordinates coords)
    {
        var server = ctx.Pair.Server;
        var msg = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = "AlignTileAny",
            IsTile = true,
            TileType = ctx.Tile,
            NetCoordinates = server.EntMan.GetNetCoordinates(coords),
            DirRcv = Direction.South,
            MsgChannel = ctx.Session.Channel,
        };
        server.ResolveDependency<IPlacementManager>().HandlePlacementRequest(msg);
    }

    /// <summary>Grids on the test map (the map itself excluded) and their tile counts.</summary>
    private static List<(EntityUid Grid, int Tiles)> Grids(Ctx ctx)
    {
        var server = ctx.Pair.Server;
        var maps = server.System<SharedMapSystem>();
        var list = new List<(EntityUid, int)>();
        var q = server.EntMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (q.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != ctx.Map || uid == ctx.MapUid)
                continue;
            list.Add((uid, maps.GetAllTiles(uid, grid).Count()));
        }
        return list;
    }

    private static string Describe(List<(EntityUid Grid, int Tiles)> grids)
        => $"{grids.Count} grids: " + string.Join(", ", grids.Select(g => $"{g.Grid}={g.Tiles}"));

    /// <summary>The client's line batch: start + i * unit step along the main axis (PlacementMode.LineCoordinates).</summary>
    private static IEnumerable<EntityCoordinates> Line(EntityCoordinates start, int count, Vector2 step)
    {
        for (var i = 0; i < count; i++)
            yield return new EntityCoordinates(start.EntityId, start.Position + step * i);
    }

    /// <summary>The client's fill batch (PlacementMode.GridCoordinates).</summary>
    private static IEnumerable<EntityCoordinates> Fill(EntityCoordinates start, int w, int h)
    {
        for (var x = 0; x < w; x++)
            for (var y = 0; y < h; y++)
                yield return new EntityCoordinates(start.EntityId, start.Position + new Vector2(x, y));
    }

    [Test]
    public async Task LineAndFillInSpaceMakeOneGrid()
    {
        var ctx = await Setup();
        var server = ctx.Pair.Server;
        var results = new List<string>();
        await server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"physics.grid_splitting = {server.ResolveDependency<IConfigurationManager>().GetCVar(CVars.GridSplitting)}");

            // 1. a line started in open space: AlignTileAny leaves the start point un-snapped, in map coordinates
            var start = new EntityCoordinates(ctx.MapUid, new Vector2(10.37f, 10.71f));
            foreach (var c in Line(start, 5, new Vector2(1, 0)))
                Place(ctx, c);
            var grids = Grids(ctx);
            results.Add("line x5 in space -> " + Describe(grids));
        });
        await server.WaitAssertion(() =>
        {
            // 2. a 3x3 fill started in open space, away from the line
            var start = new EntityCoordinates(ctx.MapUid, new Vector2(-20.6f, 4.2f));
            var before = Grids(ctx).Count;
            foreach (var c in Fill(start, 3, 3))
                Place(ctx, c);
            results.Add($"fill 3x3 in space -> {Grids(ctx).Count - before} new grids");
        });
        foreach (var r in results)
            TestContext.Out.WriteLine(r);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(results[0], Does.StartWith("line x5 in space -> 1 grids"), results[0]);
                Assert.That(results[1], Is.EqualTo("fill 3x3 in space -> 1 new grids"), results[1]);
            });
        });
        await ctx.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task NextToAGridAndDiagonal()
    {
        var ctx = await Setup();
        var server = ctx.Pair.Server;
        var results = new List<string>();
        EntityUid grid = default;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var g = maps.CreateGridEntity(ctx.Map);
            grid = g.Owner;
            for (var x = 0; x < 3; x++)
                maps.SetTile(g.Owner, g.Comp, new Vector2i(x, 0), new Tile(ctx.Tile));

            // 3. control: a line started on the grid (the client then works in the grid's frame)
            foreach (var c in Line(new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)), 4, new Vector2(1, 0)))
                Place(ctx, c);
            results.Add("line from the grid edge -> " + Describe(Grids(ctx)));

            // 4. a tile touching the grid only diagonally (single click near a corner)
            Place(ctx, new EntityCoordinates(grid, new Vector2(7.5f, 1.5f)));
            results.Add("diagonal tile -> " + Describe(Grids(ctx)));

            // 5. a fill that touches the grid, started in space 1.6 m off its edge (outside the 2 m snap box)
            var before = Grids(ctx).Count;
            foreach (var c in Fill(new EntityCoordinates(ctx.MapUid, new Vector2(2.2f, -2.1f)), 3, 2))
                Place(ctx, c);
            results.Add($"fill from 1.6 m off the edge -> {Grids(ctx).Count - before} new grids");
        });
        foreach (var r in results)
            TestContext.Out.WriteLine(r);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(results[0], Does.StartWith("line from the grid edge -> 1 grids"), results[0]);
                // engine rule, not changed: tiles touching only by a corner are split apart (physics.grid_splitting)
                Assert.That(results[1], Does.StartWith("diagonal tile -> 2 grids"), results[1]);
                // the fill is a row away from the grid (not touching it): one new grid, not one per tile
                Assert.That(results[2], Is.EqualTo("fill from 1.6 m off the edge -> 1 new grids"), results[2]);
            });
        });
        await ctx.Pair.CleanReturnAsync();
    }
}
