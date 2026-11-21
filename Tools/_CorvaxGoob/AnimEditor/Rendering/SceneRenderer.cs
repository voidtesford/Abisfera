using System.Numerics;
using Abisfera.AnimEditor.Model;
using Content.Client._CorvaxGoob.ProcAnim;
using Content.Shared._CorvaxGoob.ProcAnim;
using SkiaSharp;

namespace Abisfera.AnimEditor.Rendering;

/// <summary>Something drawn that the mouse can pick: a puppet group (its bone) or a prop.</summary>
public sealed record HitItem(int Actor, ProcAnimBone Bone, int Prop, Affine ImageToScreen, byte[] Pixels, int W, int H, SKImage Image);

/// <summary>Per actor result of a render: solved pose, pivots used, entity position, the actor's screen transform.</summary>
public sealed record ActorFrame(int Actor, ProcAnimPose[] Pose, IReadOnlyDictionary<ProcAnimBone, Vector2> Pivots, Vector2 Entity,
    Affine Scr, Affine BodyToScreen)
{
    /// <summary>Screen position of a forward-frame point of the actor (px relative to its entity, y up).</summary>
    public Vector2 ToScreen(Vector2 local) => Scr.Apply(local);
}

public sealed class RenderOptions
{
    public bool Effects = true;
    public bool Flash = true;
    public bool Shake = false;
    /// <summary>Procedural layers on (walk, idle, drag, daze, look, IK, hit recoil), like the game.</summary>
    public bool Procedural = true;
    public float Alpha = 1f;
    public SKColor? Tint;
    public bool CollectHits = true;
    /// <summary>Only these actors are drawn (null: all), e.g. the onion skin of the edited puppet.</summary>
    public Func<int, bool>? Actors;
}

/// <summary>
/// Draws a scene the way the game / preview.py does: each actor is its puppet (one baked image per limb group), posed
/// by the game's own <see cref="ProcAnimTimeline.Solve"/>, whole-pixel offsets, props, held / slung items, forced
/// views, flips, hides, flash, blood droplets, hit effects, faces and dizzy stars.
/// </summary>
public sealed class SceneRenderer
{
    private readonly GameRepo _repo;
    private readonly RsiLoader _rsi;
    public readonly PuppetCache Puppets;
    private readonly Dictionary<(string, string), SKImage?> _props = new();

    public SceneRenderer(GameRepo repo, RsiLoader rsi)
    {
        _repo = repo;
        _rsi = rsi;
        Puppets = new PuppetCache(repo, rsi);
    }

    public List<HitItem> Hits { get; } = new();
    public List<ActorFrame> Frames { get; } = new();

    public static bool Wields(ProcAnimScenePrototype proto) => proto.Actors.Any(a => a.Items.Any(i => i.Wield));

    /// <summary>
    /// Solving copy of the scene with the procedural layers removed (only the keys, joint limits and the keyed props).
    /// </summary>
    public static ProcAnimScenePrototype WithoutProcedural(ProcAnimScenePrototype p)
    {
        foreach (var a in p.Actors)
        {
            a.Walk = null;
            a.Idle = null;
            a.Drag = null;
            a.Daze = null;
            a.Look = null;
            a.Ik.Clear();
        }

        return p;
    }

    public void Render(SKCanvas canvas, ProcAnimScenePrototype proto, Func<int, Appearance> apps, float at, float real, Affine cam,
        RenderOptions opt)
    {
        if (opt.CollectHits)
        {
            Hits.Clear();
            Frames.Clear();
        }

        var wield = Wields(proto);
        var n = proto.Actors.Count;
        var shake = opt.Shake ? ShakeAt(proto, real) : Vector2.Zero;
        var ys = Enumerable.Range(0, n).Select(i => ProcAnimTimeline.Evaluate(proto.Actors[i].Move, at).Y).ToArray();
        var order = Enumerable.Range(0, n).ToList();
        order.Sort((a, b) => MathF.Abs(ys[a] - ys[b]) <= 1e-6f ? b - a : ys[a] > ys[b] ? -1 : 1);
        foreach (var i in order)
            if (opt.Actors == null || opt.Actors(i))
                DrawActor(canvas, proto, apps, i, at, real, shake, cam, opt, wield);
        if (opt.Effects && opt.Alpha >= 1f)
        {
            DrawBlood(canvas, proto, apps, real, shake, cam, wield);
            DrawFx(canvas, proto, apps, at, real, shake, cam, wield);
            DrawStars(canvas, proto, apps, at, shake, cam, wield);
        }
    }

