using Abisfera.AnimEditor.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Content.Shared._CorvaxGoob.ProcAnim;
using AColor = Avalonia.Media.Color;
using Point = Avalonia.Point;

namespace Abisfera.AnimEditor.UI;

/// <summary>
/// Dope sheet: time ruler with beats / hit-stops, the frame row (keys of the edited actors merged) and one row per track.
/// Click to scrub, drag a diamond to retime it, right-click for frame / key actions, Ctrl+wheel to zoom.
/// </summary>
public sealed class TimelineView : Control
{
    private const double NameW = 190;
    private const double RulerH = 26;
    private const double FrameH = 24;
    private const double RowH = 18;

    private readonly EditorState _s;
    private readonly EditorCommands _cmd;
    private double _pxPerSec = 200;
    private double _viewStart = -0.05;
    private double _scrollY;
    private bool _userZoom;

    private enum DragKind : byte { None, Scrub, Frame, Key }

    private DragKind _drag;
    private float _dragFrom;
    private float _dragLo, _dragHi;
    private TrackRef _dragTrack;
    private bool _checkpointed;
    private double _pressX;

    private sealed record Row(string Name, TrackRef? Track, int Actor, bool Header);

    private readonly List<Row> _rows = new();

    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly IBrush Bg = new SolidColorBrush(AColor.FromRgb(32, 34, 39));
    private static readonly IBrush NameBg = new SolidColorBrush(AColor.FromRgb(40, 42, 48));
    private static readonly IBrush RowAlt = new SolidColorBrush(AColor.FromRgb(36, 38, 44));
    private static readonly IBrush SelRow = new SolidColorBrush(AColor.FromArgb(70, 255, 200, 60));
    private static readonly IBrush Text = new SolidColorBrush(AColor.FromRgb(215, 215, 220));
    private static readonly IBrush Dim = new SolidColorBrush(AColor.FromRgb(140, 142, 150));
    private static readonly IBrush KeyFill = new SolidColorBrush(AColor.FromRgb(230, 230, 235));
    private static readonly IBrush FrameFill = new SolidColorBrush(AColor.FromRgb(255, 190, 70));
    private static readonly IBrush SelFill = new SolidColorBrush(AColor.FromRgb(255, 230, 80));
    private static readonly IBrush HitStop = new SolidColorBrush(AColor.FromArgb(90, 255, 70, 70));
    private static readonly IBrush Intro = new SolidColorBrush(AColor.FromArgb(40, 120, 160, 255));
    private static readonly IPen Grid = new Pen(new SolidColorBrush(AColor.FromRgb(55, 57, 64)), 1);
    private static readonly IPen GridMajor = new Pen(new SolidColorBrush(AColor.FromRgb(75, 77, 86)), 1);
    private static readonly IPen Playhead = new Pen(new SolidColorBrush(AColor.FromRgb(255, 80, 80)), 2);
    private static readonly IPen BeatPen = new Pen(new SolidColorBrush(AColor.FromArgb(160, 120, 200, 255)), 1, new DashStyle(new double[] { 3, 3 }, 0));
    private static readonly IPen KeyEdge = new Pen(Brushes.Black, 1);

    public TimelineView(EditorState state, EditorCommands cmd)
    {
        _s = state;
        _cmd = cmd;
        Focusable = true;
        ClipToBounds = true;
        _s.DocChanged += InvalidateVisual;
        _s.TimeChanged += OnTime;
        _s.SelectionChanged += InvalidateVisual;
    }

    private void OnTime()
    {
        // keep the playhead visible while playing
        var x = X(_s.Time);
        if (_s.Playing && (x > Bounds.Width - 20 || x < NameW))
            _viewStart = _s.Time - 0.1;
        InvalidateVisual();
    }

    public void FitTime()
    {
        _userZoom = false;
        InvalidateVisual();
    }

    private double X(double t) => NameW + (t - _viewStart) * _pxPerSec;
    private double T(double x) => (x - NameW) / _pxPerSec + _viewStart;

