using System.Numerics;
using Abisfera.AnimEditor.Model;
using Content.Client._CorvaxGoob.ProcAnim;
using Content.Shared._CorvaxGoob.ProcAnim;
using SkiaSharp;

namespace Abisfera.AnimEditor.Rendering;

/// <summary>A baked puppet: one image per group (the game's ProcAnimPuppet split) and the mask-derived pivots.</summary>
public sealed class Puppet : IDisposable
{
    public required int Size;
    public required ProcAnimPuppetResult Result;
    public readonly SKImage?[] Images = new SKImage?[ProcAnimPuppet.GroupCount];
    /// <summary>Per group: its pixels (straight RGBA) for hit testing.</summary>
    public readonly byte[]?[] Pixels = new byte[ProcAnimPuppet.GroupCount][];
    public readonly Dictionary<ProcAnimBone, Vector2> Pivots = new();

    public static ProcAnimBone? PartOf(ProcAnimPuppetGroup g) => g switch
    {
        ProcAnimPuppetGroup.Torso or ProcAnimPuppetGroup.TorsoTop or ProcAnimPuppetGroup.Slung => null,
        _ => Enum.TryParse<ProcAnimBone>(g.ToString(), out var b) ? b : null,
    };

    public static Puppet Build(GameRepo repo, RsiLoader rsi, Appearance app, int dir, bool nearRight, bool wield)
    {
        var layers = AppearanceBuilder.Layers(repo, rsi, app, dir, wield);
        var c = 32;
        foreach (var (_, f) in layers)
            c = Math.Max(c, Math.Max(f.W, f.H));
        c += c % 2;
        var inputs = layers.Select(l => new ProcAnimPuppetInput(l.Key, RsiLoader.Place(l.Frame, c))).ToList();
        if (inputs.Count == 0)
            inputs.Add(new ProcAnimPuppetInput("Chest", new byte[c * c * 4]));
        var result = ProcAnimPuppet.Build(inputs, c, nearRight);
        var p = new Puppet { Size = c, Result = result };
        for (var g = 0; g < ProcAnimPuppet.GroupCount; g++)
        {
            if (result.IsEmpty((ProcAnimPuppetGroup) g))
                continue;
            var px = result.Images[g];
            p.Pixels[g] = px;
            var info = new SKImageInfo(c, c, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            p.Images[g] = SKImage.FromPixelCopy(info, px, c * 4);
        }

        foreach (var (group, pv) in result.Pivots)
            if (PartOf(group) is { } bone)
                p.Pivots[bone] = pv;
        return p;
    }

    public void Dispose()
    {
        foreach (var i in Images)
            i?.Dispose();
    }
}

/// <summary>Puppets of every actor of a scene (side view + each forced view), rebuilt when the look changes.</summary>
public sealed class PuppetCache : IDisposable
{
    private readonly GameRepo _repo;
    private readonly RsiLoader _rsi;
    private readonly Dictionary<(Appearance, int, bool, bool), Puppet> _cache = new();

    public PuppetCache(GameRepo repo, RsiLoader rsi)
    {
        _repo = repo;
        _rsi = rsi;
    }

    public Puppet Get(Appearance app, int dir, bool nearRight, bool wield)
    {
        var key = (app, dir, nearRight, wield);
        if (!_cache.TryGetValue(key, out var p))
            _cache[key] = p = Puppet.Build(_repo, _rsi, app, dir, nearRight, wield);
        return p;
    }

    /// <summary>The puppet an actor is drawn with at <paramref name="t"/> (its side view, or the forced view).</summary>
    public Puppet For(ProcAnimActor tr, Appearance app, float t, bool wield)
    {
        var right = tr.Facing > 0;
        var view = ProcAnimTimeline.ViewInfoAt(tr, t);
        if (view == null)
            return Get(app, right ? 2 : 3, right, wield);
        if (view.Turned)
            return Get(app, right ? 3 : 2, !right, wield);
        var dir = view.Facing switch
        {
            Direction.South => 0,
            Direction.North => 1,
            Direction.East => 2,
            Direction.West => 3,
            _ => right ? 2 : 3,
        };
        return Get(app, dir, right, wield);
    }

    public Puppet Main(ProcAnimActor tr, Appearance app, bool wield)
    {
        var right = tr.Facing > 0;
        return Get(app, right ? 2 : 3, right, wield);
    }

    public void Clear()
    {
        foreach (var p in _cache.Values)
            p.Dispose();
        _cache.Clear();
    }

    public void Dispose() => Clear();
}