    private Func<int, IReadOnlyDictionary<ProcAnimBone, Vector2>?> OtherPivots(ProcAnimScenePrototype proto, Func<int, Appearance> apps, bool wield)
        => j => j >= 0 && j < proto.Actors.Count ? Puppets.Main(proto.Actors[j], apps(j), wield).Pivots : null;

    public ProcAnimPose[] Pose(ProcAnimScenePrototype proto, Func<int, Appearance> apps, int i, float at, out Puppet puppet)
    {
        var wield = Wields(proto);
        var tr = proto.Actors[i];
        puppet = Puppets.For(tr, apps(i), at, wield);
        return ProcAnimTimeline.Solve(proto, i, at, puppet.Pivots, true, OtherPivots(proto, apps, wield));
    }

    private SKPaint MakePaint(RenderOptions opt, bool flash)
    {
        var paint = new SKPaint { IsAntialias = false, FilterQuality = SKFilterQuality.None };
        if (opt.Tint is { } tint)
        {
            paint.ColorFilter = SKColorFilter.CreateBlendMode(tint.WithAlpha((byte) (255 * opt.Alpha)), SKBlendMode.SrcIn);
        }
        else if (flash)
        {
            // multiply by (1, .55, .55): the white-red flash of the game
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
            {
                1, 0, 0, 0, 0,
                0, 0.55f, 0, 0, 0,
                0, 0, 0.55f, 0, 0,
                0, 0, 0, opt.Alpha, 0,
            });
        }
        else if (opt.Alpha < 1f)
        {
            paint.Color = SKColors.White.WithAlpha((byte) (255 * opt.Alpha));
        }