    private void BuildRows()
    {
        _rows.Clear();
        if (_s.Doc is not { } doc)
            return;
        foreach (var a in _s.EditedActors)
        {
            var tr = doc.Proto.Actors[a];
            _rows.Add(new Row($"Кукла {a + 1}: {tr.Role}", null, a, true));
            foreach (var track in doc.Tracks(a))
                _rows.Add(new Row(_s.TrackName(track), track, a, false));
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
        if (_s.Doc is not { } doc)
            return;
        if (!_userZoom)
        {
            _viewStart = _s.Start - 0.05;
            _pxPerSec = Math.Max(20, (w - NameW - 30) / Math.Max(0.1, _s.End - _s.Start + 0.1));
        }

        BuildRows();

        // intro (negative time) and hit-stops
        if (_s.Start < 0)
            ctx.FillRectangle(Intro, new Rect(X(_s.Start), 0, X(0) - X(_s.Start), h));
        foreach (var e in doc.Proto.Events)
            if (e.HitStop > 0)
                ctx.FillRectangle(HitStop, new Rect(X(e.Time) - 1.5, RulerH, 3, h - RulerH));
        // past the end
        ctx.FillRectangle(new SolidColorBrush(AColor.FromArgb(90, 0, 0, 0)), new Rect(X(_s.End), 0, Math.Max(0, w - X(_s.End)), h));

        // ruler
        var step = NiceStep(60 / _pxPerSec);
        for (var t = Math.Floor(T(NameW) / step) * step; X(t) < w; t += step)
        {
            var x = X(t);
            if (x < NameW)
                continue;
            var major = Math.Abs(t / (step * 5) - Math.Round(t / (step * 5))) < 1e-6;
            ctx.DrawLine(major ? GridMajor : Grid, new Point(x, RulerH - (major ? 10 : 5)), new Point(x, h));
            if (major || step >= 0.1)
                DrawText(ctx, $"{t:0.##}", new Point(x + 2, 2), 11, Dim);
        }

        // beats
        foreach (var e in doc.Proto.Events)
        {
            if (e.Beat == null)
                continue;
            var x = X(e.Time);
            ctx.DrawLine(BeatPen, new Point(x, 12), new Point(x, h));
            DrawText(ctx, e.Beat, new Point(x + 2, 12), 10, new SolidColorBrush(AColor.FromRgb(140, 200, 255)));
        }

        // frame row
        var frames = _s.Frames();
        var y0 = RulerH;
        ctx.FillRectangle(NameBg, new Rect(0, y0, NameW, FrameH));
        DrawText(ctx, "Кадры (все дорожки)", new Point(8, y0 + 4), 12, Text);
        var cur = _s.FrameAtPlayhead();
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            var x = X(f);
            if (i + 1 < frames.Count)
            {
                var d = frames[i + 1] - f;
                var mid = (x + X(frames[i + 1])) / 2;
                if (X(frames[i + 1]) - x > 34)
                    DrawText(ctx, $"{d:0.##}", new Point(mid - 10, y0 + 5), 10, Dim);
            }

            Diamond(ctx, x, y0 + FrameH / 2, 6, cur is { } c && MathF.Abs(c - f) < 0.004f ? SelFill : FrameFill);
        }

        // track rows
        var top = RulerH + FrameH;
        using (ctx.PushClip(new Rect(0, top, w, h - top)))
        {
            for (var r = 0; r < _rows.Count; r++)
            {
                var y = top + r * RowH - _scrollY;
                if (y + RowH < top || y > h)
                    continue;
                var row = _rows[r];
                if (row.Header)
                {
                    ctx.FillRectangle(new SolidColorBrush(AColor.FromRgb(50, 54, 64)), new Rect(0, y, w, RowH));
                    DrawText(ctx, row.Name, new Point(6, y + 2), 12, Text);
                    continue;
                }

                var selected = row.Track is { } tr0 && _s.Selected == tr0;
                ctx.FillRectangle(selected ? SelRow : r % 2 == 0 ? RowAlt : Bg, new Rect(0, y, w, RowH));
                ctx.FillRectangle(NameBg, new Rect(0, y, NameW, RowH));
                if (selected)
                    ctx.FillRectangle(SelRow, new Rect(0, y, NameW, RowH));
                DrawText(ctx, row.Name, new Point(16, y + 2), 11, selected ? Text : Dim);
                var keys = doc.ProtoKeys(row.Track!.Value);
                if (keys == null)
                    continue;
                foreach (var k in keys)
                {
                    var atHead = MathF.Abs(k.Time - _s.Time) < 0.004f;
                    var fill = selected && atHead ? SelFill : KeyFill;
                    var x = X(k.Time);
                    if (k.Ease is ProcAnimEase.Step)
                        ctx.FillRectangle(fill, new Rect(x - 4, y + 5, 8, 8));
                    else if (k.Ease is ProcAnimEase.Linear)
                        ctx.DrawEllipse(fill, KeyEdge, new Point(x, y + RowH / 2), 4, 4);
                    else
                        Diamond(ctx, x, y + RowH / 2, 4.5, fill);
                }
            }
        }

        // playhead
        var px = X(_s.Time);
        ctx.DrawLine(Playhead, new Point(px, 0), new Point(px, h));
        ctx.DrawLine(GridMajor, new Point(NameW, 0), new Point(NameW, h));
        ctx.FillRectangle(NameBg, new Rect(0, 0, NameW, RulerH));
        DrawText(ctx, $"{_s.Time:0.000} / {_s.End:0.###} с", new Point(8, 5), 12, Text);
    }

