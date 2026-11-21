using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat.UI;

public enum ZakatNodeState : byte
{
    Locked,
    Available,
    Bought,
}

/// <summary>
/// One neuron of the map. Strings come already localized.
/// </summary>
public sealed record ZakatNeuralNode(int Id, int Branch, int Index, string Title, string Cost,
    ZakatNodeState State, bool Affordable);

/// <summary>
/// «Паутина» of «Закат»: glowing flesh neurons on fleshy tendrils around a central neuron
/// (styled after the Web of Intrigue from Prototype). Draws everything itself, handles hover tooltips and clicks.
/// </summary>
public sealed class ZakatNeuralMapControl : Control
{
    // flesh palette
    private static readonly Color Background = Color.FromHex("#1c0408");
    private static readonly Color VeinDark = Color.FromHex("#4a0b13");
    private static readonly Color VeinLight = Color.FromHex("#6e1621");
    private static readonly Color Outline = Color.FromHex("#0a0102");
    private static readonly Color TendrilBought = Color.FromHex("#d01426");
    private static readonly Color TendrilAvailable = Color.FromHex("#9a2a36");
    private static readonly Color TendrilLocked = Color.FromHex("#5e2228");
    private static readonly Color NodeBought = Color.FromHex("#c8102a");
    private static readonly Color NodeBoughtCore = Color.FromHex("#ff6b78");
    private static readonly Color NodeAvailable = Color.FromHex("#8c1a28");
    private static readonly Color NodeAvailableRing = Color.FromHex("#ff8f8f");
    private static readonly Color NodeLocked = Color.FromHex("#4c1d1f");
    private static readonly Color NodeLockedCore = Color.FromHex("#6e3a38");
    private static readonly Color Glow = Color.FromHex("#ff2034");
    private static readonly Color TextBright = Color.FromHex("#ffd9d2");
    private static readonly Color TextDim = Color.FromHex("#d0a4a0");
    private static readonly Color TextLocked = Color.FromHex("#b08886");
    private static readonly Color TooltipBack = Color.FromHex("#1d0507");
    private static readonly Color TooltipBorder = Color.FromHex("#8a1a26");

    private const float NodeRadius = 19f;
    private const float CenterRadius = 40f;

    private readonly Font _font;
    private readonly Font _fontSmall;
    private readonly Font _fontBig;

    private readonly List<(Vector2 A, Vector2 B, Vector2 C, Vector2 D, float Width, bool Light)> _veins = new();
    private readonly List<(Vector2 Pos, float Radius, float Alpha)> _blotches = new();

    private List<ZakatNeuralNode> _nodes = new();
    private List<string> _branchTitles = new();
    private int? _hovered;
    private Vector2 _mouse;
    private float _time;

    public string CenterTitle = string.Empty;
    public string CenterSubtitle = string.Empty;

    /// <summary>
    /// Size of the central neuron (the core grows with every «Рост»).
    /// </summary>
    public float CenterScale = 1f;

    /// <summary>
    /// «Рост 4»: the core opens an eye and grows tentacles.
    /// </summary>
    public bool Awakened;

    /// <summary>
    /// The central neuron is itself an upgrade (a host's «порабощение»): its id, state and cost.
    /// </summary>
    public ZakatNeuralNode? CenterNode;

    /// <summary>
    /// Nodes sit on a circle around the centre, each wired straight to it (no branches, any order).
    /// </summary>
    public bool Ring;

    public event Action<int>? OnNodePressed;

