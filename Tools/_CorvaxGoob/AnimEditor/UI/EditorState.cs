using Abisfera.AnimEditor.Model;
using Abisfera.AnimEditor.Rendering;
using Content.Shared._CorvaxGoob.ProcAnim;

namespace Abisfera.AnimEditor.UI;

public enum Tool : byte { Rotate, Translate }

/// <summary>Shared editor state: the open scene, playhead, selection, tools, the puppets' looks.</summary>
public sealed class EditorState
{
    public required GameRepo Repo;
    public required SceneRenderer Renderer;
    public required Settings Settings;

    public SceneDocument? Doc { get; private set; }

    /// <summary>Animation time (keys' t). Real time (hit-stops included) follows it unless playing.</summary>
    public float Time { get; private set; }
    public float Real { get; private set; }
    public bool Playing { get; set; }
    public float Speed { get; set; } = 1f;

    /// <summary>Actor being edited; -1 = all (frame operations on every actor).</summary>
    public int EditActor { get; private set; }
    public TrackRef? Selected { get; private set; }
    public Tool Tool { get; set; } = Tool.Rotate;
    public FrameClip? Clipboard { get; set; }

    public event Action? DocChanged;
    public event Action? TimeChanged;
    public event Action? SelectionChanged;
    public event Action<string>? Status;

    public void SetStatus(string text) => Status?.Invoke(text);

    public Appearance App(int actor) => Settings.Puppet(Math.Min(actor, 1));

    public void SetApp(int actor, Appearance app)
    {
        while (Settings.Puppets.Count <= actor)
            Settings.Puppets.Add(Appearance.Default(Settings.Puppets.Count));
        Settings.Puppets[actor] = app;
        Settings.Save();
        DocChanged?.Invoke();
    }

    public IReadOnlyList<int> EditedActors
    {
        get
        {
            if (Doc == null)
                return Array.Empty<int>();
            if (EditActor < 0)
                return Enumerable.Range(0, Doc.Proto.Actors.Count).ToList();
            return new[] { Math.Min(EditActor, Doc.Proto.Actors.Count - 1) };
        }
    }

    public int PrimaryActor => Doc == null ? 0 : Math.Clamp(EditActor < 0 ? (Selected?.Actor ?? 0) : EditActor, 0, Math.Max(0, Doc.Proto.Actors.Count - 1));

