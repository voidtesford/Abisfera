using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.Client._CorvaxGoob.ProcAnim;
using Content.Server._CorvaxGoob.Gilbert.Finisher;
using Content.Shared._CorvaxGoob.ProcAnim;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Gilbert;

/// <summary>
/// fix3: since RT 266 a packaged / launcher client gets every .rsi compacted into one .rsic sheet, so the puppet can't
/// read <c>state.png</c> any more: before the fix it baked nothing, fell back to moving the bare body-part layers and
/// left all clothing and gear static. These check that every piece of equipment is baked into the puppet (originals
/// hidden) and that frames cut out of a packed sheet are pixel-identical to the loose state PNGs.
/// </summary>
[TestFixture]
public sealed class GilbertFix3Test
{
    private static async Task RunSeconds(Pair.TestPair pair, double seconds)
    {
        var rate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        await pair.RunTicksSync((int) Math.Ceiling(seconds * rate) + 1);
    }

    [Test]
    public async Task EquipmentLayersArePuppeted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var sEnt = server.EntMan;
        var cEnt = client.EntMan;

        EntityUid a = default, v = default;
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            for (var x = -6; x <= 6; x++)
            for (var y = -6; y <= 6; y++)
                mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            a = GilbertTestUtil.SpawnActive(sEnt, "MobGilbert", map.GridCoords);
            v = sEnt.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1.3f, 0.2f)));
        });
        await RunSeconds(pair, 0.5);

        var ca = pair.ToClientUid(a);
        var equipment = new List<SpriteComponent.Layer>();
        await client.WaitPost(() =>
        {
            var sprite = cEnt.GetComponent<SpriteComponent>(ca);
            equipment = sprite.AllLayers.OfType<SpriteComponent.Layer>()
                .Where(l => l.Visible && l.ActualRsi?.Path.ToString().Contains("/Clothing/") == true)
                .ToList();
        });
        // Jumpsuit, armour, helmet, mask, gloves, boots, belt, backpack, dog tag.
        Assert.That(equipment, Has.Count.GreaterThanOrEqualTo(8), "Gilbert spawns fully equipped");

        await server.WaitPost(() => Assert.That(server.System<GilbertFinisherSystem>().TryStart(a, v, out var err), Is.True, err));
        var intro = 0f;
        await server.WaitPost(() => intro = sEnt.GetComponent<ProcAnimComponent>(a).Intro);
        await RunSeconds(pair, intro + 0.4);

        await client.WaitAssertion(() =>
        {
            var vis = cEnt.System<ProcAnimVisualsSystem>();
            var sprite = cEnt.GetComponent<SpriteComponent>(ca);
            Assert.That(vis.GetPuppetResult(ca), Is.Not.Null, "side-view puppet baked (no whole-layer fallback)");
            Assert.That(vis.GetPuppetLayer(ca, ProcAnimPuppetGroup.Torso), Is.Not.Null);
            var hidden = vis.GetPuppetHiddenLayers(ca).ToHashSet();
            foreach (var layer in equipment)
            {
                if (!sprite.AllLayers.Contains(layer))
                    continue;
                Assert.That(hidden, Does.Contain(layer),
                    $"{layer.ActualRsi?.Path}/{layer.ActualState?.StateId.Name} is baked into the puppet");
                Assert.That(layer.Visible, Is.False, $"{layer.ActualRsi?.Path} original hidden under the puppet");
            }

            // Nothing from the clothing RSIs is left drawn as a static original.
            var staticClothing = sprite.AllLayers.OfType<SpriteComponent.Layer>()
                .Where(l => l.Visible && l.ActualRsi?.Path.ToString().Contains("/Clothing/") == true)
                .Select(l => $"{l.ActualRsi!.Path}/{l.ActualState?.StateId.Name}")
                .ToList();
            Assert.That(staticClothing, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The .rsic sheet layout of RobustToolbox's packer (RsiLoading.GenerateAtlas), from the loose files.</summary>
    private static Image<Rgba32> PackSheet(IResourceManager res, ResPath rsiPath, out int frameW, out int frameH)
    {
        using var metaStream = res.ContentFileRead(rsiPath / "meta.json");
        using var meta = JsonDocument.Parse(metaStream);
        frameW = meta.RootElement.GetProperty("size").GetProperty("x").GetInt32();
        frameH = meta.RootElement.GetProperty("size").GetProperty("y").GetInt32();

        var states = new List<(Image<Rgba32> Image, int Frames)>();
        foreach (var state in meta.RootElement.GetProperty("states").EnumerateArray())
        {
            var name = state.GetProperty("name").GetString()!;
            var dirs = state.TryGetProperty("directions", out var d) ? d.GetInt32() : 1;
            var frames = 0;
            if (state.TryGetProperty("delays", out var delays))
                foreach (var dir in delays.EnumerateArray())
                    frames += dir.GetArrayLength();
            else
                frames = dirs;
            using var png = res.ContentFileRead(rsiPath / $"{name}.png");
            states.Add((Image.Load<Rgba32>(png), frames));
        }

        var total = states.Sum(s => s.Frames);
        var dimX = (int) MathF.Ceiling(MathF.Sqrt(total));
        var dimY = (int) MathF.Ceiling((float) total / dimX);
        var sheet = new Image<Rgba32>(dimX * frameW, dimY * frameH);
        var index = 0;
        foreach (var (image, frames) in states)
        {
            var srcCols = image.Width / frameW;
            for (var i = 0; i < frames; i++, index++)
            {
                var sx = i % srcCols * frameW;
                var sy = i / srcCols * frameH;
                var dx = index % dimX * frameW;
                var dy = index / dimX * frameH;
                for (var y = 0; y < frameH; y++)
                for (var x = 0; x < frameW; x++)
                    sheet[dx + x, dy + y] = image[sx + x, sy + y];
            }

            image.Dispose();
        }

        return sheet;
    }

    [Test]
    [TestCase("/Textures/Mobs/Species/Human/parts.rsi")]
    [TestCase("/Textures/Clothing/OuterClothing/Armor/bulletproof.rsi")]
    [TestCase("/Textures/_NF/Clothing/Uniforms/Jumpsuit/military.rsi")]
    [TestCase("/Textures/_CorvaxGoob/Clothing/Head/Helmets/shogkk_helmet.rsi")]
    public async Task PackedSheetFramesMatchStatePngs(string path)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var res = client.ResolveDependency<IResourceManager>();
            var cache = client.ResolveDependency<IResourceCache>();
            var vis = client.EntMan.System<ProcAnimVisualsSystem>();
            var rsiPath = new ResPath(path);
            var rsi = cache.GetResource<RSIResource>(rsiPath).RSI;

            using var sheet = PackSheet(res, rsiPath, out _, out _);
            var origin = ProcAnimVisualsSystem.SheetOrigin(rsi);
            Assert.That(origin, Is.Not.Null);

            var checkedFrames = 0;
            foreach (var state in rsi)
            {
                var dirs = state.RsiDirections switch
                {
                    RsiDirectionType.Dir1 => 1,
                    RsiDirectionType.Dir4 => 4,
                    _ => 8,
                };
                for (var d = 0; d < dirs; d++)
                for (var f = 0; f < Math.Max(1, state.DelayCount); f++)
                {
                    var dir = (RsiDirection) d;
                    var loose = vis.RsiFrame(rsi, state, dir, f);
                    var packed = ProcAnimVisualsSystem.SheetFrame(sheet, origin!.Value, state, dir, f);
                    Assert.That(loose, Is.Not.Null, $"{state.StateId.Name} {dir} {f}: loose frame");
                    Assert.That(packed, Is.EqualTo(loose!.Value.Pixels), $"{state.StateId.Name} {dir} {f}: packed frame");
                    checkedFrames++;
                }
            }

            Assert.That(checkedFrames, Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }
}