    public ZakatNeuralMapControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        var res = cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf");
        var bold = cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf");
        _font = new VectorFont(res, 11);
        _fontSmall = new VectorFont(res, 9);
        _fontBig = new VectorFont(bold, 13);

        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(760, 520);
        GenerateBackground();
    }

    public void SetNodes(List<ZakatNeuralNode> nodes, List<string> branchTitles)
    {
        _nodes = nodes;
        _branchTitles = branchTitles;
    }

    private void GenerateBackground()
    {
        // fixed seed: the same flesh every time it opens
        var rand = new System.Random(1931);
        Vector2 R() => new((float) rand.NextDouble(), (float) rand.NextDouble());
        for (var i = 0; i < 34; i++)
        {
            var a = R();
            var d = new Vector2((float) rand.NextDouble() - 0.5f, (float) rand.NextDouble() - 0.5f) * 0.6f;
            var b = a + d * 0.33f + new Vector2((float) rand.NextDouble() - 0.5f, (float) rand.NextDouble() - 0.5f) * 0.15f;
            var c = a + d * 0.66f + new Vector2((float) rand.NextDouble() - 0.5f, (float) rand.NextDouble() - 0.5f) * 0.15f;
            _veins.Add((a, b, c, a + d, 1f + (float) rand.NextDouble() * 2.2f, rand.NextDouble() < 0.35));
        }

        for (var i = 0; i < 46; i++)
            _blotches.Add((R(), 8f + (float) rand.NextDouble() * 40f, 0.05f + (float) rand.NextDouble() * 0.12f));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    #region geometry

    // three branches: a bit above the middle, the downward branch gets the longer reach;
    // one branch grows downwards from high up; four branches and the ring sit in the middle
    private Vector2 Center => new(PixelSize.X / 2f, PixelSize.Y * (Ring || _branchTitles.Count >= 4 ? 0.5f : _branchTitles.Count == 1 ? 0.28f : 0.4f));

    private const int CenterId = int.MinValue;

    private int BranchNodeCount(int branch)
    {
        var n = 0;
        foreach (var node in _nodes)
        {
            if (node.Branch == branch)
                n = Math.Max(n, node.Index + 1);
        }

        return n;
    }

    private static float BranchAngle(int branch, int branches)
    {
        // three branches: left (slightly up), right (slightly up), down
        if (branches == 3)
            return branch switch { 0 => -165f, 1 => -15f, _ => 90f } * MathF.PI / 180f;

        // four branches: an X, upper pair first; the lower pair steeper to clear the host's name under the centre
        if (branches == 4)
            return branch switch { 0 => -142f, 1 => -38f, 2 => 124f, _ => 56f } * MathF.PI / 180f;

        if (branches == 1)
            return MathF.PI / 2f;

        return (-90f + 360f * branch / Math.Max(1, branches)) * MathF.PI / 180f;
    }

    private Vector2 RingPosition(ZakatNeuralNode node)
    {
        var s = UIScale;
        var count = Math.Max(1, _nodes.Count);
        var angle = (-90f + 360f * node.Index / count) * MathF.PI / 180f;
        var radius = MathF.Min(PixelSize.X / 2f - 120f * s, PixelSize.Y / 2f - 72f * s);
        return Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private float CenterRadiusPx => CenterRadius * CenterScale * UIScale;

    private Vector2 NodePosition(ZakatNeuralNode node)
    {
        if (Ring)
            return RingPosition(node);

        var branches = Math.Max(1, _branchTitles.Count);
        var count = Math.Max(1, BranchNodeCount(node.Branch));
        var s = UIScale;
        var angle = BranchAngle(node.Branch, branches);
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

        // the furthest the branch can reach along its direction and still fit with its captions
        var center = Center;
        // the X keeps its captions on the outer side, so it needs a wider margin
        var marginX = branches == 4 ? 185f : 84f;
        var reachX = (PixelSize.X / 2f - marginX * s) / MathF.Max(0.001f, MathF.Abs(dir.X));
        var reachY = (dir.Y < 0 ? center.Y - 56f * s : PixelSize.Y - center.Y - 30f * s) / MathF.Max(0.001f, MathF.Abs(dir.Y));
        var reach = MathF.Min(reachX, reachY);

        var start = CenterRadiusPx + 66f * s;
        var t = count == 1 ? 0.5f : (float) node.Index / (count - 1);

        // four branches leave room around the central neuron for the host's name and the «порабощение» node
        if (branches == 4)
            start = CenterRadiusPx + (dir.Y > 0 ? 108f : 92f) * s;

        // a lone stem (the core's «Рост» chain) spreads its nodes evenly, clear of the growing core and its name
        if (branches == 1)
        {
            start = CenterRadiusPx + 84f * s;
            t = (node.Index + 0.5f) / count;
        }
        var dist = start + (reach - start) * t;

        // the branch wavers like a living nerve
        var side = new Vector2(-dir.Y, dir.X);
        var wave = MathF.Sin(t * MathF.PI * 1.5f) * 16f * s * (node.Branch % 2 == 0 ? 1f : -1f);
        return center + dir * dist + side * wave;
    }

    private ZakatNeuralNode? Previous(ZakatNeuralNode node)
    {
        if (Ring)
            return null;

        foreach (var other in _nodes)
        {
            if (other.Branch == node.Branch && other.Index == node.Index - 1)
                return other;
        }

        return null;
    }

    private int? NodeAt(Vector2 pixel)
    {
        var r = (NodeRadius + 4f) * UIScale;
        foreach (var node in _nodes)
        {
            if ((NodePosition(node) - pixel).LengthSquared() <= r * r)
                return node.Id;
        }

        var cr = CenterRadiusPx + 3f * UIScale;
        if (CenterNode != null && (Center - pixel).LengthSquared() <= cr * cr)
            return CenterId;

        return null;
    }

    #endregion

    #region input

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mouse = args.RelativePixelPosition;
        _hovered = NodeAt(_mouse);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = null;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (NodeAt(args.RelativePixelPosition) is not { } id)
            return;

        if (id == CenterId ? PressCenter() : Press(id))
            args.Handle();
    }

    /// <summary>
    /// A click on the central neuron: buys it when it is open.
    /// </summary>
    public bool PressCenter()
    {
        if (CenterNode is not { State: ZakatNodeState.Available } center)
            return false;

        OnNodePressed?.Invoke(center.Id);
        return true;
    }

    public bool Press(int id)
    {
        foreach (var node in _nodes)
        {
            if (node.Id == id && node.State == ZakatNodeState.Available)
            {
                OnNodePressed?.Invoke(id);
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<ZakatNeuralNode> Nodes => _nodes;

    #endregion

    #region drawing

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var s = UIScale;
        var size = PixelSize;

        // flesh backdrop
        handle.DrawRect(new UIBox2(Vector2.Zero, size), Background);
        DrawGlow(handle, Center, MathF.Max(size.X, size.Y) * 0.75f, Color.FromHex("#3d0810"), 0.55f);
        foreach (var (pos, radius, alpha) in _blotches)
            DrawGlow(handle, pos * size, radius * s, Color.FromHex("#4a0a12"), alpha * 2f);
        foreach (var (a, b, c, d, width, light) in _veins)
            DrawTube(handle, a * size, b * size, c * size, d * size, width * s, width * s * 0.4f, light ? VeinLight : VeinDark, null);

        // tendrils first, nodes on top
        foreach (var node in _nodes)
        {
            var from = Previous(node) is { } prev ? NodePosition(prev) : Center;
            var to = NodePosition(node);
            var color = node.State switch
            {
                ZakatNodeState.Bought => TendrilBought,
                ZakatNodeState.Available => TendrilAvailable,
                _ => TendrilLocked,
            };
            var dir = to - from;
            var perp = new Vector2(-dir.Y, dir.X) * ((node.Index % 2 == 0 ? 1f : -1f) * 0.16f);
            var b = from + dir * 0.33f + perp;
            var c = from + dir * 0.66f - perp * 0.6f;
            var w0 = (node.Index == 0 || Ring ? 10f : 8f) * s;
            DrawTube(handle, from, b, c, to, w0, 5f * s, color, Outline);

            if (node.State == ZakatNodeState.Bought)
            {
                // a pulse travels along the tendril
                var t = (_time * 0.6f + node.Index * 0.23f + node.Branch * 0.31f) % 1f;
                DrawGlow(handle, Bezier(from, b, c, to, t), 9f * s, Glow, 0.7f);
            }
        }

        DrawCenter(handle);

        foreach (var node in _nodes)
            DrawNode(handle, node);

        DrawBranchTitles(handle);

        if (_hovered == CenterId && CenterNode != null)
            DrawTooltip(handle, CenterNode);
        else if (_hovered is { } hovered)
        {
            foreach (var node in _nodes)
            {
                if (node.Id == hovered)
                    DrawTooltip(handle, node);
            }
        }
    }

    private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        var u = 1f - t;
        return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
    }

    /// <summary>
    /// Tapered fleshy tube along a cubic curve: dark outline, body, wet highlight and a few nodules.
    /// </summary>
    private void DrawTube(DrawingHandleScreen handle, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float w0, float w1, Color color, Color? outline)
    {
        const int segments = 22;
        var outer = new Vector2[(segments + 1) * 2];
        var body = new Vector2[(segments + 1) * 2];
        var shine = new Vector2[(segments + 1) * 2];

        for (var i = 0; i <= segments; i++)
        {
            var t = (float) i / segments;
            var p = Bezier(a, b, c, d, t);
            var q = Bezier(a, b, c, d, MathF.Min(1f, t + 0.01f)) - Bezier(a, b, c, d, MathF.Max(0f, t - 0.01f));
            var n = q.LengthSquared() > 0.0001f ? Vector2.Normalize(new Vector2(-q.Y, q.X)) : Vector2.UnitY;
            // slight bulges make it look like muscle, not a wire
            var w = (w0 + (w1 - w0) * t) * (1f + 0.12f * MathF.Sin(t * 19f + a.X * 0.01f));
            outer[i * 2] = p + n * (w / 2f + 1.6f * UIScale);
            outer[i * 2 + 1] = p - n * (w / 2f + 1.6f * UIScale);
            body[i * 2] = p + n * (w / 2f);
            body[i * 2 + 1] = p - n * (w / 2f);
            shine[i * 2] = p + n * (w * 0.32f);
            shine[i * 2 + 1] = p + n * (w * 0.12f);
        }

        if (outline is { } o)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, outer, o);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, body, color);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, shine, Color.InterpolateBetween(color, Color.White, 0.35f).WithAlpha(0.45f));
    }

    /// <summary>
    /// Soft radial glow (a triangle fan fading to transparent).
    /// </summary>
    private static void DrawGlow(DrawingHandleScreen handle, Vector2 pos, float radius, Color color, float alpha)
    {
        const int steps = 24;
        var verts = new DrawVertexUV2DColor[steps + 2];
        var inner = Color.FromSrgb(color.WithAlpha(Math.Clamp(alpha, 0f, 1f)));
        var outer = Color.FromSrgb(color.WithAlpha(0f));
        verts[0] = new DrawVertexUV2DColor(pos, new Vector2(0.5f, 0.5f), inner);
        for (var i = 0; i <= steps; i++)
        {
            var ang = MathF.PI * 2f * i / steps;
            verts[i + 1] = new DrawVertexUV2DColor(pos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius, new Vector2(0.5f, 0.5f), outer);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White, verts);
    }

    private void DrawCenter(DrawingHandleScreen handle)
    {
        var s = UIScale;
        var c = Center;
        var r = CenterRadiusPx;
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 2.2f);
        var state = CenterNode?.State ?? ZakatNodeState.Bought;

        DrawGlow(handle, c, r * 2.6f, Glow, state == ZakatNodeState.Locked ? 0.2f : 0.45f + 0.15f * pulse);

        if (Awakened)
            DrawTentacles(handle, c, r);

        // dendrites: more of them the bigger the core has grown
        var dendrites = 9 + (int) MathF.Round((CenterScale - 1f) * 10f);
        for (var i = 0; i < dendrites; i++)
        {
            var ang = i * 0.7f + 0.3f;
            var len = r * (1.35f + 0.25f * MathF.Sin(i * 2.1f));
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var side = new Vector2(-dir.Y, dir.X);
            DrawTube(handle, c, c + dir * len * 0.4f + side * 6f * s, c + dir * len * 0.75f - side * 5f * s, c + dir * len,
                7f * s, 1.5f * s, TendrilBought, Outline);
        }

        handle.DrawCircle(c, r + 3f * s, Outline);
        handle.DrawCircle(c, r, Color.FromHex("#8e0b1c"));
        handle.DrawCircle(c, r * 0.82f, Color.FromHex("#b5122a"));
        handle.DrawCircle(c + new Vector2(-r * 0.12f, -r * 0.1f), r * 0.55f, Color.FromHex("#d7283c"));
        DrawGlow(handle, c, r * 0.55f, NodeBoughtCore, 0.6f + 0.3f * pulse);
        handle.DrawCircle(c + new Vector2(-r * 0.38f, -r * 0.42f), r * 0.16f, Color.White.WithAlpha(0.35f));

        if (Awakened)
            DrawEye(handle, c, r);

        if (CenterNode is { } node)
        {
            if (state == ZakatNodeState.Available)
            {
                var ring = node.Affordable ? NodeAvailableRing.WithAlpha(0.55f + 0.45f * pulse) : TextDim.WithAlpha(0.6f);
                handle.DrawCircle(c, r + 5f * s, ring, false);
                handle.DrawCircle(c, r + 6f * s, ring, false);
            }
            else if (state == ZakatNodeState.Locked)
            {
                handle.DrawCircle(c, r, NodeLocked.WithAlpha(0.6f));
            }

            if (_hovered == CenterId)
                handle.DrawCircle(c, r + 8f * s, TextBright.WithAlpha(0.7f), false);
        }

        DrawCentered(handle, _fontBig, CenterTitle, c + new Vector2(0, r + 14f * s), TextBright, true);
        if (CenterSubtitle.Length > 0)
            DrawCentered(handle, _fontSmall, CenterSubtitle, c + new Vector2(0, r + 16f * s + _fontBig.GetLineHeight(1f)), state == ZakatNodeState.Locked ? TextLocked : TextDim, true);
    }

    /// <summary>
    /// Thick writhing tentacles growing out of the awakened core.
    /// </summary>
    private void DrawTentacles(DrawingHandleScreen handle, Vector2 c, float r)
    {
        var s = UIScale;
        const int count = 7;
        for (var i = 0; i < count; i++)
        {
            var baseAng = MathF.PI * 2f * i / count + 0.45f;
            var wave = MathF.Sin(_time * 1.3f + i * 1.7f);
            var wave2 = MathF.Sin(_time * 0.9f + i * 2.3f + 1f);
            var len = r * (2.05f + 0.25f * MathF.Sin(i * 1.9f)) * (1f + 0.04f * wave2);
            var dir = new Vector2(MathF.Cos(baseAng), MathF.Sin(baseAng));
            var side = new Vector2(-dir.Y, dir.X);
            var start = c + dir * r * 0.75f;
            var b = c + dir * len * 0.45f + side * r * 0.35f * wave;
            var cc = c + dir * len * 0.75f - side * r * 0.45f * wave2;
            var end = c + dir * len + side * r * 0.3f * MathF.Sin(_time * 1.7f + i);
            DrawTube(handle, start, b, cc, end, r * 0.36f, 2.5f * s, Color.FromHex("#6e0a18"), Outline);
            // suckers along the inner side
            for (var k = 1; k <= 4; k++)
            {
                var t = k / 5.5f;
                var p = Bezier(start, b, cc, end, t);
                var sucker = r * 0.07f * (1f - t * 0.7f);
                handle.DrawCircle(p, sucker + 1f * s, Outline);
                handle.DrawCircle(p, sucker, Color.FromHex("#c8566a"));
            }
        }
    }

    /// <summary>
    /// The eye of the awakened core: it follows the cursor and blinks now and then.
    /// </summary>
    private void DrawEye(DrawingHandleScreen handle, Vector2 c, float r)
    {
        var s = UIScale;
        var blinkPhase = _time % 5.3f;
        var open = blinkPhase < 0.18f ? MathF.Abs(blinkPhase - 0.09f) / 0.09f : 1f;
        var w = r * 0.62f;
        var h = r * 0.4f * MathF.Max(0.06f, open);
        var eye = c + new Vector2(0, r * 0.05f);

        DrawEllipse(handle, eye, w + 3f * s, h + 3f * s, Outline);
        DrawEllipse(handle, eye, w, h, Color.FromHex("#e9d8b0"));
        DrawEllipse(handle, eye + new Vector2(0, h * 0.35f), w * 0.95f, h * 0.55f, Color.FromHex("#d3a98a").WithAlpha(0.7f));
        if (open < 0.3f)
            return;

        var look = _mouse - eye;
        var max = r * 0.2f;
        if (look.Length() > max)
            look = Vector2.Normalize(look) * max;
        var iris = eye + new Vector2(look.X, look.Y * 0.5f);
        var ir = MathF.Min(r * 0.26f, h * 0.95f);
        DrawGlow(handle, iris, ir * 1.8f, Color.FromHex("#ff7a1a"), 0.35f);
        handle.DrawCircle(iris, ir + 1.5f * s, Color.FromHex("#3a0408"));
        handle.DrawCircle(iris, ir, Color.FromHex("#e8641a"));
        handle.DrawCircle(iris, ir * 0.7f, Color.FromHex("#ffb53a"));
        DrawEllipse(handle, iris, ir * 0.22f, ir * 0.85f, Color.FromHex("#120002"));
        handle.DrawCircle(iris + new Vector2(-ir * 0.4f, -ir * 0.4f), ir * 0.18f, Color.White.WithAlpha(0.7f));
    }

    private static void DrawEllipse(DrawingHandleScreen handle, Vector2 pos, float rx, float ry, Color color)
    {
        const int steps = 32;
        var verts = new Vector2[steps + 2];
        verts[0] = pos;
        for (var i = 0; i <= steps; i++)
        {
            var ang = MathF.PI * 2f * i / steps;
            verts[i + 1] = pos + new Vector2(MathF.Cos(ang) * rx, MathF.Sin(ang) * ry);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, verts, color);
    }

    private void DrawNode(DrawingHandleScreen handle, ZakatNeuralNode node)
    {
        var s = UIScale;
        var p = NodePosition(node);
        var r = NodeRadius * s;
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 3.2f + node.Id);
        var hovered = _hovered == node.Id;

        switch (node.State)
        {
            case ZakatNodeState.Bought:
                DrawGlow(handle, p, r * 2.6f, Glow, 0.55f);
                handle.DrawCircle(p, r + 3f * s, Outline);
                handle.DrawCircle(p, r, NodeBought);
                handle.DrawCircle(p + new Vector2(-r * 0.1f, -r * 0.1f), r * 0.62f, Color.FromHex("#e3263a"));
                DrawGlow(handle, p, r * 0.6f, NodeBoughtCore, 0.9f);
                break;
            case ZakatNodeState.Available:
                DrawGlow(handle, p, r * 1.9f, Glow, node.Affordable ? 0.08f + 0.12f * pulse : 0.04f);
                handle.DrawCircle(p, r + 3f * s, Outline);
                handle.DrawCircle(p, r, NodeAvailable);
                handle.DrawCircle(p, r * 0.5f, Color.FromHex("#7c1622"));
                var ring = node.Affordable ? NodeAvailableRing.WithAlpha(0.55f + 0.45f * pulse) : TextDim.WithAlpha(0.6f);
                handle.DrawCircle(p, r - 1.5f * s, ring, false);
                handle.DrawCircle(p, r - 2.5f * s, ring, false);
                break;
            default:
                handle.DrawCircle(p, r + 3f * s, Outline);
                handle.DrawCircle(p, r, NodeLocked);
                handle.DrawCircle(p, r * 0.45f, NodeLockedCore);
                break;
        }

        // wet sheen
        if (node.State != ZakatNodeState.Locked)
            handle.DrawCircle(p + new Vector2(-r * 0.4f, -r * 0.42f), r * 0.17f, Color.White.WithAlpha(0.4f));

        if (hovered)
        {
            handle.DrawCircle(p, r + 5f * s, TextBright.WithAlpha(0.8f), false);
            handle.DrawCircle(p, r + 6f * s, TextBright.WithAlpha(0.5f), false);
        }

        var costColor = node.State switch
        {
            ZakatNodeState.Bought => TextBright,
            ZakatNodeState.Available => node.Affordable ? TextBright : TextDim,
            _ => TextLocked,
        };
        DrawCentered(handle, _fontSmall, node.Cost, p - new Vector2(0, _fontSmall.GetLineHeight(1f) / 2f), costColor, true);

        var titleColor = node.State == ZakatNodeState.Locked ? TextLocked : TextBright;
        DrawTitle(handle, node, p, r, titleColor);
    }

    /// <summary>
    /// Node captions: beside the node on a vertical branch, otherwise alternating below/above
    /// so neighbours on a flat branch never overlap. Long captions wrap into two lines.
    /// </summary>
    private void DrawTitle(DrawingHandleScreen handle, ZakatNeuralNode node, Vector2 p, float r, Color color)
    {
        var s = UIScale;
        var lines = Wrap(handle, node.Title, _font, 132f * s);
        var lineHeight = _font.GetLineHeight(1f);
        var angle = BranchAngle(node.Branch, Math.Max(1, _branchTitles.Count));

        if (Ring)
        {
            // outside the ring: under the lower neurons, over the upper ones
            var under = p.Y >= Center.Y - 1f;
            var y0 = under ? p.Y + r + 5f * s : p.Y - r - 5f * s - lines.Count * lineHeight;
            foreach (var line in lines)
            {
                DrawCentered(handle, _font, line, new Vector2(p.X, y0), color, true);
                y0 += lineHeight;
            }

            return;
        }

        if (_branchTitles.Count == 4)
        {
            // the X: caption on the outer side of the node, away from the other branches
            var left = MathF.Cos(angle) < 0f;
            var y = p.Y - lines.Count * lineHeight / 2f;
            foreach (var line in lines)
            {
                var w = handle.GetDimensions(_font, line, 1f).X;
                var pos = new Vector2(left ? p.X - r - 8f * s - w : p.X + r + 8f * s, y);
                handle.DrawString(_font, pos + new Vector2(1, 1), line, 1f, Color.Black.WithAlpha(0.85f));
                handle.DrawString(_font, pos, line, 1f, color);
                y += lineHeight;
            }

            return;
        }

        if (MathF.Abs(MathF.Cos(angle)) < 0.5f)
        {
            // vertical branch: caption to the right of the node
            var y = p.Y - lines.Count * lineHeight / 2f;
            foreach (var line in lines)
            {
                var pos = new Vector2(p.X + r + 8f * s, y);
                handle.DrawString(_font, pos + new Vector2(1, 1), line, 1f, Color.Black.WithAlpha(0.85f));
                handle.DrawString(_font, pos, line, 1f, color);
                y += lineHeight;
            }

            return;
        }

        var below = node.Index % 2 == 0;
        var top = below ? p.Y + r + 5f * s : p.Y - r - 5f * s - lines.Count * lineHeight;
        foreach (var line in lines)
        {
            DrawCentered(handle, _font, line, new Vector2(p.X, top), color, true);
            top += lineHeight;
        }
    }

    private void DrawBranchTitles(DrawingHandleScreen handle)
    {
        for (var b = 0; b < _branchTitles.Count; b++)
        {
            var count = BranchNodeCount(b);
            if (count == 0)
                continue;

            // between the central neuron and the first node of the branch
            ZakatNeuralNode? first = null;
            foreach (var node in _nodes)
            {
                if (node.Branch == b && node.Index == 0)
                    first = node;
            }

            if (first == null)
                continue;

            var p = NodePosition(first);
            var s = UIScale;
            var title = _branchTitles[b].ToUpperInvariant();
            if (title.Length == 0)
                continue;

            var angle = BranchAngle(b, _branchTitles.Count);
            Vector2 at;
            if (_branchTitles.Count == 4)
            {
                // the X: upper branches named above their first neuron, lower ones below it (clear of the host's name)
                at = MathF.Sin(angle) < 0f
                    ? p - new Vector2(0, (NodeRadius + 8f) * s + _fontSmall.GetLineHeight(1f))
                    : p + new Vector2(0, (NodeRadius + 6f) * s);
            }
            else if (MathF.Abs(MathF.Cos(angle)) < 0.5f)
            {
                // vertical branch: to the left of its first neuron, clear of the centre caption
                var dim = handle.GetDimensions(_fontSmall, title, 1f);
                at = p + new Vector2(-(NodeRadius + 12f) * s - dim.X / 2f, -_fontSmall.GetLineHeight(1f) / 2f);
            }
            else
            {
                // flat branch: above its first neuron (its own caption goes below)
                at = p - new Vector2(0, (NodeRadius + 8f) * s + _fontSmall.GetLineHeight(1f));
            }

            DrawCentered(handle, _fontSmall, title, at, NodeAvailableRing, true);
        }
    }

    private void DrawTooltip(DrawingHandleScreen handle, ZakatNeuralNode node)
    {
        var s = UIScale;
        var pad = 8f * s;

        var lines = new List<(string Text, Font Font, Color Color)>
        {
            (node.Title, _fontBig, TextBright),
        };
        if (node.State != ZakatNodeState.Bought)
            lines.Add((node.Cost, _font, NodeAvailableRing));

        var width = 0f;
        var height = 0f;
        foreach (var (text, font, _) in lines)
        {
            width = MathF.Max(width, handle.GetDimensions(font, text, 1f).X);
            height += font.GetLineHeight(1f) + 2f * s;
        }

        var box = UIBox2.FromDimensions(_mouse + new Vector2(16f, 12f) * s, new Vector2(width + pad * 2f, height + pad * 2f));
        // keep it inside the map
        if (box.Right > PixelSize.X)
            box = box.Translated(new Vector2(PixelSize.X - box.Right - 4f, 0));
        if (box.Bottom > PixelSize.Y)
            box = box.Translated(new Vector2(0, PixelSize.Y - box.Bottom - 4f));
        if (box.Left < 0)
            box = box.Translated(new Vector2(-box.Left + 4f, 0));

        handle.DrawRect(new UIBox2(box.TopLeft - new Vector2(2f * s), box.BottomRight + new Vector2(2f * s)), TooltipBorder);
        handle.DrawRect(box, TooltipBack);
        var y = box.Top + pad;
        foreach (var (text, font, color) in lines)
        {
            handle.DrawString(font, new Vector2(box.Left + pad, y), text, 1f, color);
            y += font.GetLineHeight(1f) + 2f * s;
        }
    }

    private static List<string> Wrap(DrawingHandleScreen handle, string text, Font font, float maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var test = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && handle.GetDimensions(font, test, 1f).X > maxWidth)
                {
                    result.Add(line);
                    line = word;
                }
                else
                {
                    line = test;
                }
            }

            result.Add(line);
        }

        return result;
    }

    private static void DrawCentered(DrawingHandleScreen handle, Font font, string text, Vector2 topCenter, Color color, bool shadow = false)
    {
        var dim = handle.GetDimensions(font, text, 1f);
        var pos = topCenter - new Vector2(dim.X / 2f, 0);
        if (shadow)
            handle.DrawString(font, pos + new Vector2(1, 1), text, 1f, Color.Black.WithAlpha(0.85f));
        handle.DrawString(font, pos, text, 1f, color);
    }

    #endregion
}