        return paint;
    }

    private void DrawActor(SKCanvas canvas, ProcAnimScenePrototype proto, Func<int, Appearance> apps, int i, float at, float real,
        Vector2 shake, Affine cam, RenderOptions opt, bool wield)
    {
        var tr = proto.Actors[i];
        var m = ProcAnimTimeline.Evaluate(tr.Move, at);
        var ex = m.X * 32f;
        var ey = m.Y * 32f;
        var puppet = Puppets.For(tr, apps(i), at, wield);
        var pose = ProcAnimTimeline.Solve(proto, i, at, puppet.Pivots, true, OtherPivots(proto, apps, wield));
        var inh = ProcAnimTimeline.HeldInHands(tr, at);
        var b = pose[(int) ProcAnimBone.Body];
        var body = Affine.Translate(b.X, b.Y) * Affine.Rotate(b.Rot);
        var flash = opt.Flash && proto.Events.Any(e => e.Flash && e.TargetIndex == i
                                                      && ProcAnimTimeline.RealTime(proto, e.Time) <= real
                                                      && real < ProcAnimTimeline.RealTime(proto, e.Time) + 0.12f);
        var ash = opt.Effects ? ProcAnimFx.Shake(proto, i, real) : Vector2.Zero;
        var fl = ProcAnimFx.ViewFlipScale(tr, at);
        var actor = cam * Affine.Translate(MathF.Round(ex + shake.X) + ash.X, MathF.Round(ey - shake.Y) + ash.Y);
        var scr = actor * Affine.Scale(fl, 1f);
        if (opt.CollectHits)
            Frames.Add(new ActorFrame(i, pose, puppet.Pivots, new Vector2(ex, ey), scr, scr * body));

        using var paint = MakePaint(opt, flash);
        var hiddenAll = ProcAnimTimeline.IsHidden(tr, ProcAnimBone.Body, at);
        var c = puppet.Size;
        var pix = new Affine(1, 0, 0, -1, -c / 2f, c / 2f);
        var drawnGroups = new HashSet<ProcAnimPuppetGroup>();
        for (var g = 0; g < ProcAnimPuppet.GroupCount; g++)
            if (puppet.Images[g] != null)
                drawnGroups.Add((ProcAnimPuppetGroup) g);

        for (var g = 0; g < ProcAnimPuppet.GroupCount; g++)
        {
            var group = (ProcAnimPuppetGroup) g;
            var img = puppet.Images[g];
            if (img == null)
                continue;
            if (group is ProcAnimPuppetGroup.HeldNear or ProcAnimPuppetGroup.HeldFar && !inh)
                continue;
            if (group == ProcAnimPuppetGroup.Slung && inh)
                continue;
            var bone = Puppet.PartOf(group);
            if (hiddenAll || ProcAnimTimeline.IsHidden(tr, bone ?? ProcAnimBone.Body, at))
                continue;
            var lay = Affine.Identity;
            if (bone is { } bn)
            {
                var o = pose[(int) bn];
                lay = Affine.Translate(MathF.Round(o.X), MathF.Round(o.Y)) * Affine.Rotate(o.Rot);
                var fs = ProcAnimTimeline.FlipScale(tr, bn, at);
                if (fs != 1f)
                    lay *= Affine.Scale(fs, 1f);
            }

            DrawProps(canvas, proto, i, tr, group, at, pose, scr, body, true, opt, paint, drawnGroups);
            var f = scr * body * lay * pix;
            DrawImage(canvas, img, f, paint);
            if (opt.CollectHits)
                Hits.Add(new HitItem(i, bone ?? ProcAnimBone.Body, -1, f, puppet.Pixels[g]!, c, c, img));
            DrawProps(canvas, proto, i, tr, group, at, pose, scr, body, false, opt, paint, drawnGroups);
        }

        if (!hiddenAll)
            DrawProps(canvas, proto, i, tr, null, at, pose, scr, body, null, opt, paint, drawnGroups);
    }

    private static ProcAnimPuppetGroup PropGroup(ProcAnimProp pr)
        => pr.Part == ProcAnimBone.Body ? ProcAnimPuppetGroup.Torso : Enum.Parse<ProcAnimPuppetGroup>(pr.Part.ToString());

    private void DrawProps(SKCanvas canvas, ProcAnimScenePrototype proto, int actor, ProcAnimActor tr, ProcAnimPuppetGroup? g, float t,
        ProcAnimPose[] pose, Affine scr, Affine body, bool? under, RenderOptions opt, SKPaint paint, HashSet<ProcAnimPuppetGroup> drawn)
    {
        for (var j = 0; j < tr.Props.Count; j++)
        {
            var pr = tr.Props[j];
            var pg = PropGroup(pr);
            if (under == null)
            {
                if (!(pr.Front || !drawn.Contains(pg)))
                    continue;
            }
            else
            {
                if (pr.Front || pr.Under != under || pg != g)
                    continue;
            }

            if (!ProcAnimTimeline.PropVisible(tr, pr, t))
                continue;
            var (img, sc, extra) = PropImage(proto, pr);
            if (img == null)
                continue;
            var basePose = pr.Part == ProcAnimBone.Body ? default : pose[(int) pr.Part];
            var pf = pr.Part == ProcAnimBone.Body ? 1f : ProcAnimTimeline.FlipScale(tr, pr.Part, t);
            var pp = ProcAnimTimeline.PropPose(pr, basePose, t, pf, extra);
            var pix = new Affine(sc * pf, 0, 0, -sc, -img.Width / 2f * sc * pf, img.Height / 2f * sc);
            var f = scr * body * Affine.Translate(MathF.Round(pp.X), MathF.Round(pp.Y)) * Affine.Rotate(pp.Rot) * pix;
            DrawImage(canvas, img, f, paint);
            if (opt.CollectHits && PropPixels(proto, pr) is { } px)
                Hits.Add(new HitItem(actor, pr.Part, j, f, px, img.Width, img.Height, img));
        }
    }

    private (string Rsi, string State)? PropSource(ProcAnimScenePrototype proto, ProcAnimProp pr)
    {
        if (pr.ItemIndex >= 0 && pr.ItemIndex < proto.Items.Count && proto.Items[pr.ItemIndex].PreviewSprite is { } ps)
            return (ps.CanonPath, pr.ItemState);
        if (pr.Sprite is { } s)
            return (s.CanonPath, pr.State);
        return null;
    }

    private (SKImage? Image, float Scale, float Extra) PropImage(ProcAnimScenePrototype proto, ProcAnimProp pr)
    {
        var bound = pr.ItemIndex >= 0 && pr.ItemIndex < proto.Items.Count && proto.Items[pr.ItemIndex].PreviewSprite != null;
        var src = PropSource(proto, pr);
        if (src is not { } s)
            return (null, 1f, 0f);
        var img = PropSk(s.Rsi, s.State);
        return bound ? (img, pr.ItemScale ?? pr.Scale, pr.ItemRotation) : (img, pr.Scale, 0f);
    }

    private byte[]? PropPixels(ProcAnimScenePrototype proto, ProcAnimProp pr)
        => PropSource(proto, pr) is { } s ? _rsi.State(s.Rsi, s.State, 0)?.Pixels : null;

    private SKImage? PropSk(string rsi, string state)
    {
        if (_props.TryGetValue((rsi, state), out var img))
            return img;
        var f = _rsi.State(rsi, state, 0);
        img = f == null ? null : SKImage.FromPixelCopy(new SKImageInfo(f.W, f.H, SKColorType.Rgba8888, SKAlphaType.Unpremul), f.Pixels, f.W * 4);
        _props[(rsi, state)] = img;
        return img;
    }

    public static void DrawImage(SKCanvas canvas, SKImage img, Affine f, SKPaint paint)
    {
        canvas.Save();
        var mm = f.ToSk();
        canvas.Concat(ref mm);
        canvas.DrawImage(img, 0, 0, paint);
        canvas.Restore();
    }

    // ---------------- effects (ProcAnimBloodOverlay / preview mirror) ----------------

    public static Vector2 ShakeAt(ProcAnimScenePrototype proto, float t)
    {
        float sx = 0, sy = 0;
        foreach (var e in proto.Events)
        {
            var te = ProcAnimTimeline.RealTime(proto, e.Time);
            if (e.Shake > 0 && te <= t)
            {
                var k = MathF.Exp(-(t - te) * 9f) * 0.35f * e.Shake * 32f;
                sx += k * MathF.Cos((t - te) * 60f);
                sy += k * 0.4f * MathF.Sin((t - te) * 47f);
            }
        }

        return new Vector2(sx, sy);
    }

    private static readonly Color BloodDefault = Color.FromHex("#800000");

    private static void Dot(SKCanvas canvas, Affine cam, Vector2 entity, float x, float y, float w, float h, Color col, Vector2 shake, bool snap,
        Vector2 ash = default, float fl = 1f)
    {
        x = x * fl + ash.X;
        y += ash.Y;
        if (snap)
        {
            x = MathF.Round(x);
            y = MathF.Round(y);
        }

        var wx = entity.X + x + shake.X;
        var wy = entity.Y + y - shake.Y;
        var p0 = cam.Apply(new Vector2(wx - w / 2, wy + h / 2));
        var p1 = cam.Apply(new Vector2(wx + w / 2, wy - h / 2));
        using var paint = new SKPaint { Color = new SKColor(col.RByte, col.GByte, col.BByte, col.AByte), IsAntialias = false };
        canvas.DrawRect(SKRect.Create(MathF.Round(MathF.Min(p0.X, p1.X)), MathF.Round(MathF.Min(p0.Y, p1.Y)),
            MathF.Round(MathF.Abs(p1.X - p0.X)), MathF.Round(MathF.Abs(p1.Y - p0.Y))), paint);
    }

    private void DrawBlood(SKCanvas canvas, ProcAnimScenePrototype proto, Func<int, Appearance> apps, float real, Vector2 shake, Affine cam, bool wield)
    {
        for (var i = 0; i < proto.Events.Count; i++)
        {
            var e = proto.Events[i];
            if (e.Blood is not { } bl)
                continue;
            var t0 = ProcAnimTimeline.RealTime(proto, e.Time);
            if (real < t0 || e.TargetIndex < 0 || e.TargetIndex >= proto.Actors.Count)
                continue;
            var role = e.TargetIndex;
            var m = ProcAnimTimeline.Evaluate(proto.Actors[role].Move, e.Time);
            var pose = ProcAnimTimeline.Solve(proto, role, e.Time, Puppets.Main(proto.Actors[role], apps(role), wield).Pivots, true,
                OtherPivots(proto, apps, wield));
            var o = ProcAnimTimeline.Point(pose, bl.From, bl.At);
            var col = bl.Color ?? BloodDefault;
            foreach (var d in ProcAnimTimeline.Droplets(bl, i, o, real - t0))
            {
                var c = new Color(MathF.Min(1, col.R * d.Shade), MathF.Min(1, col.G * d.Shade), MathF.Min(1, col.B * d.Shade), d.Alpha);
                Dot(canvas, cam, new Vector2(m.X * 32, m.Y * 32), MathF.Round(d.Position.X), MathF.Round(d.Position.Y), d.Size, d.Size, c, shake, false);
            }
        }
    }

    private void DrawFx(SKCanvas canvas, ProcAnimScenePrototype proto, Func<int, Appearance> apps, float at, float real, Vector2 shake, Affine cam, bool wield)
    {
        var blood = BloodDefault;
        var pivots = OtherPivots(proto, apps, wield);
        for (var i = 0; i < proto.Events.Count; i++)
        {
            var e = proto.Events[i];
            var role = e.TargetIndex;
            if (e.Effects.Count == 0 || role < 0 || role >= proto.Actors.Count)
                continue;
            var t0 = ProcAnimTimeline.RealTime(proto, e.Time);
            if (real < t0)
                continue;
            ProcAnimPose[]? pose = null;
            var m = ProcAnimTimeline.Evaluate(proto.Actors[role].Move, e.Time);
            for (var j = 0; j < e.Effects.Count; j++)
            {
                var fx = e.Effects[j];
                if (!ProcAnimFx.Anchored(fx.Type) || real - t0 > ProcAnimFx.Life(fx))
                    continue;
                pose ??= ProcAnimTimeline.Solve(proto, role, e.Time, pivots(role), true, pivots);
                foreach (var d in ProcAnimFx.Particles(fx, i * 16 + j, ProcAnimFx.Origin(fx, pose), real - t0, blood))
                    Dot(canvas, cam, new Vector2(m.X * 32, m.Y * 32), d.Position.X, d.Position.Y, d.Size.X, d.Size.Y, d.Color, shake, true);
            }
        }

        for (var role = 0; role < proto.Actors.Count; role++)
        {
            var tr = proto.Actors[role];
            var m = ProcAnimTimeline.Evaluate(tr.Move, at);
            var ent = new Vector2(m.X * 32, m.Y * 32);
            ProcAnimPose[]? pose = null;
            var ash = ProcAnimFx.Shake(proto, role, real);
            var fl = ProcAnimFx.ViewFlipScale(tr, at);
            for (var i = 0; i < proto.Events.Count; i++)
            {
                var e = proto.Events[i];
                if (e.Time > at)
                    break;
                if (e.TargetIndex != role)
                    continue;
                for (var j = 0; j < e.Effects.Count; j++)
                {
                    var fx = e.Effects[j];
                    if (fx.Type is not (ProcAnimEffectType.Bruise or ProcAnimEffectType.Drips))
                        continue;
                    if (ProcAnimTimeline.IsHidden(tr, fx.Part, at))
                        continue;
                    if (fx.Type == ProcAnimEffectType.Drips && at - e.Time > ProcAnimFx.Life(fx) + 2)
                        continue;
                    pose ??= ProcAnimTimeline.Solve(proto, role, at, pivots(role), true, pivots);
                    foreach (var d in ProcAnimFx.Particles(fx, i * 16 + j, ProcAnimFx.Origin(fx, pose), at - e.Time, blood))
                        Dot(canvas, cam, ent, d.Position.X, d.Position.Y, d.Size.X, d.Size.Y, d.Color, shake, true, ash, fl);
                }
            }

            // faces
            var expr = ProcAnimTimeline.ExpressionAt(tr, at);
            var cfg = tr.Face;
            if (cfg.Disabled || (expr.Face is null or "neutral" && expr.Eyes is null or "open") || ProcAnimTimeline.IsHidden(tr, ProcAnimBone.Head, at))
                continue;
            var hf = ProcAnimTimeline.FlipScale(tr, ProcAnimBone.Head, at);
            if (MathF.Abs(hf) < 0.4f)
                continue;
            var fs = (tr.Facing >= 0 ? 1f : -1f) * (hf > 0 ? 1f : -1f);
            var front = false;
            var v = ProcAnimTimeline.ViewInfoAt(tr, at);
            if (v is { Turned: true })
                fs = -fs;
            else if (v != null)
            {
                if (v.Facing != Direction.South)
                    continue;
                front = true;
            }

            pose ??= ProcAnimTimeline.Solve(proto, role, at, pivots(role), true, pivots);
            foreach (var d in ProcAnimFx.FaceDots(cfg, expr, pose, fs, front))
                Dot(canvas, cam, ent, d.Position.X, d.Position.Y, d.Size.X, d.Size.Y, d.Color, shake, false, ash, fl);
        }
    }

    private void DrawStars(SKCanvas canvas, ProcAnimScenePrototype proto, Func<int, Appearance> apps, float at, Vector2 shake, Affine cam, bool wield)
    {
        var pivots = OtherPivots(proto, apps, wield);
        for (var role = proto.Actors.Count - 1; role >= 0; role--)
        {
            var tr = proto.Actors[role];
            if (tr.Daze?.Stars is not { } st)
                continue;
            var stars = ProcAnimTimeline.Stars(tr, at);
            if (stars.Count == 0)
                continue;
            var m = ProcAnimTimeline.Evaluate(tr.Move, at);
            var pose = ProcAnimTimeline.Solve(proto, role, at, pivots(role), true, pivots);
            var c0 = ProcAnimTimeline.Point(pose, st.From, st.At);
            foreach (var s in stars)
            {
                var k = s.Front ? 1f : 0.7f;
                var col = new Color(st.Color.R * k, st.Color.G * k, st.Color.B * k, s.Alpha);
                var px = MathF.Round(m.X * 32 + c0.X + s.Position.X);
                var py = MathF.Round(m.Y * 32 + c0.Y + s.Position.Y);
                var cells = s.Size > 1 ? new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) } : new[] { (0, 0) };
                foreach (var (dx, dy) in cells)
                    Dot(canvas, cam, Vector2.Zero, px + dx, py + dy, 1, 1, col, shake, false);
            }
        }
    }

    public static Vector2 HitPixel(HitItem h, Vector2 screen)
    {
        var p = h.ImageToScreen.Inverse().Apply(screen);
        return p;
    }

    public HitItem? Pick(Vector2 screen, int? actor = null)
    {
        for (var i = Hits.Count - 1; i >= 0; i--)
        {
            var h = Hits[i];
            if (actor is { } a && h.Actor != a)
                continue;
            var p = HitPixel(h, screen);
            var x = (int) MathF.Floor(p.X);
            var y = (int) MathF.Floor(p.Y);
            if (x < 0 || y < 0 || x >= h.W || y >= h.H)
                continue;
            if (h.Pixels[(y * h.W + x) * 4 + 3] > 0)
                return h;
        }

        return null;
    }
}
