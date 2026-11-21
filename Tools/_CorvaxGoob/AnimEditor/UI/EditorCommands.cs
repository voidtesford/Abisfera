using Abisfera.AnimEditor.Model;
using Content.Shared._CorvaxGoob.ProcAnim;

namespace Abisfera.AnimEditor.UI;

/// <summary>Frame / key operations shared by the menu, shortcuts, the timeline and the side panels.</summary>
public sealed class EditorCommands
{
    private readonly EditorState _s;

    public EditorCommands(EditorState state) => _s = state;

    private SceneDocument? Doc => _s.Doc;
    private IReadOnlyList<int> Actors => _s.EditedActors;

    public void Undo()
    {
        if (Doc is { CanUndo: true } d)
        {
            d.Undo();
            _s.SetTime(_s.Time);
            _s.SetStatus("Отменено");
        }
    }

    public void Redo()
    {
        if (Doc is { CanRedo: true } d)
        {
            d.Redo();
            _s.SetTime(_s.Time);
            _s.SetStatus("Повторено");
        }
    }

    public void PrevFrame()
    {
        var f = _s.Frames();
        var prev = f.LastOrDefault(x => x < _s.Time - 0.004f, float.NaN);
        _s.SetTime(float.IsNaN(prev) ? _s.Start : prev);
    }

    public void NextFrame()
    {
        var f = _s.Frames();
        var next = f.FirstOrDefault(x => x > _s.Time + 0.004f, float.NaN);
        _s.SetTime(float.IsNaN(next) ? _s.End : next);
    }

    public void Step(float dt) => _s.SetTime(MathF.Round((_s.Time + dt) * 1000f) / 1000f);

    /// <summary>Keys the current pose of the edited actor(s) at the playhead (K).</summary>
    public void InsertFrame()
    {
        if (Doc is not { } d)
            return;
        var t = Snap(_s.Time);
        d.Edit(() =>
        {
            foreach (var a in Actors)
            {
                if (!d.Tracks(a).Any())
                {
                    d.WriteKey(new TrackRef(a, TrackKind.Move), t, v => v);
                    d.WriteKey(new TrackRef(a, TrackKind.Part, ProcAnimBone.Body), t, v => v);
                }
            }

            d.InsertFrame(Actors, t);
        });
        _s.SetTime(t);
        _s.SetStatus($"Кадр {t:0.###} с: поза записана");
    }

    public void DeleteFrame()
    {
        if (Doc is not { } d || _s.FrameAtPlayhead() is not { } t)
        {
            _s.SetStatus("На курсоре нет кадра");
            return;
        }

        d.Edit(() => d.DeleteFrame(Actors, t));
        _s.SetStatus($"Кадр {t:0.###} с удалён");
    }

    public void DeleteSelectedKey()
    {
        if (Doc is not { } d || _s.Selected is not { } tr)
            return;
        var t = _s.FrameAtPlayhead() ?? _s.Time;
        d.Edit(() => d.DeleteKey(tr, t));
        _s.SetStatus($"Ключ удалён: {_s.TrackName(tr)} @ {t:0.###} с");
    }

    public void Copy()
    {
        if (Doc is not { } d)
            return;
        _s.Clipboard = d.CopyFrame(Actors, _s.Time);
        _s.SetStatus($"Поза скопирована ({_s.Clipboard.Keys.Count} дорожек)");
    }

    public void Paste()
    {
        if (Doc is not { } d || _s.Clipboard is not { } clip)
            return;
        var t = Snap(_s.Time);
        var single = Actors.Count == 1 && clip.Keys.Select(k => k.Track.Actor).Distinct().Count() == 1 ? Actors[0] : (int?) null;
        d.Edit(() => d.PasteFrame(clip, t, single));
        _s.SetTime(t);
        _s.SetStatus($"Поза вставлена в {t:0.###} с");
    }

    /// <summary>Duration of the frame at <paramref name="t"/>: time to the next frame (or the end).</summary>
    public float DurationOf(float t)
    {
        var f = _s.Frames();
        var next = f.FirstOrDefault(x => x > t + 0.004f, float.NaN);
        return (float.IsNaN(next) ? _s.End : next) - t;
    }

    /// <summary>Copies the frame at the playhead right after it, pushing the rest of the scene later (Ctrl+D).</summary>
    public void DuplicateFrame()
    {
        if (Doc is not { } d)
            return;
        var t = _s.FrameAtPlayhead() ?? Snap(_s.Time);
        var dur = DurationOf(t);
        if (dur < 0.02f)
            dur = 0.2f;
        var clip = d.CopyFrame(Actors, t);
        d.Edit(() =>
        {
            d.ShiftAfter(t, dur);
            d.PasteFrame(clip, t + dur);
        });
        _s.SetTime(t + dur);
        _s.SetStatus($"Кадр продублирован в {t + dur:0.###} с (всё после сдвинуто на {dur:0.###} с)");
    }

    /// <summary>Sets how long the frame at the playhead lasts; with ripple everything later moves too.</summary>
    public void SetDuration(float duration, bool ripple)
    {
        if (Doc is not { } d || _s.FrameAtPlayhead() is not { } t)
            return;
        duration = MathF.Max(0.01f, MathF.Round(duration * 1000f) / 1000f);
        var f = _s.Frames();
        var next = f.FirstOrDefault(x => x > t + 0.004f, float.NaN);
        if (float.IsNaN(next))
        {
            d.Edit(() => d.SetLength(t + duration));
            _s.SetStatus($"Длина сцены: {t + duration:0.###} с");
            return;
        }

        var delta = duration - (next - t);
        if (ripple)
        {
            var min = d.MinShift(t);
            d.Edit(() => d.ShiftAfter(t, MathF.Max(delta, min)));
        }
        else
        {
            var after = f.FirstOrDefault(x => x > next + 0.004f, float.NaN);
            var to = Math.Clamp(t + duration, t + 0.01f, float.IsNaN(after) ? _s.End : after - 0.01f);
            d.Edit(() => d.MoveFrame(Actors, next, to));
        }

        _s.SetStatus($"Длительность кадра {t:0.###} с: {duration:0.###} с{(ripple ? " (остальное сдвинуто)" : "")}");
    }

    public void SetFrameEase(ProcAnimEase ease)
    {
        if (Doc is not { } d || _s.FrameAtPlayhead() is not { } t)
            return;
        d.Edit(() => d.SetFrameEase(Actors, t, ease));
        _s.SetStatus($"Плавность кадра {t:0.###} с: {EditorState.EaseName(ease)}");
    }

    public void SetKeyEase(TrackRef tr, float t, ProcAnimEase ease)
    {
        if (Doc is not { } d)
            return;
        d.Edit(() => d.SetEase(tr, t, ease));
    }

    public void ResetSelected()
    {
        if (Doc is not { } d || _s.Selected is not { } tr)
            return;
        var t = Snap(_s.Time);
        d.Edit(() => d.WriteKey(tr, t, v => tr.Kind == TrackKind.Move ? v : new KeyValue(0, 0, 0)));
        _s.SetStatus($"{_s.TrackName(tr)}: сброшено в {t:0.###} с");
    }

    public static float Snap(float t) => MathF.Round(t * 1000f) / 1000f;
}
