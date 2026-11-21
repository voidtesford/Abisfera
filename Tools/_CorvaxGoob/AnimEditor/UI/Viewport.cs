using System.Numerics;
using Abisfera.AnimEditor.Model;
using Abisfera.AnimEditor.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Content.Shared._CorvaxGoob.ProcAnim;
using SkiaSharp;
using Point = Avalonia.Point;
using RenderOptions = Abisfera.AnimEditor.Rendering.RenderOptions;
using AColor = Avalonia.Media.Color;

namespace Abisfera.AnimEditor.UI;

/// <summary>
/// The stage: both puppets drawn as in game (pixel art, nearest sampling) with the bone overlay; drag a part to rotate
/// it about its joint (or move it), drag the floor square to move the actor, right / middle drag to pan, wheel to zoom.
/// </summary>
public sealed class Viewport : Control
{
    private readonly EditorState _s;
    private WriteableBitmap? _bmp;
    private float _zoom = 4f;
    private Vector2 _center = new(40f, 6f);
    private double _scale = 1;

    private enum DragKind : byte { None, Pan, Rotate, Translate, Root, Ik }

    private DragKind _drag;
    private Vector2 _dragStart;
    private Vector2 _lastPos;
    private Vector2 _joint;
    private float _prevAngle;
    private float _accAngle;
    private KeyValue _startKey;
    private TrackRef _dragTrack;
    private float _frameRot;
    private bool _checkpointed;
    private HitItem? _hover;
    private Vector2 _centerAtDrag;

    private ProcAnimScenePrototype? _rawProto;
    private int _rawRevision = -1;

    public Viewport(EditorState state)
    {
        _s = state;
        Focusable = true;
        ClipToBounds = true;
        Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _s.DocChanged += InvalidateVisual;
        _s.TimeChanged += InvalidateVisual;
        _s.SelectionChanged += InvalidateVisual;
    }

    public float Zoom => _zoom;

    private Affine Cam(int w, int h)
    {
        var z = (float) (_zoom * _scale);
        return Affine.Translate(w / 2f, h / 2f) * Affine.Scale(z, -z) * Affine.Translate(-_center.X, -_center.Y);
    }

    public void ZoomBy(float factor, Vector2? at = null)
    {
        var old = _zoom;
        _zoom = Math.Clamp(_zoom * factor, 1f, 24f);
        if (at is { } p && _bmp != null)
        {
            // keep the world point under the cursor fixed
            var w = _bmp.PixelSize.Width;
            var h = _bmp.PixelSize.Height;
            var before = Cam(w, h);
            _zoom = old;
            var world = Cam(w, h).Inverse().Apply(p);
            _zoom = Math.Clamp(old * factor, 1f, 24f);
            var after = Cam(w, h).Apply(world);
            var z = (float) (_zoom * _scale);
            _center += new Vector2((after.X - p.X) / z, -(after.Y - p.Y) / z);
            _ = before;
        }

        InvalidateVisual();
    }

    /// <summary>Frames both actors over the whole animation.</summary>
    public void Fit()
    {
        if (_s.Doc is not { } doc)
            return;
        if (Bounds.Width < 50 || Bounds.Height < 50)
        {
            _needFit = true;
            return;
        }

        _needFit = false;
        float minX = 0, maxX = 32, minY = -16, maxY = 16;
        for (var a = 0; a < doc.Proto.Actors.Count; a++)
        {
            for (var t = _s.Start; t <= _s.End; t += 0.25f)
            {
                var m = ProcAnimTimeline.Evaluate(doc.Proto.Actors[a].Move, t);
                minX = MathF.Min(minX, m.X * 32 - 20);
                maxX = MathF.Max(maxX, m.X * 32 + 20);
                minY = MathF.Min(minY, m.Y * 32 - 20);
                maxY = MathF.Max(maxY, m.Y * 32 + 24);
            }
        }

        _center = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        var w = (float) Bounds.Width;
        var h = (float) Bounds.Height;
        _zoom = MathF.Max(1f, MathF.Floor(MathF.Min(w / (maxX - minX), h / (maxY - minY)) * 0.9f));
        InvalidateVisual();
    }