    private static double NiceStep(double min)
    {
        foreach (var s in new[] { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5 })
            if (s >= min)
                return s;
        return 10;
    }

    private static void Diamond(DrawingContext ctx, double x, double y, double r, IBrush fill)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y - r), true);
            c.LineTo(new Point(x + r, y));
            c.LineTo(new Point(x, y + r));
            c.LineTo(new Point(x - r, y));
            c.EndFigure(true);
        }

        ctx.DrawGeometry(fill, KeyEdge, g);
    }

    private static void DrawText(DrawingContext ctx, string text, Point at, double size, IBrush brush)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush);
        ctx.DrawText(ft, at);
    }

    // ---------------- input ----------------

    private (Row? Row, float? Key) HitTest(Point p)
    {
        if (_s.Doc is not { } doc)
            return (null, null);
        if (p.Y >= RulerH && p.Y < RulerH + FrameH)
        {
            foreach (var f in _s.Frames())
                if (Math.Abs(X(f) - p.X) < 6)
                    return (null, f);
            return (null, null);
        }

        var r = (int) Math.Floor((p.Y - RulerH - FrameH + _scrollY) / RowH);
        if (p.Y < RulerH + FrameH || r < 0 || r >= _rows.Count)
            return (null, null);
        var row = _rows[r];
        if (row.Track is not { } tr)
            return (row, null);
        foreach (var k in doc.ProtoKeys(tr) ?? new())
            if (Math.Abs(X(k.Time) - p.X) < 5)
                return (row, k.Time);
        return (row, null);
    }

    private float SnapTime(double t, KeyModifiers mods)
    {
        var res = (mods & KeyModifiers.Control) != 0 ? 0.001 : 0.01;
        var v = (float) (Math.Round(t / res) * res);
        if ((mods & KeyModifiers.Control) == 0)
        {
            foreach (var f in _s.Frames())
                if (Math.Abs(X(f) - X(v)) < 5)
                    return f;
        }

        return Math.Clamp(v, _s.Start, _s.End);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_s.Doc is not { } doc)
            return;
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        var (row, key) = HitTest(p);
        _pressX = p.X;
        _checkpointed = false;

        if (props.IsRightButtonPressed)
        {
            ShowMenu(p, row, key);
            return;
        }

        if (!props.IsLeftButtonPressed)
            return;
        _s.Playing = false;
        e.Pointer.Capture(this);

        if (row?.Track is { } tr)
            _s.Select(tr);

        if (key is { } k && p.X > NameW)
        {
            _s.SetTime(k);
            _dragFrom = k;
            if (row?.Track is { } track)
            {
                _drag = DragKind.Key;
                _dragTrack = track;
                var keys = doc.ProtoKeys(track)!;
                _dragLo = keys.Where(x => x.Time < k - 0.002f).Select(x => x.Time + 0.01f).DefaultIfEmpty(_s.Start).Max();
                _dragHi = keys.Where(x => x.Time > k + 0.002f).Select(x => x.Time - 0.01f).DefaultIfEmpty(_s.End).Min();
            }
            else
            {
                _drag = DragKind.Frame;
                var f = _s.Frames();
                _dragLo = f.Where(x => x < k - 0.002f).Select(x => x + 0.01f).DefaultIfEmpty(_s.Start).Max();
                _dragHi = f.Where(x => x > k + 0.002f).Select(x => x - 0.01f).DefaultIfEmpty(_s.End).Min();
            }

            return;
        }

        if (p.X > NameW)
        {
            _drag = DragKind.Scrub;
            _s.SetTime(SnapTime(T(p.X), e.KeyModifiers));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_s.Doc is not { } doc || _drag == DragKind.None)
            return;
        var p = e.GetPosition(this);
        if (_drag == DragKind.Scrub)
        {
            _s.SetTime(SnapTime(T(p.X), e.KeyModifiers));
            return;
        }

        if (Math.Abs(p.X - _pressX) < 3 && !_checkpointed)
            return;
        var res = (e.KeyModifiers & KeyModifiers.Control) != 0 ? 0.001 : 0.01;
        var to = (float) Math.Clamp(Math.Round(T(p.X) / res) * res, _dragLo, _dragHi);
        if (MathF.Abs(to - _dragFrom) < 0.0005f)
            return;
        if (!_checkpointed)
        {
            doc.Checkpoint();
            _checkpointed = true;
        }

        var from = _dragFrom;
        if (_drag == DragKind.Key)
            doc.EditLive(() => doc.MoveKey(_dragTrack, from, to));
        else
            doc.EditLive(() => doc.MoveFrame(_s.EditedActors, from, to));
        _dragFrom = to;
        _s.SetTime(to);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_checkpointed)
            _s.SetStatus(_drag == DragKind.Key ? $"Ключ перенесён на {_dragFrom:0.###} с" : $"Кадр перенесён на {_dragFrom:0.###} с");
        _drag = DragKind.None;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            var t = T(p.X);
            _userZoom = true;
            _pxPerSec = Math.Clamp(_pxPerSec * (e.Delta.Y > 0 ? 1.25 : 0.8), 10, 5000);
            _viewStart = t - (p.X - NameW) / _pxPerSec;
        }
        else if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            _userZoom = true;
            _viewStart -= e.Delta.Y * 40 / _pxPerSec;
        }
        else
        {
            var max = Math.Max(0, _rows.Count * RowH - (Bounds.Height - RulerH - FrameH));
            _scrollY = Math.Clamp(_scrollY - e.Delta.Y * RowH * 2, 0, max);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void ShowMenu(Point p, Row? row, float? key)
    {
        var items = new List<object>();
        var t = key ?? SnapTime(T(p.X), KeyModifiers.None);
        if (p.X > NameW)
            _s.SetTime(t);

        MenuItem Item(string header, Action act, string? gesture = null)
        {
            var mi = new MenuItem { Header = header, InputGesture = gesture == null ? null : KeyGesture.Parse(gesture) };
            mi.Click += (_, _) => act();
            return mi;
        }

        MenuItem EaseMenu(string header, Action<ProcAnimEase> set)
        {
            var mi = new MenuItem { Header = header };
            mi.ItemsSource = EditorState.Eases.Select(x => Item(x.Name, () => set(x.Ease))).ToList();
            return mi;
        }

        if (row?.Track is { } tr && key is { } k)
        {
            _s.Select(tr);
            items.Add(new MenuItem { Header = $"{_s.TrackName(tr)} @ {k:0.###} с", IsEnabled = false });
            items.Add(EaseMenu("Плавность ключа", e => _cmd.SetKeyEase(tr, k, e)));
            items.Add(Item("Удалить ключ", _cmd.DeleteSelectedKey, "Shift+Delete"));
            items.Add(new Separator());
        }

        items.Add(Item("Записать позу (кадр) здесь", _cmd.InsertFrame, "K"));
        if (_s.FrameAtPlayhead() != null)
        {
            items.Add(Item("Дублировать кадр", _cmd.DuplicateFrame, "Ctrl+D"));
            items.Add(Item("Копировать позу", _cmd.Copy, "Ctrl+C"));
            items.Add(EaseMenu("Плавность кадра", _cmd.SetFrameEase));
            items.Add(Item("Удалить кадр", _cmd.DeleteFrame, "Delete"));
        }

        if (_s.Clipboard != null)
            items.Add(Item("Вставить позу", _cmd.Paste, "Ctrl+V"));

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(this);
    }
}
