using System.Globalization;
using Content.Shared._CorvaxGoob.ProcAnim;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using static Abisfera.AnimEditor.Model.YamlText;

namespace Abisfera.AnimEditor.Model;

public enum TrackKind : byte { Move, Part, Prop, Ik }

/// <summary>One keyed track of an actor: its root movement (tiles), a bone, a prop or an IK effector (px).</summary>
public readonly record struct TrackRef(int Actor, TrackKind Kind, ProcAnimBone Bone = ProcAnimBone.Body, int Index = 0)
{
    public bool HasRot => Kind is TrackKind.Part or TrackKind.Prop;
}

public readonly record struct KeyValue(float X, float Y, float Rot);

public sealed record FrameClip(List<(TrackRef Track, KeyValue Value, string? Ease)> Keys);

/// <summary>Editing operations on the node tree (all keep key lists sorted by time, as the game requires).</summary>
public sealed partial class SceneDocument
{
    public const float TimeEps = 0.0005f;

    public YamlSequenceNode? ActorsNode => Node.Get("actors") as YamlSequenceNode;

    public YamlMappingNode? ActorNode(int actor)
        => ActorsNode is { } a && actor >= 0 && actor < a.Children.Count ? a.Children[actor] as YamlMappingNode : null;

    public static float KeyTime(YamlNode key)
        => key is YamlMappingNode m && m.Float("t") is { } t ? t : 0f;

    /// <summary>The key list node of a track (created when <paramref name="create"/>).</summary>
    public YamlSequenceNode? KeysNode(TrackRef tr, bool create)
    {
        var actor = ActorNode(tr.Actor);
        if (actor == null)
            return null;
        switch (tr.Kind)
        {
            case TrackKind.Move:
                return SeqOf(actor, "move", create, "facing");
            case TrackKind.Part:
            {
                if (actor.Get("parts") is not YamlMappingNode parts)
                {
                    if (!create)
                        return null;
                    parts = new YamlMappingNode();
                    actor.InsertAfter("move", "parts", parts);
                }

                var name = tr.Bone.ToString();
                if (parts.Get(name) is not YamlMappingNode bone)
                {
                    if (!create)
                        return null;
                    bone = new YamlMappingNode();
                    if (tr.Bone == ProcAnimBone.Body)
                        bone.Set("pivot", S("0, -14"));
                    parts.Set(name, bone);
                }

                return SeqOf(bone, "keys", create, null);
            }
            case TrackKind.Prop:
                return actor.Get("props") is YamlSequenceNode props && tr.Index < props.Children.Count
                                              && props.Children[tr.Index] is YamlMappingNode prop
                    ? SeqOf(prop, "keys", create, null)
                    : null;
            case TrackKind.Ik:
                return actor.Get("ik") is YamlSequenceNode iks && tr.Index < iks.Children.Count
                                           && iks.Children[tr.Index] is YamlMappingNode ik
                    ? SeqOf(ik, "keys", create, null)
                    : null;
        }

        return null;
    }

    private static YamlSequenceNode? SeqOf(YamlMappingNode map, string key, bool create, string? after)
    {
        if (map.Get(key) is YamlSequenceNode seq)
        {
            if (create && seq.Style == SequenceStyle.Flow && seq.Children.Count == 0)
                seq.Style = SequenceStyle.Any;
            return seq;
        }

        if (!create)
            return null;
        seq = new YamlSequenceNode();
        if (after != null)
            map.InsertAfter(after, key, seq);
        else
            map.Set(key, seq);
        return seq;
    }