    private ProcAnimScenePrototype ProtoForView(SceneDocument doc)
    {
        if (_s.Settings.Procedural)
            return doc.Proto;
        if (_rawRevision != doc.Revision || _rawProto == null)
        {
            _rawProto = SceneRenderer.WithoutProcedural(new NodeMapper().Read<ProcAnimScenePrototype>(doc.Node));
            _rawRevision = doc.Revision;
        }

        return _rawProto;
    }

    private bool _needFit;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_needFit)
            Fit();
    }

    public override void Render(DrawingContext context)
    {
        _scale = VisualRoot?.RenderScaling ?? 1.0;
        var pw = Math.Max(1, (int) (Bounds.Width * _scale));
        var ph = Math.Max(1, (int) (Bounds.Height * _scale));
        if (_bmp == null || _bmp.PixelSize.Width != pw || _bmp.PixelSize.Height != ph)
        {
            _bmp?.Dispose();
            _bmp = new WriteableBitmap(new PixelSize(pw, ph), new Avalonia.Vector(96 * _scale, 96 * _scale), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        using (var fb = _bmp.Lock())
        {
            var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
            DrawScene(surface.Canvas, pw, ph);
            surface.Canvas.Flush();
        }

        context.DrawImage(_bmp, new Rect(0, 0, pw, ph), new Rect(Bounds.Size));
        DrawHud(context);
    }

    private void DrawGrid(SKCanvas c, Affine cam, int w, int h)
    {
        c.Clear(new SKColor(46, 48, 54));
        var inv = cam.Inverse();
        var p0 = inv.Apply(new Vector2(0, h));
        var p1 = inv.Apply(new Vector2(w, 0));
        using var floor = new SKPaint { Color = new SKColor(56, 58, 66) };
        using var line = new SKPaint { Color = new SKColor(70, 72, 82), StrokeWidth = 1 };
        var tx0 = (int) MathF.Floor(p0.X / 32f) - 1;
        var tx1 = (int) MathF.Ceiling(p1.X / 32f) + 1;
        var ty0 = (int) MathF.Floor(p0.Y / 32f) - 1;
        var ty1 = (int) MathF.Ceiling(p1.Y / 32f) + 1;
        for (var tx = tx0; tx <= tx1; tx++)
        for (var ty = ty0; ty <= ty1; ty++)
        {
            if (((tx + ty) & 1) != 0)
                continue;
            var a = cam.Apply(new Vector2((tx - 0.5f) * 32, (ty + 0.5f) * 32));
            var b = cam.Apply(new Vector2((tx + 0.5f) * 32, (ty - 0.5f) * 32));
            c.DrawRect(SKRect.Create(a.X, a.Y, b.X - a.X, b.Y - a.Y), floor);
        }

        for (var tx = tx0; tx <= tx1; tx++)
        {
            var x = cam.Apply(new Vector2((tx + 0.5f) * 32, 0)).X;
            c.DrawLine(x, 0, x, h, line);
        }

        for (var ty = ty0; ty <= ty1; ty++)
        {
            var y = cam.Apply(new Vector2(0, (ty + 0.5f) * 32)).Y;
            c.DrawLine(0, y, w, y, line);
        }
    }

    private void DrawScene(SKCanvas c, int w, int h)
    {
        var cam = Cam(w, h);
        DrawGrid(c, cam, w, h);
        if (_s.Doc is not { } doc || doc.Proto.Actors.Count == 0)
            return;
        var proto = ProtoForView(doc);
        var renderer = _s.Renderer;

        try
        {
            if (_s.Settings.Onion && !_s.Playing)
            {
                var frames = _s.Frames();
                var edited = _s.EditedActors;
                Func<int, bool> onionActors = i => edited.Contains(i);
                var prev = frames.LastOrDefault(f => f < _s.Time - 0.004f, float.NaN);
                var next = frames.FirstOrDefault(f => f > _s.Time + 0.004f, float.NaN);
                if (!float.IsNaN(prev))
                    renderer.Render(c, proto, _s.App, prev, ProcAnimTimeline.RealTime(proto, prev), cam,
                        new RenderOptions { Alpha = 0.32f, Tint = new SKColor(255, 90, 90), CollectHits = false, Effects = false, Flash = false, Actors = onionActors });
                if (!float.IsNaN(next))
                    renderer.Render(c, proto, _s.App, next, ProcAnimTimeline.RealTime(proto, next), cam,
                        new RenderOptions { Alpha = 0.32f, Tint = new SKColor(90, 220, 120), CollectHits = false, Effects = false, Flash = false, Actors = onionActors });
            }

            renderer.Render(c, proto, _s.App, _s.Time, _s.Real, cam,
                new RenderOptions { Effects = _s.Settings.Effects, Flash = _s.Settings.Effects });
        }
        catch (Exception e)
        {
            _s.SetStatus("Ошибка отрисовки: " + e.Message);
            return;
        }

        if (_s.Playing)
            return;

        // highlight: hovered part (faint) and the selected part
        using (var hl = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(255, 220, 60, 110), SKBlendMode.SrcIn) })
        using (var hv = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(255, 255, 255, 60), SKBlendMode.SrcIn) })
        {
            foreach (var hit in renderer.Hits)
            {
                if (_s.Selected is { } sel && Matches(hit, sel))
                    SceneRenderer.DrawImage(c, hit.Image, hit.ImageToScreen, hl);
                else if (_hover != null && hit.Actor == _hover.Actor && hit.Bone == _hover.Bone && hit.Prop == _hover.Prop)
                    SceneRenderer.DrawImage(c, hit.Image, hit.ImageToScreen, hv);
            }
        }

        if (_s.Settings.Bones)
            foreach (var frame in renderer.Frames)
                if (_s.EditedActors.Contains(frame.Actor))
                    DrawBones(c, doc, proto, frame);
    }

    private static bool Matches(HitItem hit, TrackRef sel) => sel.Kind switch
    {
        TrackKind.Part => hit.Actor == sel.Actor && hit.Prop < 0 && hit.Bone == sel.Bone,
        TrackKind.Prop => hit.Actor == sel.Actor && hit.Prop == sel.Index,
        _ => false,
    };

    public static readonly ProcAnimBone[] EditableBones =
    {
        ProcAnimBone.Body, ProcAnimBone.Head, ProcAnimBone.ArmNear, ProcAnimBone.ArmNearLow, ProcAnimBone.ArmFar, ProcAnimBone.ArmFarLow,
        ProcAnimBone.LegNear, ProcAnimBone.LegNearLow, ProcAnimBone.LegFar, ProcAnimBone.LegFarLow, ProcAnimBone.HeldNear, ProcAnimBone.HeldFar,
    };

    public static ProcAnimBone? ParentOf(ProcAnimBone b) => b switch
    {
        ProcAnimBone.ArmNearLow => ProcAnimBone.ArmNear,
        ProcAnimBone.ArmFarLow => ProcAnimBone.ArmFar,
        ProcAnimBone.LegNearLow => ProcAnimBone.LegNear,
        ProcAnimBone.LegFarLow => ProcAnimBone.LegFar,
        ProcAnimBone.ArmNearHand => ProcAnimBone.ArmNearLow,
        ProcAnimBone.ArmFarHand => ProcAnimBone.ArmFarLow,
        ProcAnimBone.LegNearFoot => ProcAnimBone.LegNearLow,
        ProcAnimBone.LegFarFoot => ProcAnimBone.LegFarLow,
        ProcAnimBone.HeldNear => ProcAnimBone.ArmNearHand,
        ProcAnimBone.HeldFar => ProcAnimBone.ArmFarHand,
        _ => null,
    };

    public static Vector2 PivotOf(ProcAnimActor tr, ActorFrame frame, ProcAnimBone b)
    {
        if (b != ProcAnimBone.Body && frame.Pivots.TryGetValue(b, out var pv))
            return pv;
        return tr.Parts.TryGetValue(b, out var track) ? track.Pivot : Vector2.Zero;
    }

    /// <summary>Joint of a bone in the actor's frame (px from the entity, y up).</summary>
    public static Vector2 JointLocal(ProcAnimActor tr, ActorFrame frame, ProcAnimBone b)
        => ProcAnimTimeline.Point(frame.Pose, b, PivotOf(tr, frame, b));

    private static Vector2 RootLocal => new(0f, -15f);

    /// <summary>World rotation of the frame a bone's translation keys live in.</summary>
    private static float FrameRot(ActorFrame frame, ProcAnimBone b)
    {
        if (b == ProcAnimBone.Body)
            return 0f;
        var body = frame.Pose[(int) ProcAnimBone.Body].Rot;
        return ParentOf(b) is { } p ? body + frame.Pose[(int) p].Rot : body;
    }

    private ProcAnimIkTrack? ActiveIk(ProcAnimActor tr, ProcAnimBone bone, float t, out int index)
    {
        index = -1;
        var limb = bone switch
        {
            ProcAnimBone.ArmNearLow or ProcAnimBone.ArmNearHand or ProcAnimBone.HeldNear => ProcAnimBone.ArmNear,
            ProcAnimBone.ArmFarLow or ProcAnimBone.ArmFarHand or ProcAnimBone.HeldFar => ProcAnimBone.ArmFar,
            ProcAnimBone.LegNearLow or ProcAnimBone.LegNearFoot => ProcAnimBone.LegNear,
            ProcAnimBone.LegFarLow or ProcAnimBone.LegFarFoot => ProcAnimBone.LegFar,
            _ => bone,
        };
        for (var i = 0; i < tr.Ik.Count; i++)
            if (tr.Ik[i].Limb == limb && tr.Ik[i].Valid && ProcAnimTimeline.IkWeight(tr.Ik[i], t) > 0.01f)
            {
                index = i;
                return tr.Ik[i];
            }

        return null;
    }

    private Vector2? IkTargetScreen(ProcAnimScenePrototype proto, ActorFrame frame, int ikIndex)
    {
        var tr = proto.Actors[frame.Actor];
        if (ikIndex < 0 || ikIndex >= tr.Ik.Count)
            return null;
        var pivots = new Func<int, IReadOnlyDictionary<ProcAnimBone, Vector2>?>(j => _s.Renderer.Frames.FirstOrDefault(f => f.Actor == j)?.Pivots);
        var target = ProcAnimTimeline.IkTarget(proto, frame.Actor, tr.Ik[ikIndex], _s.Time, frame.Pose, pivots);
        return frame.ToScreen(target);
    }

    private void DrawBones(SKCanvas c, SceneDocument doc, ProcAnimScenePrototype proto, ActorFrame frame)
    {
        var tr = proto.Actors[frame.Actor];
        var z = (float) _scale;
        using var line = new SKPaint { Color = new SKColor(255, 255, 255, 120), StrokeWidth = 1.5f * z, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var dot = new SKPaint { Color = new SKColor(255, 255, 255, 200), IsAntialias = true };
        using var sel = new SKPaint { Color = new SKColor(255, 210, 40), IsAntialias = true };
        using var ikp = new SKPaint { Color = new SKColor(80, 220, 255), IsAntialias = true, StrokeWidth = 1.5f * z, Style = SKPaintStyle.Stroke };
        using var ring = new SKPaint { Color = new SKColor(0, 0, 0, 160), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f * z };
        foreach (var b in EditableBones)
        {
            if (ProcAnimTimeline.IsHidden(tr, b, _s.Time))
                continue;
            if (b is ProcAnimBone.HeldNear or ProcAnimBone.HeldFar && !tr.Parts.ContainsKey(b))
                continue;
            var j = frame.ToScreen(JointLocal(tr, frame, b));
            if (ParentOf(b) is { } p)
            {
                var pp = p is ProcAnimBone.ArmNearHand or ProcAnimBone.ArmFarHand ? ParentOf(p)!.Value : p;
                var pj = frame.ToScreen(JointLocal(tr, frame, pp));
                c.DrawLine(pj.X, pj.Y, j.X, j.Y, line);
            }
            else if (b != ProcAnimBone.Body)
            {
                var bj = frame.ToScreen(JointLocal(tr, frame, ProcAnimBone.Body));
                c.DrawLine(bj.X, bj.Y, j.X, j.Y, line);
            }

            var selected = _s.Selected is { Kind: TrackKind.Part } s && s.Actor == frame.Actor && s.Bone == b;
            var r = (selected ? 4.5f : 3f) * z;
            c.DrawCircle(j.X, j.Y, r, selected ? sel : dot);
            c.DrawCircle(j.X, j.Y, r, ring);
        }

        // root (floor square): drags the actor's `move`
        var root = frame.ToScreen(RootLocal);
        var rootSel = _s.Selected is { Kind: TrackKind.Move } rs && rs.Actor == frame.Actor;
        using (var rp = new SKPaint { Color = rootSel ? new SKColor(255, 210, 40) : new SKColor(255, 140, 60, 220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * z })
            c.DrawRect(SKRect.Create(root.X - 6 * z, root.Y - 3 * z, 12 * z, 6 * z), rp);

        // IK target of the selected limb
        if (_s.Selected is { } st && st.Actor == frame.Actor)
        {
            var ikIndex = st.Kind == TrackKind.Ik ? st.Index : -1;
            if (ikIndex < 0 && st.Kind == TrackKind.Part)
                ActiveIk(tr, st.Bone, _s.Time, out ikIndex);
            if (ikIndex >= 0 && IkTargetScreen(proto, frame, ikIndex) is { } ik)
            {
                c.DrawLine(ik.X - 6 * z, ik.Y, ik.X + 6 * z, ik.Y, ikp);
                c.DrawLine(ik.X, ik.Y - 6 * z, ik.X, ik.Y + 6 * z, ikp);
                c.DrawCircle(ik.X, ik.Y, 4 * z, ikp);
            }
        }
    }

    private void DrawHud(DrawingContext ctx)
    {
        if (_s.Doc is not { } doc)
        {
            var ft0 = new FormattedText("Выберите анимацию в списке слева (или Файл → Новая анимация добивания)",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 16, Brushes.LightGray);
            ctx.DrawText(ft0, new Point(20, 20));
            return;
        }

        var beat = "";
        foreach (var e in doc.Proto.Events)
            if (e.Beat != null && e.Time <= _s.Time + 1e-4f)
                beat = e.Beat;
        var frame = _s.FrameAtPlayhead();
        var sel = _s.Selected is { } s ? _s.TrackName(s) : "ничего";
        var tool = _s.Tool == Tool.Rotate ? "вращение (R)" : "смещение (G)";
        var text = $"{doc.Id}{(doc.Dirty ? " *" : "")}   t = {_s.Time:0.000} с{(frame != null ? "  [кадр]" : "")}   {beat}\n" +
                   $"Выбрано: {sel}   Инструмент: {tool}   Масштаб: ×{_zoom:0.#}";
        var hint = HintFor(doc);
        if (hint != null)
            text += "\n" + hint;
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), 13, new SolidColorBrush(AColor.FromArgb(230, 235, 235, 235)));
        ctx.FillRectangle(new SolidColorBrush(AColor.FromArgb(120, 0, 0, 0)), new Rect(6, 6, ft.Width + 12, ft.Height + 8), 4);
        ctx.DrawText(ft, new Point(12, 10));
    }

    private string? HintFor(SceneDocument doc)
    {
        if (_s.Selected is not { Kind: TrackKind.Part } sel || sel.Actor >= doc.Proto.Actors.Count)
            return null;
        var tr = doc.Proto.Actors[sel.Actor];
        if (ActiveIk(tr, sel.Bone, _s.Time, out _) is { } ik)
            return $"Эту конечность ведёт IK (вес {ProcAnimTimeline.IkWeight(ik, _s.Time):0.00}): тяните голубой крестик, чтобы сдвинуть цель.";
        if (ProcAnimTimeline.ViewAt(tr, _s.Time) is { } v)
            return $"Здесь актёр нарисован с направлением {v}: ограничения суставов в игре не действуют.";
        return null;
    }

    // ---------------- input ----------------

    private Vector2 Dev(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Vector2((float) (p.X * _scale), (float) (p.Y * _scale));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        var pos = Dev(e);
        _dragStart = pos;
        _lastPos = pos;
        _checkpointed = false;
        e.Pointer.Capture(this);
        if (props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            _drag = DragKind.Pan;
            _centerAtDrag = _center;
            return;
        }

        if (!props.IsLeftButtonPressed || _s.Doc is not { } doc || _s.Playing)
            return;

        var alt = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var tool = alt ? (_s.Tool == Tool.Rotate ? Tool.Translate : Tool.Rotate) : _s.Tool;

        // handles first: IK cross, root square, joints
        foreach (var frame in _s.Renderer.Frames)
        {
            if (!_s.EditedActors.Contains(frame.Actor) || frame.Actor >= doc.Proto.Actors.Count)
                continue;
            var tr = doc.Proto.Actors[frame.Actor];
            if (_s.Selected is { } st && st.Actor == frame.Actor)
            {
                var ikIndex = st.Kind == TrackKind.Ik ? st.Index : -1;
                if (ikIndex < 0 && st.Kind == TrackKind.Part)
                    ActiveIk(tr, st.Bone, _s.Time, out ikIndex);
                if (ikIndex >= 0 && IkTargetScreen(doc.Proto, frame, ikIndex) is { } ik && Vector2.Distance(ik, pos) < 8 * _scale)
                {
                    BeginKeyDrag(DragKind.Ik, new TrackRef(frame.Actor, TrackKind.Ik, tr.Ik[ikIndex].Limb, ikIndex), frame, pos);
                    return;
                }
            }

            var root = frame.ToScreen(RootLocal);
            if (MathF.Abs(root.X - pos.X) < 8 * _scale && MathF.Abs(root.Y - pos.Y) < 6 * _scale)
            {
                _s.Select(new TrackRef(frame.Actor, TrackKind.Move));
                BeginKeyDrag(DragKind.Root, new TrackRef(frame.Actor, TrackKind.Move), frame, pos);
                return;
            }

            if (_s.Settings.Bones)
            {
                foreach (var b in EditableBones.Reverse())
                {
                    if (ProcAnimTimeline.IsHidden(tr, b, _s.Time) || b is ProcAnimBone.HeldNear or ProcAnimBone.HeldFar && !tr.Parts.ContainsKey(b))
                        continue;
                    var j = frame.ToScreen(JointLocal(tr, frame, b));
                    if (Vector2.Distance(j, pos) < 5 * _scale)
                    {
                        var track = new TrackRef(frame.Actor, TrackKind.Part, b);
                        _s.Select(track);
                        BeginKeyDrag(tool == Tool.Rotate ? DragKind.Rotate : DragKind.Translate, track, frame, pos);
                        return;
                    }
                }
            }
        }

        var hit = _s.Renderer.Pick(pos);
        if (hit == null)
        {
            _s.Select(null);
            _drag = DragKind.None;
            return;
        }

        var tref = hit.Prop >= 0
            ? new TrackRef(hit.Actor, TrackKind.Prop, hit.Bone, hit.Prop)
            : new TrackRef(hit.Actor, TrackKind.Part, hit.Bone);
        if (_s.EditActor >= 0 && _s.EditActor != hit.Actor)
            _s.SetEditActor(hit.Actor);
        _s.Select(tref);
        var fr = _s.Renderer.Frames.FirstOrDefault(f => f.Actor == hit.Actor);
        if (fr != null)
            BeginKeyDrag(tool == Tool.Rotate ? DragKind.Rotate : DragKind.Translate, tref, fr, pos);
    }

    private void BeginKeyDrag(DragKind kind, TrackRef track, ActorFrame frame, Vector2 pos)
    {
        var doc = _s.Doc!;
        var tr = doc.Proto.Actors[track.Actor];
        _drag = kind;
        _dragTrack = track;
        _startKey = doc.KeyedValue(track, _s.Time);
        _frameRot = 0f;
        if (track.Kind == TrackKind.Prop && track.Index < tr.Props.Count)
        {
            var pr = tr.Props[track.Index];
            var basePose = pr.Part == ProcAnimBone.Body ? default : frame.Pose[(int) pr.Part];
            var pp = ProcAnimTimeline.PropPose(pr, basePose, _s.Time);
            _joint = frame.BodyToScreen.Apply(new Vector2(pp.X, pp.Y));
            _frameRot = frame.Pose[(int) ProcAnimBone.Body].Rot + basePose.Rot;
        }
        else if (track.Kind == TrackKind.Part)
        {
            _joint = frame.ToScreen(JointLocal(tr, frame, track.Bone));
            _frameRot = FrameRot(frame, track.Bone);
        }

        _prevAngle = Angle(pos);
        _accAngle = 0f;
        if (kind == DragKind.Rotate && !track.HasRot)
            _drag = DragKind.Translate;
    }

    private float Angle(Vector2 p) => MathF.Atan2(-(p.Y - _joint.Y), p.X - _joint.X) * 180f / MathF.PI;

    private static float Wrap(float d)
    {
        while (d > 180f)
            d -= 360f;
        while (d <= -180f)
            d += 360f;
        return d;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = Dev(e);
        var doc = _s.Doc;
        switch (_drag)
        {
            case DragKind.None:
                var h = _s.Playing ? null : _s.Renderer.Pick(pos);
                if (h?.Actor != _hover?.Actor || h?.Bone != _hover?.Bone || h?.Prop != _hover?.Prop)
                {
                    _hover = h;
                    Cursor = h != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                    InvalidateVisual();
                }

                return;
            case DragKind.Pan:
            {
                var z = (float) (_zoom * _scale);
                var d = pos - _dragStart;
                _center = _centerAtDrag + new Vector2(-d.X / z, d.Y / z);
                InvalidateVisual();
                return;
            }
        }

        if (doc == null || Vector2.Distance(pos, _dragStart) < 2 && !_checkpointed)
            return;
        if (!_checkpointed)
        {
            doc.Checkpoint();
            _checkpointed = true;
        }

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var track = _dragTrack;
        var t = _s.Time;
        var zz = (float) (_zoom * _scale);
        var world = new Vector2((pos.X - _dragStart.X) / zz, -(pos.Y - _dragStart.Y) / zz);
        switch (_drag)
        {
            case DragKind.Rotate:
            {
                var a = Angle(pos);
                _accAngle += Wrap(a - _prevAngle);
                _prevAngle = a;
                var rot = _startKey.Rot + _accAngle;
                rot = ctrl ? MathF.Round(rot / 15f) * 15f : MathF.Round(rot);
                var tr = doc.Proto.Actors[track.Actor];
                if (_s.Settings.Limits && track.Kind == TrackKind.Part && ProcAnimTimeline.ViewAt(tr, t) == null
                    && EditorState.Limit(track.Bone, tr.Joints) is { } lim)
                {
                    var f = tr.Facing;
                    rot = f * Math.Clamp(f * rot, lim.Min, lim.Max);
                }

                doc.EditLive(() => doc.WriteKey(track, t, v => v with { Rot = rot }));
                break;
            }
            case DragKind.Translate:
            {
                var local = ProcAnimTimeline.Rotate(world, -_frameRot);
                var x = _startKey.X + local.X;
                var y = _startKey.Y + local.Y;
                if (!ctrl)
                {
                    x = MathF.Round(x);
                    y = MathF.Round(y);
                }

                doc.EditLive(() => doc.WriteKey(track, t, v => v with { X = x, Y = y }));
                break;
            }
            case DragKind.Root:
            {
                var x = _startKey.X + world.X / 32f;
                var y = _startKey.Y + world.Y / 32f;
                if (!ctrl)
                {
                    x = MathF.Round(x * 32f) / 32f;
                    y = MathF.Round(y * 32f) / 32f;
                }

                doc.EditLive(() => doc.WriteKey(track, t, v => v with { X = x, Y = y }));
                break;
            }
            case DragKind.Ik:
            {
                var x = MathF.Round(_startKey.X + world.X);
                var y = MathF.Round(_startKey.Y + world.Y);
                doc.EditLive(() => doc.WriteKey(track, t, v => v with { X = x, Y = y }));
                break;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_drag is DragKind.Rotate or DragKind.Translate or DragKind.Root or DragKind.Ik && _checkpointed)
            _s.SetStatus($"Ключ записан: {_s.TrackName(_dragTrack)} @ {_s.Time:0.###} с");
        _drag = DragKind.None;
    }

    /// <summary>Esc during a drag: puts the pose back.</summary>
    public bool CancelDrag()
    {
        if (_drag == DragKind.None)
            return false;
        if (_checkpointed)
            _s.Doc?.Undo();
        _drag = DragKind.None;
        return true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomBy(e.Delta.Y > 0 ? 1.25f : 0.8f, Dev(e));
        e.Handled = true;
    }

    /// <summary>The current view as a PNG (what the viewport shows, overlay included).</summary>
    public void SavePng(string path)
    {
        if (_bmp == null)
            return;
        _bmp.Save(path);
    }
}
