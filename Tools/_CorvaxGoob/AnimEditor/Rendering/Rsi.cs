using System.Text.Json;
using SkiaSharp;

namespace Abisfera.AnimEditor.Rendering;

/// <summary>An RGBA frame (straight alpha), y down.</summary>
public sealed record Frame(int W, int H, byte[] Pixels);

/// <summary>Reads RSI frames (meta.json + per-state png sheets) straight from Resources/Textures.</summary>
public sealed class RsiLoader
{
    private readonly string _textures;
    private readonly Dictionary<string, Meta?> _meta = new();
    private readonly Dictionary<string, Frame?> _png = new();
    private readonly Dictionary<(string, string, int), Frame?> _frames = new();

    private sealed record Meta(int W, int H, Dictionary<string, (int Dirs, int Per)> Directions);

    public RsiLoader(string resources)
    {
        _textures = Path.Combine(resources, "Textures");
    }

    public static string Normalize(string rsi)
    {
        var p = rsi.Replace('\\', '/').TrimStart('/');
        if (p.StartsWith("Textures/"))
            p = p["Textures/".Length..];
        return p;
    }

    private Meta? MetaOf(string rsi)
    {
        if (_meta.TryGetValue(rsi, out var m))
            return m;
        m = null;
        try
        {
            var path = Path.Combine(_textures, rsi, "meta.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path).TrimStart('\uFEFF'));
                var root = doc.RootElement;
                var size = root.GetProperty("size");
                var dirs = new Dictionary<string, (int, int)>();
                foreach (var st in root.GetProperty("states").EnumerateArray())
                {
                    var name = st.GetProperty("name").GetString() ?? "";
                    var nd = st.TryGetProperty("directions", out var d) ? d.GetInt32() : 1;
                    var per = st.TryGetProperty("delays", out var del) && del.ValueKind == JsonValueKind.Array && del.GetArrayLength() > 0
                        ? Math.Max(1, del[0].GetArrayLength())
                        : 1;
                    dirs[name] = (nd, per);
                }

                m = new Meta(size.GetProperty("x").GetInt32(), size.GetProperty("y").GetInt32(), dirs);
            }
        }
        catch
        {
            m = null;
        }

        _meta[rsi] = m;
        return m;
    }

    public bool HasState(string rsi, string state) => MetaOf(Normalize(rsi)) is { } m && m.Directions.ContainsKey(state);

    public static Frame? LoadPng(string path)
    {
        if (!File.Exists(path))
            return null;
        using var codec = SKCodec.Create(path);
        if (codec == null)
            return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var px = new byte[info.BytesSize];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var res = codec.GetPixels(info, handle.AddrOfPinnedObject());
            if (res is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                return null;
        }
        finally
        {
            handle.Free();
        }

        return new Frame(info.Width, info.Height, px);
    }

    /// <summary>First animation frame of <paramref name="state"/> in RSI direction <paramref name="dir"/> (0 S, 1 N, 2 E, 3 W).</summary>
    public Frame? State(string rsi, string state, int dir)
    {
        rsi = Normalize(rsi);
        var key = (rsi, state, dir);
        if (_frames.TryGetValue(key, out var f))
            return f;
        f = null;
        var meta = MetaOf(rsi);
        if (meta != null && meta.Directions.TryGetValue(state, out var info))
        {
            var pngPath = Path.Combine(_textures, rsi, state + ".png");
            if (!_png.TryGetValue(pngPath, out var sheet))
                _png[pngPath] = sheet = LoadPng(pngPath);
            if (sheet != null)
            {
                var cols = Math.Max(1, sheet.W / meta.W);
                var nd = info.Dirs;
                var idx = nd > 1 && dir < nd ? dir * info.Per : 0;
                var x0 = idx % cols * meta.W;
                var y0 = idx / cols * meta.H;
                if (x0 + meta.W <= sheet.W && y0 + meta.H <= sheet.H)
                {
                    var px = new byte[meta.W * meta.H * 4];
                    for (var y = 0; y < meta.H; y++)
                        Buffer.BlockCopy(sheet.Pixels, ((y0 + y) * sheet.W + x0) * 4, px, y * meta.W * 4, meta.W * 4);
                    f = new Frame(meta.W, meta.H, px);
                }
            }
        }

        _frames[key] = f;
        return f;
    }

    /// <summary>Frame multiplied by a colour (layer colour), like the sprite system.</summary>
    public static Frame Tint(Frame f, Color c)
    {
        var px = (byte[]) f.Pixels.Clone();
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = (byte) Math.Clamp(MathF.Round(px[i] * c.R), 0, 255);
            px[i + 1] = (byte) Math.Clamp(MathF.Round(px[i + 1] * c.G), 0, 255);
            px[i + 2] = (byte) Math.Clamp(MathF.Round(px[i + 2] * c.B), 0, 255);
            px[i + 3] = (byte) Math.Clamp(MathF.Round(px[i + 3] * c.A), 0, 255);
        }

        return new Frame(f.W, f.H, px);
    }

    /// <summary>Straight-alpha "over" of <paramref name="src"/> onto <paramref name="dst"/> (same size).</summary>
    public static void Over(Frame dst, Frame src)
    {
        var d = dst.Pixels;
        var s = src.Pixels;
        for (var i = 0; i < d.Length && i < s.Length; i += 4)
        {
            var sa = s[i + 3] / 255f;
            if (sa <= 0f)
                continue;
            var da = d[i + 3] / 255f;
            var oa = sa + da * (1 - sa);
            for (var c = 0; c < 3; c++)
                d[i + c] = (byte) Math.Clamp(MathF.Round((s[i + c] * sa + d[i + c] * da * (1 - sa)) / oa), 0, 255);
            d[i + 3] = (byte) Math.Clamp(MathF.Round(oa * 255f), 0, 255);
        }
    }

    /// <summary>Places a frame centred on a C x C canvas (as the client's Place does), with a px offset (y up).</summary>
    public static byte[] Place(Frame f, int c, int offX = 0, int offY = 0)
    {
        var dst = new byte[c * c * 4];
        var x0 = (c - f.W) / 2 + offX;
        var y0 = (c - f.H) / 2 - offY;
        for (var y = 0; y < f.H; y++)
        for (var x = 0; x < f.W; x++)
        {
            var dx = x0 + x;
            var dy = y0 + y;
            if (dx < 0 || dy < 0 || dx >= c || dy >= c)
                continue;
            Buffer.BlockCopy(f.Pixels, (y * f.W + x) * 4, dst, (dy * c + dx) * 4, 4);
        }

        return dst;
    }
}