    /// <summary>Every track of an actor that has keys (or could: every bone with a track).</summary>
    public IEnumerable<TrackRef> Tracks(int actor, bool includeEmpty = false)
    {
        if (actor < 0 || actor >= Proto.Actors.Count)
            yield break;
        var a = Proto.Actors[actor];
        if (includeEmpty || a.Move.Count > 0)
            yield return new TrackRef(actor, TrackKind.Move);
        foreach (var bone in Enum.GetValues<ProcAnimBone>())
            if (a.Parts.TryGetValue(bone, out var track) && (includeEmpty || track.Keys.Count > 0))
                yield return new TrackRef(actor, TrackKind.Part, bone);
        for (var i = 0; i < a.Props.Count; i++)
            if (includeEmpty || a.Props[i].Keys.Count > 0)
                yield return new TrackRef(actor, TrackKind.Prop, a.Props[i].Part, i);
        for (var i = 0; i < a.Ik.Count; i++)
            if (includeEmpty || a.Ik[i].Keys.Count > 0)
                yield return new TrackRef(actor, TrackKind.Ik, a.Ik[i].Limb, i);
    }

    public List<ProcAnimKey>? ProtoKeys(TrackRef tr)
    {
        if (tr.Actor < 0 || tr.Actor >= Proto.Actors.Count)
            return null;
        var a = Proto.Actors[tr.Actor];
        return tr.Kind switch
        {
            TrackKind.Move => a.Move,
            TrackKind.Part => a.Parts.TryGetValue(tr.Bone, out var b) ? b.Keys : null,
            TrackKind.Prop => tr.Index < a.Props.Count ? a.Props[tr.Index].Keys : null,
            TrackKind.Ik => tr.Index < a.Ik.Count ? a.Ik[tr.Index].Keys : null,
            _ => null,
        };
    }

    /// <summary>Keyed value of a track at <paramref name="t"/> (no procedural layers), as the game evaluates it.</summary>
    public KeyValue KeyedValue(TrackRef tr, float t)
    {
        var keys = ProtoKeys(tr);
        if (keys == null || keys.Count == 0)
            return default;
        var smooth = tr.Kind == TrackKind.Part && Proto.Actors[tr.Actor].Smooth;
        var p = ProcAnimTimeline.Evaluate(keys, t, smooth);
        return new KeyValue(p.X, p.Y, p.Rot);
    }

    /// <summary>Sorted, de-duplicated key times of the actors' frame tracks (root + bones + props + IK).</summary>
    public List<float> FrameTimes(IEnumerable<int> actors)
    {
        var times = new List<float>();
        foreach (var actor in actors)
        foreach (var tr in Tracks(actor))
        foreach (var k in ProtoKeys(tr)!)
            times.Add(k.Time);
        times.Sort();
        var res = new List<float>();
        foreach (var t in times)
            if (res.Count == 0 || t - res[^1] > TimeEps * 2)
                res.Add(t);
        return res;
    }

    public static int FindKey(YamlSequenceNode seq, float t)
    {
        for (var i = 0; i < seq.Children.Count; i++)
            if (MathF.Abs(KeyTime(seq.Children[i]) - t) < TimeEps * 2)
                return i;
        return -1;
    }

    private static YamlMappingNode NewKey(float t)
    {
        var k = FlowMap();
        k.Set("t", Num(t));
        return k;
    }

    private static void SetComp(YamlMappingNode key, string name, float v, bool keepZero)
    {
        if (MathF.Abs(v) < 0.0005f && !keepZero && key.Get(name) == null)
            return;
        key.Set(name, Num(v));
    }

    private static void Insert(YamlSequenceNode seq, YamlNode key)
    {
        var t = KeyTime(key);
        var i = 0;
        while (i < seq.Children.Count && KeyTime(seq.Children[i]) < t)
            i++;
        seq.Children.Insert(i, key);
    }

    private static void SortKeys(YamlSequenceNode seq)
    {
        var sorted = seq.Children.Select((n, i) => (n, i)).OrderBy(p => KeyTime(p.n)).ThenBy(p => p.i).Select(p => p.n).ToList();
        seq.Children.Clear();
        foreach (var n in sorted)
            seq.Children.Add(n);
    }