    public void Open(SceneDocument doc)
    {
        if (Doc != null)
            Doc.Changed -= OnDocChanged;
        Doc = doc;
        doc.Changed += OnDocChanged;
        Selected = null;
        EditActor = Math.Min(EditActor < 0 ? 0 : EditActor, doc.Proto.Actors.Count - 1);
        Playing = false;
        SetTime(0f);
        Settings.LastScene = doc.Id;
        Settings.Save();
        DocChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    private void OnDocChanged()
    {
        DocChanged?.Invoke();
    }

    public float Start => Doc == null ? 0f : -Doc.Proto.Intro;
    public float End => Doc?.Proto.Length ?? 1f;

    public void SetTime(float t, bool fromPlayback = false)
    {
        if (Doc == null)
            return;
        Time = Math.Clamp(t, Start, End);
        if (!fromPlayback)
            Real = ProcAnimTimeline.RealTime(Doc.Proto, Time);
        TimeChanged?.Invoke();
    }

    /// <summary>Advances playback by <paramref name="dt"/> wall seconds (hit-stops freeze the animation time).</summary>
    public void Advance(float dt)
    {
        if (Doc == null)
            return;
        var total = ProcAnimTimeline.TotalLength(Doc.Proto);
        Real += dt * Speed;
        if (Real > total)
        {
            if (Settings.Loop)
                Real = Start;
            else
            {
                Real = total;
                Playing = false;
            }
        }

        Time = Math.Clamp(ProcAnimTimeline.AnimTime(Doc.Proto, Real), Start, End);
        TimeChanged?.Invoke();
    }

    public void SetEditActor(int actor)
    {
        EditActor = actor;
        if (Selected is { } s && actor >= 0 && s.Actor != actor)
            Selected = null;
        SelectionChanged?.Invoke();
    }

    public void Select(TrackRef? track)
    {
        Selected = track;
        if (track is { } t && EditActor >= 0)
            EditActor = t.Actor;
        SelectionChanged?.Invoke();
    }

    public void NotifySelection() => SelectionChanged?.Invoke();

    /// <summary>Key times of the edited actors, for the frame row and frame stepping.</summary>
    public List<float> Frames() => Doc?.FrameTimes(EditedActors) ?? new List<float>();

    /// <summary>The keyframe at the playhead (within a few ms), if any.</summary>
    public float? FrameAtPlayhead()
    {
        foreach (var f in Frames())
            if (MathF.Abs(f - Time) < 0.004f)
                return f;
        return null;
    }

    public static string BoneName(ProcAnimBone b) => b switch
    {
        ProcAnimBone.Body => "Торс",
        ProcAnimBone.Head => "Голова",
        ProcAnimBone.ArmNear => "Плечо (ближнее)",
        ProcAnimBone.ArmFar => "Плечо (дальнее)",
        ProcAnimBone.LegNear => "Бедро (ближнее)",
        ProcAnimBone.LegFar => "Бедро (дальнее)",
        ProcAnimBone.ArmNearLow => "Предплечье (ближнее)",
        ProcAnimBone.ArmFarLow => "Предплечье (дальнее)",
        ProcAnimBone.LegNearLow => "Голень (ближняя)",
        ProcAnimBone.LegFarLow => "Голень (дальняя)",
        ProcAnimBone.ArmNearHand => "Кисть (ближняя)",
        ProcAnimBone.ArmFarHand => "Кисть (дальняя)",
        ProcAnimBone.LegNearFoot => "Стопа (ближняя)",
        ProcAnimBone.LegFarFoot => "Стопа (дальняя)",
        ProcAnimBone.HeldNear => "Предмет (ближняя рука)",
        ProcAnimBone.HeldFar => "Предмет (дальняя рука)",
        _ => b.ToString(),
    };

    public string TrackName(TrackRef t)
    {
        var role = Doc != null && t.Actor < Doc.Proto.Actors.Count ? Doc.Proto.Actors[t.Actor].Role : "?";
        return t.Kind switch
        {
            TrackKind.Move => $"Корень ({role})",
            TrackKind.Part => BoneName(t.Bone),
            TrackKind.Prop => $"Реквизит #{t.Index + 1} ({PropLabel(t)})",
            TrackKind.Ik => $"IK-цель: {BoneName(t.Bone)}",
            _ => "?",
        };
    }

    private string PropLabel(TrackRef t)
    {
        if (Doc == null || t.Actor >= Doc.Proto.Actors.Count || t.Index >= Doc.Proto.Actors[t.Actor].Props.Count)
            return "?";
        var p = Doc.Proto.Actors[t.Actor].Props[t.Index];
        var name = p.Item ?? p.Sprite?.CanonPath.Split('/').LastOrDefault()?.Replace(".rsi", "") ?? p.State;
        return $"{name}, {BoneName(p.Part)}";
    }

    public static readonly (ProcAnimEase Ease, string Name)[] Eases =
    {
        (ProcAnimEase.InOut, "Плавно (InOut)"),
        (ProcAnimEase.Linear, "Линейно (Linear)"),
        (ProcAnimEase.In, "Разгон (In)"),
        (ProcAnimEase.Out, "Торможение (Out)"),
        (ProcAnimEase.Step, "Скачок (Step)"),
        (ProcAnimEase.BackIn, "Замах (BackIn)"),
        (ProcAnimEase.BackOut, "Перелёт (BackOut)"),
        (ProcAnimEase.BackInOut, "Замах и перелёт (BackInOut)"),
        (ProcAnimEase.ElasticIn, "Пружина в начале (ElasticIn)"),
        (ProcAnimEase.ElasticOut, "Пружина в конце (ElasticOut)"),
        (ProcAnimEase.HitStop, "Удар со стопом (HitStop)"),
    };

    public static string EaseName(ProcAnimEase e) => Eases.FirstOrDefault(x => x.Ease == e).Name ?? e.ToString();

    /// <summary>Joint limits used for the soft clamp while dragging (relative to the parent, in facing convention).</summary>
    public static (float Min, float Max)? Limit(ProcAnimBone b, ProcAnimJointLimits? l)
    {
        l ??= ProcAnimJointLimits.Default;
        return b switch
        {
            ProcAnimBone.ArmNear or ProcAnimBone.ArmFar => (l.Shoulder.X, l.Shoulder.Y),
            ProcAnimBone.ArmNearLow or ProcAnimBone.ArmFarLow => (l.Elbow.X, l.Elbow.Y),
            ProcAnimBone.LegNear or ProcAnimBone.LegFar => (l.Hip.X, l.Hip.Y),
            ProcAnimBone.LegNearLow or ProcAnimBone.LegFarLow => (l.Knee.X, l.Knee.Y),
            _ => null,
        };
    }

    public void RaiseDocChanged() => DocChanged?.Invoke();
}
