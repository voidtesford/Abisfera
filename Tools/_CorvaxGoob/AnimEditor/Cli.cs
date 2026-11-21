using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Abisfera.AnimEditor.Model;
using Abisfera.AnimEditor.Rendering;
using Content.Shared._CorvaxGoob.ProcAnim;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Abisfera.AnimEditor;

/// <summary>Command line helpers (no window): offline frame renders, a self-test and the pose fingerprints.</summary>
public static class Cli
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        switch (args[0])
        {
            case "--render":
                return Render(args[1], args[2], float.Parse(args[3], Inv), args[4]);
            case "--selftest":
                return SelfTest(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
            case "--fingerprint":
                return Fingerprint(args[1], args[2]);
            default:
                Console.Error.WriteLine("usage: --render <res> <scene> <t> <out.png> | --selftest <res> [tmpdir] | --fingerprint <res> <out.json>");
                return 2;
        }
    }

    private static GameRepo LoadRepo(string res)
    {
        var r = GameRepo.ResolveResources(res) ?? throw new DirectoryNotFoundException(res);
        return GameRepo.Load(r);
    }

    public static int Render(string res, string scene, float t, string output)
    {
        var repo = LoadRepo(res);
        var info = repo.Scenes.First(s => s.Id == scene);
        var doc = SceneDocument.Load(info.File, info.Id);
        var renderer = new SceneRenderer(repo, new RsiLoader(repo.Resources));
        const int w = 900, h = 450;
        const float s = 4f;
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(52, 54, 60));
        var cam = Affine.Translate(w / 2f - 2f * 32 * s, h * 0.62f) * Affine.Scale(s, -s);
        var real = ProcAnimTimeline.RealTime(doc.Proto, t);
        renderer.Render(canvas, doc.Proto, Appearance.Default, t, real, cam, new RenderOptions());
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(output, data.ToArray());
        Console.WriteLine($"rendered {scene} t={t} -> {output}; problems: {doc.Problems.Count}");
        foreach (var p in doc.Problems)
            Console.WriteLine("  " + p);
        return 0;
    }

    /// <summary>Poses of every scene at 0.05 s steps (YAML pivots, no puppet): compared with the game's own load.</summary>
    public static Dictionary<string, List<float>> Fingerprints(GameRepo repo)
    {
        var res = new Dictionary<string, List<float>>();
        foreach (var info in repo.Scenes)
        {
            var doc = SceneDocument.Load(info.File, info.Id);
            res[info.Id] = Fingerprint(doc.Proto);
        }

        return res;
    }

    public static List<float> Fingerprint(ProcAnimScenePrototype proto)
    {
        var list = new List<float>();
        for (var t = -proto.Intro; t <= proto.Length + 1e-4f; t += 0.05f)
        {
            for (var a = 0; a < proto.Actors.Count; a++)
            {
                var m = ProcAnimTimeline.Evaluate(proto.Actors[a].Move, t);
                list.Add(m.X);
                list.Add(m.Y);
                foreach (var p in ProcAnimTimeline.Solve(proto, a, t))
                {
                    list.Add(p.X);
                    list.Add(p.Y);
                    list.Add(p.Rot);
                }
            }
        }

        list.Add(ProcAnimTimeline.TotalLength(proto));
        list.Add(proto.Events.Count);
        return list;
    }

    private static int Fingerprint(string res, string output)
    {
        var repo = LoadRepo(res);
        File.WriteAllText(output, JsonSerializer.Serialize(Fingerprints(repo)));
        Console.WriteLine($"{repo.Scenes.Count} scenes -> {output}");
        return 0;
    }

    private static bool Same(List<float> a, List<float> b, out string why)
    {
        why = "";
        if (a.Count != b.Count)
        {
            why = $"length {a.Count} != {b.Count}";
            return false;
        }

        for (var i = 0; i < a.Count; i++)
            if (MathF.Abs(a[i] - b[i]) > 1e-3f)
            {
                why = $"value #{i}: {a[i]} != {b[i]}";
                return false;
            }

        return true;
    }

    /// <summary>
    /// Mapping without problems, emit → parse → same poses, comment-preserving save of an unchanged / edited scene,
    /// undo, keyframe operations keep the keys sorted.
    /// </summary>
    public static int SelfTest(string res, string? tmp, string? only = null)
    {
        var repo = LoadRepo(res);
        tmp ??= Path.Combine(Path.GetTempPath(), "animeditor-selftest");
        Directory.CreateDirectory(tmp);
        var failures = 0;
        void Fail(string msg)
        {
            failures++;
            Console.WriteLine("FAIL " + msg);
        }

        Console.WriteLine($"scenes: {repo.Scenes.Count}, species: {repo.Species.Count}, markings: {repo.Markings.Count}");
        foreach (var info in repo.Scenes)
        {
            if (only != null && info.Id != only)
                continue;
            var doc = SceneDocument.Load(info.File, info.Id);
            if (doc.Problems.Count > 0)
                Fail($"{info.Id}: problems: {string.Join("; ", doc.Problems)}");
            var fp = Fingerprint(doc.Proto);

            // 1. emitted text parses back to the same tree and the same poses
            var text = doc.SceneText();
            var back = SceneDocument.FindScene(YamlText.Parse(text), info.Id);
            if (back == null || !YamlText.DeepEquals(back, doc.Node))
                Fail($"{info.Id}: emit/parse tree differs");
            else if (!Same(fp, Fingerprint(new NodeMapper().Read<ProcAnimScenePrototype>(back)), out var why))
                Fail($"{info.Id}: emit/parse poses differ: {why}");

            // 2. saving an unchanged scene leaves the file byte-identical
            var copy = Path.Combine(tmp, Path.GetFileName(info.File));
            File.Copy(info.File, copy, true);
            var original = File.ReadAllText(copy);
            var d2 = SceneDocument.Load(copy, info.Id);
            d2.Save();
            if (File.ReadAllText(copy) != original)
                Fail($"{info.Id}: unchanged save altered the file");

            // 3. an edit changes only the edited key lines, comments survive
            d2.Edit(() => d2.WriteKey(new TrackRef(0, TrackKind.Part, ProcAnimBone.Head), 0.5f, v => v with { Rot = v.Rot + 7 }));
            d2.Edit(() => d2.InsertFrame(new[] { 1 % d2.Proto.Actors.Count }, 0.77f));
            d2.Save();
            var edited = File.ReadAllText(copy);
            var commentsBefore = original.Split('\n').Count(l => l.TrimStart().StartsWith('#'));
            var commentsAfter = edited.Split('\n').Count(l => l.TrimStart().StartsWith('#'));
            if (commentsAfter < commentsBefore)
                Fail($"{info.Id}: comments lost on save ({commentsBefore} -> {commentsAfter})");
            var reread = SceneDocument.Load(copy, info.Id);
            if (!YamlText.DeepEquals(reread.Node, d2.Node))
                Fail($"{info.Id}: saved file does not read back as edited");
            if (reread.Problems.Count > 0)
                Fail($"{info.Id}: edited scene has problems: {string.Join("; ", reread.Problems)}");
            var diffLines = DiffCount(original, edited);
            Console.WriteLine($"  {info.Id}: ok-ish, edit touched {diffLines} lines, comments {commentsBefore}->{commentsAfter}");

            // 4. undo restores
            d2.Undo();
            d2.Undo();
            if (!YamlText.DeepEquals(d2.Node, SceneDocument.Load(info.File, info.Id).Node))
                Fail($"{info.Id}: undo did not restore the tree");

            // 5. frame ops keep validation clean
            var d3 = SceneDocument.Load(info.File, info.Id);
            var frames = d3.FrameTimes(new[] { 0 });
            if (frames.Count > 3)
            {
                d3.Edit(() => d3.MoveFrame(new[] { 0 }, frames[2], (frames[1] + frames[2]) / 2));
                d3.Edit(() => d3.PasteFrame(d3.CopyFrame(new[] { 0 }, frames[1]), frames[^1] + 0.3f));
                d3.Edit(() => d3.DeleteFrame(new[] { 0 }, frames[3]));
                d3.Edit(() => d3.ShiftAfter(frames[1], 0.25f));
                d3.Edit(() => d3.ShiftAfter(frames[1], -0.25f));
                d3.Edit(() => d3.SetFrameEase(new[] { 0 }, frames[1], ProcAnimEase.BackOut));
                if (d3.Problems.Count > 0)
                    Fail($"{info.Id}: frame ops broke validation: {string.Join("; ", d3.Problems)}");
            }
        }

        Console.WriteLine(failures == 0 ? "SELFTEST OK" : $"SELFTEST FAILED: {failures}");
        return failures == 0 ? 0 : 1;
    }

    private static int DiffCount(string a, string b)
    {
        var la = a.Split('\n');
        var lb = b.Split('\n');
        var set = new HashSet<string>(la);
        return lb.Count(l => !set.Contains(l));
    }
}
