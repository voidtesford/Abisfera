using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Abisfera.AnimEditor.Model;

/// <summary>
/// YAML helpers for the node-tree model: deep clone / compare, a small emitter in the SS14 prototype style and a
/// comment-preserving save that rewrites only the spans of the original text that actually changed.
/// </summary>
public static class YamlText
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------------- construction helpers ----------------

    public static YamlScalarNode S(string value) => new(value);

    public static YamlScalarNode Num(float v) => new(FormatNum(v));

    public static string FormatNum(float v)
    {
        var r = MathF.Round(v, 3);
        if (MathF.Abs(r) < 0.0005f)
            r = 0f;
        return r.ToString("0.###", Inv);
    }

    public static YamlMappingNode FlowMap() => new() { Style = MappingStyle.Flow };

    public static YamlNode? Get(this YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v : null;

    public static string? Str(this YamlMappingNode map, string key)
        => map.Get(key) is YamlScalarNode s ? s.Value : null;

    public static float? Float(this YamlMappingNode map, string key)
        => map.Get(key) is YamlScalarNode s && float.TryParse(s.Value, NumberStyles.Float, Inv, out var f) ? f : null;

    public static void Set(this YamlMappingNode map, string key, YamlNode value)
        => map.Children[new YamlScalarNode(key)] = value;

    public static bool Remove(this YamlMappingNode map, string key)
        => map.Children.Remove(new YamlScalarNode(key));

    /// <summary>Inserts <paramref name="key"/> after <paramref name="after"/> (or at the end) keeping the other entries' order.</summary>
    public static void InsertAfter(this YamlMappingNode map, string? after, string key, YamlNode value)
    {
        var entries = map.Children.ToList();
        map.Children.Clear();
        var done = false;
        foreach (var (k, v) in entries)
        {
            map.Children.Add(k, v);
            if (!done && after != null && k is YamlScalarNode ks && ks.Value == after)
            {
                map.Children.Add(new YamlScalarNode(key), value);
                done = true;
            }
        }

        if (!done)
            map.Children.Add(new YamlScalarNode(key), value);
    }

    // ---------------- parse / clone / compare ----------------

    public static YamlStream Parse(string text)
    {
        var ys = new YamlStream();
        ys.Load(new StringReader(text));
        return ys;
    }

    public static YamlNode Clone(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode s:
                var sc = new YamlScalarNode(s.Value) { Style = s.Style };
                if (!s.Tag.IsEmpty)
                    sc.Tag = s.Tag;
                return sc;
            case YamlSequenceNode q:
                var qc = new YamlSequenceNode { Style = q.Style };
                if (!q.Tag.IsEmpty)
                    qc.Tag = q.Tag;
                foreach (var c in q.Children)
                    qc.Children.Add(Clone(c));
                return qc;
            case YamlMappingNode m:
                var mc = new YamlMappingNode { Style = m.Style };
                if (!m.Tag.IsEmpty)
                    mc.Tag = m.Tag;
                foreach (var (k, v) in m.Children)
                    mc.Children.Add(Clone(k), Clone(v));
                return mc;
            default:
                throw new NotSupportedException(node.GetType().Name);
        }
    }

    public static bool DeepEquals(YamlNode a, YamlNode b)
    {
        if (a.NodeType != b.NodeType || TagOf(a) != TagOf(b))
            return false;
        switch (a)
        {
            case YamlScalarNode sa:
                return sa.Value == ((YamlScalarNode) b).Value;
            case YamlSequenceNode qa:
            {
                var qb = (YamlSequenceNode) b;
                if (qa.Children.Count != qb.Children.Count)
                    return false;
                for (var i = 0; i < qa.Children.Count; i++)
                    if (!DeepEquals(qa.Children[i], qb.Children[i]))
                        return false;
                return true;
            }
            case YamlMappingNode ma:
            {
                var mb = (YamlMappingNode) b;
                if (ma.Children.Count != mb.Children.Count)
                    return false;
                var la = ma.Children.ToList();
                var lb = mb.Children.ToList();
                for (var i = 0; i < la.Count; i++)
                    if (!DeepEquals(la[i].Key, lb[i].Key) || !DeepEquals(la[i].Value, lb[i].Value))
                        return false;
                return true;
            }
        }

        return false;
    }

    private static string TagOf(YamlNode n) => n.Tag.IsEmpty || n.Tag.IsNonSpecific ? "" : n.Tag.Value;

    // ---------------- emitter ----------------

    /// <summary>
    /// Emits <paramref name="node"/> as it would appear at column <paramref name="col"/> (the first line is written
    /// without indentation: the caller is already there). Block collections stay block unless <paramref name="flow"/>.
    /// </summary>
    public static string Emit(YamlNode node, int col, bool flow, string nl)
    {
        var sb = new StringBuilder();
        EmitNode(sb, node, col, flow || IsFlow(node), nl, inFlow: false);
        return sb.ToString();
    }

    public static bool IsFlow(YamlNode n) => n switch
    {
        YamlMappingNode m => m.Style == MappingStyle.Flow || m.Children.Count == 0,
        YamlSequenceNode s => s.Style == SequenceStyle.Flow || s.Children.Count == 0,
        _ => true,
    };

    private static void EmitTag(StringBuilder sb, YamlNode n)
    {
        var t = TagOf(n);
        if (t.Length > 0)
            sb.Append(t).Append(' ');
    }

    private static void EmitNode(StringBuilder sb, YamlNode node, int col, bool flow, string nl, bool inFlow)
    {
        switch (node)
        {
            case YamlScalarNode s:
                EmitTag(sb, s);
                sb.Append(Scalar(s, inFlow));
                return;
            case YamlMappingNode m when flow || m.Children.Count == 0:
                EmitTag(sb, m);
                sb.Append('{');
                var first = true;
                foreach (var (k, v) in m.Children)
                {
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    EmitNode(sb, k, col, true, nl, true);
                    sb.Append(": ");
                    EmitNode(sb, v, col, true, nl, true);
                }

                sb.Append('}');
                return;
            case YamlSequenceNode q when flow || q.Children.Count == 0:
                EmitTag(sb, q);
                sb.Append('[');
                for (var i = 0; i < q.Children.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    EmitNode(sb, q.Children[i], col, true, nl, true);
                }

                sb.Append(']');
                return;
            case YamlMappingNode m:
            {
                var pad = new string(' ', col);
                var first2 = true;
                if (TagOf(m).Length > 0)
                {
                    sb.Append(TagOf(m)).Append(nl).Append(pad);
                }

                foreach (var (k, v) in m.Children)
                {
                    if (!first2)
                        sb.Append(nl).Append(pad);
                    first2 = false;
                    EmitNode(sb, k, col, true, nl, false);
                    sb.Append(':');
                    if (IsFlow(v))
                    {
                        sb.Append(' ');
                        EmitNode(sb, v, col, true, nl, false);
                    }
                    else if (v is YamlSequenceNode)
                    {
                        // SS14 style: a block sequence under a key sits at the key's column.
                        sb.Append(nl).Append(pad);
                        EmitNode(sb, v, col, false, nl, false);
                    }
                    else
                    {
                        sb.Append(nl).Append(new string(' ', col + 2));
                        EmitNode(sb, v, col + 2, false, nl, false);
                    }
                }

                return;
            }
            case YamlSequenceNode q:
            {
                var pad = new string(' ', col);
                for (var i = 0; i < q.Children.Count; i++)
                {
                    if (i > 0)
                        sb.Append(nl).Append(pad);
                    sb.Append("- ");
                    var c = q.Children[i];
                    if (IsFlow(c))
                        EmitNode(sb, c, col + 2, true, nl, false);
                    else
                        EmitNode(sb, c, col + 2, false, nl, false);
                }

                return;
            }
        }
    }

    private static string Scalar(YamlScalarNode s, bool inFlow)
    {
        var v = s.Value ?? "";
        switch (s.Style)
        {
            case ScalarStyle.SingleQuoted:
                return "'" + v.Replace("'", "''") + "'";
            case ScalarStyle.DoubleQuoted:
                return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        }

        if (NeedsQuotes(v, inFlow))
            return "'" + v.Replace("'", "''") + "'";
        return v;
    }

    private static bool NeedsQuotes(string v, bool inFlow)
    {
        if (v.Length == 0)
            return true;
        if (v.Contains(": ") || v.Contains(" #") || v.EndsWith(':') || v.Contains('\n'))
            return true;
        if ("!&*?|>'\"%@`#-[]{},".Contains(v[0]) && !(v[0] == '-' && v.Length > 1 && (char.IsDigit(v[1]) || v[1] == '.')))
            return true;
        if (inFlow && v.IndexOfAny(new[] { ',', '[', ']', '{', '}' }) >= 0)
            return true;
        return v != v.Trim();
    }

    // ---------------- span-preserving save ----------------

    private readonly record struct Edit(int Start, int End, string Text)
    {
        public int Seq { get; init; }
    }

    /// <summary>
    /// Rewrites <paramref name="original"/> (a node parsed from <paramref name="text"/>, with marks) into
    /// <paramref name="edited"/> by replacing only the changed spans; everything else (comments, layout) is kept.
    /// </summary>
    public static string ApplyDiff(string text, YamlNode original, YamlNode edited)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var edits = new List<Edit>();
        Diff(text, original, edited, edits, nl);
        var sb = new StringBuilder(text);
        // Same position: the edit made later (an outer mapping's appended keys) goes in first, so the inner one ends up before it.
        var ordered = edits.Select((e, i) => e with { Seq = i }).OrderByDescending(e => e.Start).ThenByDescending(e => e.Seq);
        foreach (var e in ordered)
        {
            sb.Remove(e.Start, e.End - e.Start);
            sb.Insert(e.Start, e.Text);
        }

        return sb.ToString();
    }

    private static int ColumnOf(string text, int index)
    {
        var i = index;
        while (i > 0 && text[i - 1] != '\n')
            i--;
        return index - i;
    }

    private static int StartOf(YamlNode n) => (int) n.Start.Index;

    /// <summary>End index (exclusive) of a node in the text: collections' own End marks are not reliable.</summary>
    public static int EndOf(string text, YamlNode n)
    {
        switch (n)
        {
            case YamlScalarNode:
                return (int) n.End.Index;
            case YamlMappingNode m when m.Style == MappingStyle.Flow:
                return FlowEnd(text, StartOf(m), '{', '}');
            case YamlSequenceNode q when q.Style == SequenceStyle.Flow:
                return FlowEnd(text, StartOf(q), '[', ']');
            case YamlMappingNode m:
                return m.Children.Count == 0 ? StartOf(m) : EndOf(text, m.Children.Last().Value);
            case YamlSequenceNode q:
                return q.Children.Count == 0 ? StartOf(q) : EndOf(text, q.Children[^1]);
        }

        return (int) n.End.Index;
    }

    private static int FlowEnd(string text, int start, char open, char close)
    {
        var i = text.IndexOf(open, start);
        if (i < 0)
            return start;
        var depth = 0;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'')
            {
                i++;
                while (i < text.Length && !(text[i] == '\'' && (i + 1 >= text.Length || text[i + 1] != '\'')))
                    i += text[i] == '\'' ? 2 : 1;
                continue;
            }

            if (c == '"')
            {
                i++;
                while (i < text.Length && text[i] != '"')
                    i += text[i] == '\\' ? 2 : 1;
                continue;
            }

            if (c is '{' or '[')
                depth++;
            else if (c is '}' or ']')
            {
                depth--;
                if (depth == 0)
                    return i + 1;
            }
        }

        return text.Length;
    }

    private static void Replace(string text, YamlNode orig, YamlNode edited, List<Edit> edits, string nl)
    {
        var start = StartOf(orig);
        var end = EndOf(text, orig);
        var col = ColumnOf(text, start);
        // A flow / scalar position (after "key: ") can only take flow; a block position keeps block.
        var flow = IsFlow(orig) || orig is YamlScalarNode;
        edits.Add(new Edit(start, end, Emit(edited, col, flow, nl)));
    }

    /// <summary>Position of the "- " of a block sequence item.</summary>
    private static int DashOf(string text, YamlNode item)
    {
        var i = StartOf(item) - 1;
        while (i >= 0 && text[i] == ' ')
            i--;
        return i >= 0 && text[i] == '-' ? i : StartOf(item);
    }

    private static int LineStart(string text, int i)
    {
        while (i > 0 && text[i - 1] != '\n')
            i--;
        return i;
    }

    private static int LineEndInclusive(string text, int i)
    {
        while (i < text.Length && text[i] != '\n')
            i++;
        return Math.Min(text.Length, i + 1);
    }

    /// <summary>
    /// A block sequence whose length changed: items are matched (longest common subsequence of equal items), the
    /// others are paired up / deleted / inserted one by one, so comments between the items stay where they were.
    /// </summary>
    private static void DiffSequence(string text, YamlSequenceNode qo, YamlSequenceNode qe, List<Edit> edits, string nl)
    {
        var o = qo.Children;
        var e = qe.Children;
        var lcs = new int[o.Count + 1, e.Count + 1];
        for (var i = o.Count - 1; i >= 0; i--)
        for (var j = e.Count - 1; j >= 0; j--)
            lcs[i, j] = DeepEquals(o[i], e[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var pairs = new List<(int O, int E)>();
        {
            int i = 0, j = 0;
            while (i < o.Count && j < e.Count)
            {
                if (DeepEquals(o[i], e[j]))
                {
                    pairs.Add((i, j));
                    i++;
                    j++;
                }
                else if (lcs[i + 1, j] >= lcs[i, j + 1])
                    i++;
                else
                    j++;
            }
        }

        // gaps between the matched anchors: pair positionally, delete / insert the rest
        var kept = new bool[o.Count];
        var plan = new List<(int Orig, int Edit)>(); // Orig -1 = insert, Edit -1 = delete
        var po = 0;
        var pe = 0;
        foreach (var (ao, ae) in pairs.Append((o.Count, e.Count)))
        {
            var go = ao - po;
            var ge = ae - pe;
            var common = Math.Min(go, ge);
            for (var k = 0; k < common; k++)
                plan.Add((po + k, pe + k));
            for (var k = common; k < go; k++)
                plan.Add((po + k, -1));
            for (var k = common; k < ge; k++)
                plan.Add((-1, pe + k));
            if (ao < o.Count)
                plan.Add((ao, ae));
            po = ao + 1;
            pe = ae + 1;
        }

        foreach (var (oi, ei) in plan)
            if (oi >= 0 && ei >= 0)
                kept[oi] = true;
        if (!kept.Any(k => k))
        {
            Replace(text, qo, qe, edits, nl);
            return;
        }

        var col = ColumnOf(text, DashOf(text, o[0]));
        var pad = new string(' ', col);
        var lastKept = -1;
        var firstKept = Array.IndexOf(kept, true);
        foreach (var (oi, ei) in plan)
        {
            if (oi >= 0 && ei >= 0)
            {
                Diff(text, o[oi], e[ei], edits, nl);
                lastKept = oi;
            }
            else if (oi >= 0)
            {
                var from = LineStart(text, DashOf(text, o[oi]));
                edits.Add(new Edit(from, LineEndInclusive(text, EndOf(text, o[oi]) - 1), ""));
            }
            else
            {
                var item = "- " + Emit(e[ei], col + 2, IsFlow(e[ei]), nl);
                if (lastKept >= 0)
                {
                    var at = EndOf(text, o[lastKept]);
                    edits.Add(new Edit(at, at, nl + pad + item));
                }
                else
                {
                    var at = DashOf(text, o[firstKept]);
                    edits.Add(new Edit(at, at, item + nl + pad));
                }
            }
        }
    }

    private static void Diff(string text, YamlNode orig, YamlNode edited, List<Edit> edits, string nl)
    {
        if (DeepEquals(orig, edited))
            return;
        if (orig.NodeType != edited.NodeType || TagOf(orig) != TagOf(edited) || IsFlow(orig))
        {
            Replace(text, orig, edited, edits, nl);
            return;
        }

        switch (orig)
        {
            case YamlSequenceNode qo:
            {
                var qe = (YamlSequenceNode) edited;
                if (qo.Children.Count == qe.Children.Count)
                {
                    for (var i = 0; i < qo.Children.Count; i++)
                        Diff(text, qo.Children[i], qe.Children[i], edits, nl);
                    return;
                }

                DiffSequence(text, qo, qe, edits, nl);
                return;
            }
            case YamlMappingNode mo:
            {
                var me = (YamlMappingNode) edited;
                foreach (var key in mo.Children.Keys)
                    if (!me.Children.ContainsKey(key))
                    {
                        Replace(text, orig, edited, edits, nl);
                        return;
                    }

                var col = ColumnOf(text, StartOf(mo.Children.First().Key));
                var added = new StringBuilder();
                foreach (var (k, v) in me.Children)
                {
                    if (mo.Children.TryGetValue(k, out var ov))
                    {
                        Diff(text, ov, v, edits, nl);
                        continue;
                    }

                    var single = new YamlMappingNode();
                    single.Children.Add(k, v);
                    added.Append(nl).Append(new string(' ', col)).Append(Emit(single, col, false, nl));
                }

                if (added.Length > 0)
                {
                    var end = EndOf(text, mo);
                    edits.Add(new Edit(end, end, added.ToString()));
                }

                return;
            }
            default:
                Replace(text, orig, edited, edits, nl);
                return;
        }
    }
}