    /// <summary>
    /// Writes a key on <paramref name="tr"/> at <paramref name="t"/>: an existing key is updated, otherwise a new key
    /// starting from the current keyed value is inserted. Does not checkpoint / rebuild.
    /// </summary>
    public void WriteKey(TrackRef tr, float t, Func<KeyValue, KeyValue> modify, string? ease = null)
    {
        var before = KeyedValue(tr, t);
        var seq = KeysNode(tr, true)!;
        var idx = FindKey(seq, t);
        YamlMappingNode key;
        KeyValue cur;
        if (idx >= 0 && seq.Children[idx] is YamlMappingNode existing)
        {
            key = existing;
            cur = new KeyValue(key.Float("x") ?? 0f, key.Float("y") ?? 0f, key.Float("rot") ?? 0f);
        }
        else
        {
            key = NewKey(t);
            cur = before;
            Insert(seq, key);
        }

        var v = modify(cur);
        SetComp(key, "x", v.X, false);
        SetComp(key, "y", v.Y, false);
        if (tr.HasRot)
            SetComp(key, "rot", v.Rot, key.Children.Count <= 1);
        if (tr.Kind == TrackKind.Move && key.Children.Count <= 1)
            key.Set("x", Num(v.X));
        if (ease != null)
        {
            if (ease == nameof(ProcAnimEase.InOut))
                key.Remove("ease");
            else
                key.Set("ease", S(ease));
        }
    }

    public void Edit(Action action)
    {
        Checkpoint();
        action();
        Rebuild();
    }

    /// <summary>Live edit during a drag (no checkpoint: the drag made one at its start).</summary>
    public void EditLive(Action action)
    {
        action();
        Rebuild();
    }

    private IEnumerable<TrackRef> FrameTracks(IEnumerable<int> actors) => actors.SelectMany(a => Tracks(a));

    /// <summary>Snapshots the current keyed pose at <paramref name="t"/> into every track of the actors.</summary>
    public void InsertFrame(IEnumerable<int> actors, float t)
    {
        foreach (var tr in FrameTracks(actors).ToList())
            WriteKey(tr, t, v => v);
    }

    public void DeleteFrame(IEnumerable<int> actors, float t)
    {
        foreach (var tr in FrameTracks(actors).ToList())
        {
            var seq = KeysNode(tr, false);
            if (seq == null)
                continue;
            var i = FindKey(seq, t);
            if (i >= 0)
                seq.Children.RemoveAt(i);
        }
    }

    public FrameClip CopyFrame(IEnumerable<int> actors, float t)
    {
        var keys = new List<(TrackRef, KeyValue, string?)>();
        foreach (var tr in FrameTracks(actors))
        {
            var seq = KeysNode(tr, false);
            var i = seq == null ? -1 : FindKey(seq, t);
            var ease = i >= 0 ? (seq!.Children[i] as YamlMappingNode)?.Str("ease") : null;
            keys.Add((tr, KeyedValue(tr, t), ease));
        }

        return new FrameClip(keys);
    }

    public void PasteFrame(FrameClip clip, float t, int? toActor = null)
    {
        foreach (var (tr, v, ease) in clip.Keys)
        {
            var target = toActor is { } a ? tr with { Actor = a } : tr;
            if (target.Actor >= Proto.Actors.Count)
                continue;
            WriteKey(target, t, _ => v, ease ?? nameof(ProcAnimEase.InOut));
        }
    }

    /// <summary>Moves the keys at <paramref name="from"/> to <paramref name="to"/> (overwriting keys already there).</summary>
    public void MoveFrame(IEnumerable<int> actors, float from, float to)
    {
        if (MathF.Abs(from - to) < TimeEps)
            return;
        foreach (var tr in FrameTracks(actors).ToList())
            MoveKey(tr, from, to);
    }

    public void MoveKey(TrackRef tr, float from, float to)
    {
        var seq = KeysNode(tr, false);
        if (seq == null)
            return;
        var i = FindKey(seq, from);
        if (i < 0)
            return;
        var key = (YamlMappingNode) seq.Children[i];
        var j = FindKey(seq, to);
        if (j >= 0 && j != i)
            seq.Children.RemoveAt(j);
        key.Set("t", Num(to));
        SortKeys(seq);
    }

    public void DeleteKey(TrackRef tr, float t)
    {
        var seq = KeysNode(tr, false);
        var i = seq == null ? -1 : FindKey(seq, t);
        if (i >= 0)
            seq!.Children.RemoveAt(i);
    }

    public void SetEase(TrackRef tr, float t, ProcAnimEase ease)
    {
        var seq = KeysNode(tr, false);
        var i = seq == null ? -1 : FindKey(seq, t);
        if (i < 0 || seq!.Children[i] is not YamlMappingNode key)
            return;
        if (ease == ProcAnimEase.InOut)
            key.Remove("ease");
        else
            key.Set("ease", S(ease.ToString()));
    }

    public void SetFrameEase(IEnumerable<int> actors, float t, ProcAnimEase ease)
    {
        foreach (var tr in FrameTracks(actors).ToList())
            SetEase(tr, t, ease);
    }

    /// <summary>Ease of the key at <paramref name="t"/> on the first track that has one there (null: none).</summary>
    public ProcAnimEase? FrameEase(IEnumerable<int> actors, float t)
    {
        foreach (var tr in FrameTracks(actors))
        {
            var keys = ProtoKeys(tr)!;
            foreach (var k in keys)
                if (MathF.Abs(k.Time - t) < TimeEps * 2)
                    return k.Ease;
        }

        return null;
    }

    // ---------------- ripple retime ----------------

    private static readonly HashSet<string> TimeFields = new() { "t", "start", "end", "until", "holsterAt", "unholsterAt", "draw", "return" };

    /// <summary>Largest negative shift allowed after <paramref name="after"/> (nothing may cross it).</summary>
    public float MinShift(float after)
    {
        var next = float.MaxValue;
        VisitTimes(Node, false, (m, k, v) =>
        {
            if (v > after + TimeEps && v < next)
                next = v;
        });
        return next == float.MaxValue ? -(Proto.Length - after - 0.05f) : -(next - after - 0.01f);
    }

    /// <summary>Shifts every time later than <paramref name="after"/> (keys, events, views, hides, props...) and the length.</summary>
    public void ShiftAfter(float after, float delta)
    {
        delta = MathF.Max(delta, MinShift(after));
        VisitTimes(Node, false, (m, k, v) =>
        {
            if (v > after + TimeEps)
                m.Set(k, Num(v + delta));
        });
        if (Node.Float("length") is { } len && len > after)
            Node.Set("length", Num(MathF.Max(after + 0.05f, len + delta)));
    }

    private static void VisitTimes(YamlNode node, bool skip, Action<YamlMappingNode, string, float> visit)
    {
        switch (node)
        {
            case YamlMappingNode m:
                foreach (var (k, v) in m.Children.ToList())
                {
                    var key = (k as YamlScalarNode)?.Value ?? "";
                    // stowHands keys are relative to one reach, not scene time.
                    var childSkip = skip || key == "stowHands";
                    if (!childSkip && TimeFields.Contains(key) && v is YamlScalarNode s
                        && float.TryParse(s.Value, NumberStyles.Float, Inv, out var f) && f < 1e6f)
                        visit(m, key, f);
                    else
                        VisitTimes(v, childSkip, visit);
                }

                break;
            case YamlSequenceNode q:
                foreach (var c in q.Children)
                    VisitTimes(c, skip, visit);
                break;
        }
    }

    // ---------------- events ----------------

    public YamlSequenceNode? EventsNode => Node.Get("events") as YamlSequenceNode;

    public void MoveEvent(int index, float t)
    {
        if (EventsNode is not { } ev || index < 0 || index >= ev.Children.Count || ev.Children[index] is not YamlMappingNode e)
            return;
        e.Set("t", Num(t));
        SortKeys(ev);
    }

    public void SetLength(float length)
    {
        Node.Set("length", Num(MathF.Max(0.05f, length)));
    }
}
