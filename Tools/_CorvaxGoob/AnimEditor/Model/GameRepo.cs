using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Abisfera.AnimEditor.Model;

public sealed record SceneInfo(string Id, string File, string Category, string? EditorName, IReadOnlyList<string> Roles, float Length)
{
    public string Title => EditorName is { Length: > 0 } n ? $"{n} ({Id})" : Id;
}

public sealed record SpriteRef(string Rsi, string State);

public sealed record SpeciesInfo(string Id, string Name, Dictionary<string, SpriteRef> Base, Color? DefaultSkin, string? Coloration,
    List<string> DefaultMarkings, bool RoundStart);

public sealed record MarkingInfo(string Id, string BodyPart, List<SpriteRef> Sprites);

/// <summary>
/// What the editor needs from the game's Resources: the procAnimScene prototypes (and their files), species with
/// their base body sprites and required default markings, marking sprites, entity sprite paths (outfits / items) and
/// Russian names from the locale.
/// </summary>
public sealed class GameRepo
{
    public string Resources { get; }
    public string Prototypes => Path.Combine(Resources, "Prototypes");
    public List<SceneInfo> Scenes { get; } = new();
    public Dictionary<string, SpeciesInfo> Species { get; } = new();
    public Dictionary<string, MarkingInfo> Markings { get; } = new();
    public List<string> LoadErrors { get; } = new();

    private readonly Dictionary<string, string> _entityBlocks = new();
    private readonly Dictionary<string, string> _loc = new();

    private GameRepo(string resources)
    {
        Resources = resources;
    }

    /// <summary>Accepts the repo root or its Resources folder; null when neither looks right.</summary>
    public static string? ResolveResources(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        var f = folder.Trim().Trim('"');
        if (Directory.Exists(Path.Combine(f, "Prototypes")) && Directory.Exists(Path.Combine(f, "Textures")))
            return Path.GetFullPath(f);
        var r = Path.Combine(f, "Resources");
        if (Directory.Exists(Path.Combine(r, "Prototypes")) && Directory.Exists(Path.Combine(r, "Textures")))
            return Path.GetFullPath(r);
        return null;
    }

    private static readonly Regex BlockSplit = new(@"\n(?=- type: )", RegexOptions.Compiled);
    private static readonly Regex IdRe = new(@"^\s*id:\s*(\S+)", RegexOptions.Compiled | RegexOptions.Multiline);

    public static GameRepo Load(string resources)
    {
        var repo = new GameRepo(resources);
        repo.Scan();
        return repo;
    }

    public void RescanScenes()
    {
        Scenes.Clear();
        foreach (var file in Directory.EnumerateFiles(Prototypes, "*.yml", SearchOption.AllDirectories))
        {
            var text = SafeRead(file);
            if (text != null && text.Contains("procAnimScene"))
                ParseFile(file, text, scenesOnly: true);
        }

        Scenes.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
    }

    private static string? SafeRead(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch
        {
            return null;
        }
    }

    private void Scan()
    {
        foreach (var file in Directory.EnumerateFiles(Prototypes, "*.yml", SearchOption.AllDirectories))
        {
            var text = SafeRead(file);
            if (text == null)
                continue;
            if (text.Contains("type: entity"))
            {
                foreach (var block in BlockSplit.Split(text.Replace("\r\n", "\n")))
                {
                    if (!block.TrimStart().StartsWith("- type: entity"))
                        continue;
                    var m = IdRe.Match(block);
                    if (m.Success)
                        _entityBlocks.TryAdd(m.Groups[1].Value, block);
                }
            }

            if (text.Contains("procAnimScene") || text.Contains("type: species") || text.Contains("humanoidBaseSprite")
                || text.Contains("type: marking") || text.Contains("markingPoints"))
                ParseFile(file, text, scenesOnly: false);
        }

        // ru-RU first (a Cyrillic value wins over a copied English one), en-US fills the gaps
        foreach (var lang in new[] { "ru-RU", "en-US" })
        {
            var locDir = Path.Combine(Resources, "Locale", lang);
            if (!Directory.Exists(locDir))
                continue;
            foreach (var file in Directory.EnumerateFiles(locDir, "*.ftl", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadLines(file))
                {
                    var eq = line.IndexOf(" = ", StringComparison.Ordinal);
                    if (eq <= 0 || line.StartsWith(' ') || line.StartsWith('#'))
                        continue;
                    var key = line[..eq].Trim();
                    var value = line[(eq + 3)..].Trim();
                    if (!_loc.TryGetValue(key, out var old) || lang == "ru-RU" && !HasCyrillic(old) && HasCyrillic(value))
                        _loc[key] = value;
                }
            }
        }

        BuildSpecies();
        Scenes.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
    }

    private static bool HasCyrillic(string s) => s.Any(c => c is >= '\u0400' and <= '\u04FF');

    public string Loc(string id) => _loc.TryGetValue(id, out var v) ? v : id;

    private readonly Dictionary<string, YamlMappingNode> _speciesNodes = new();
    private readonly Dictionary<string, YamlMappingNode> _baseSpritesNodes = new();
    private readonly Dictionary<string, SpriteRef?> _humanoidBase = new();
    private readonly Dictionary<string, YamlMappingNode> _markingPoints = new();

    private void ParseFile(string file, string text, bool scenesOnly)
    {
        YamlStream ys;
        try
        {
            ys = YamlText.Parse(text);
        }
        catch (Exception e)
        {
            LoadErrors.Add($"{Path.GetFileName(file)}: {e.Message}");
            return;
        }

        foreach (var doc in ys.Documents)
        {
            if (doc.RootNode is not YamlSequenceNode seq)
                continue;
            foreach (var item in seq.Children)
            {
                if (item is not YamlMappingNode m || m.Str("type") is not { } type || m.Str("id") is not { } id)
                    continue;
                switch (type)
                {
                    case "procAnimScene":
                        var roles = (m.Get("actors") as YamlSequenceNode)?.Children.OfType<YamlMappingNode>()
                            .Select(a => a.Str("role") ?? "?").ToList() ?? new List<string>();
                        Scenes.Add(new SceneInfo(id, file, m.Str("category") ?? GuessCategory(id), m.Str("editorName"), roles,
                            m.Float("length") ?? 6.8f));
                        break;
                    case "species" when !scenesOnly:
                        _speciesNodes[id] = m;
                        break;
                    case "speciesBaseSprites" when !scenesOnly:
                        _baseSpritesNodes[id] = m;
                        break;
                    case "humanoidBaseSprite" when !scenesOnly:
                        _humanoidBase[id] = m.Get("baseSprite") is YamlMappingNode bs && bs.Str("sprite") is { } rsi && bs.Str("state") is { } st
                            ? new SpriteRef(rsi, st)
                            : null;
                        break;
                    case "markingPoints" when !scenesOnly:
                        _markingPoints[id] = m;
                        break;
                    case "marking" when !scenesOnly:
                        var sprites = new List<SpriteRef>();
                        if (m.Get("sprites") is YamlSequenceNode ss)
                            foreach (var s in ss.Children.OfType<YamlMappingNode>())
                                if (s.Str("sprite") is { } rsi2 && s.Str("state") is { } st2)
                                    sprites.Add(new SpriteRef(rsi2, st2));
                        Markings[id] = new MarkingInfo(id, m.Str("bodyPart") ?? "", sprites);
                        break;
                }
            }
        }
    }

    /// <summary>Scenes written before the <c>category</c> field: a finisher by its name.</summary>
    public static string GuessCategory(string id)
        => id.Contains("Finisher", StringComparison.OrdinalIgnoreCase) ? "Finisher" : "Other";

    private void BuildSpecies()
    {
        foreach (var (id, m) in _speciesNodes)
        {
            if (m.Str("sprites") is not { } spritesId || !_baseSpritesNodes.TryGetValue(spritesId, out var bs)
                || bs.Get("sprites") is not YamlMappingNode layers)
                continue;
            var map = new Dictionary<string, SpriteRef>();
            foreach (var (k, v) in layers.Children)
                if (k is YamlScalarNode ks && v is YamlScalarNode vs && _humanoidBase.TryGetValue(vs.Value ?? "", out var sr) && sr != null)
                    map[ks.Value!] = sr;
            if (!map.ContainsKey("Chest"))
                continue;
            Color? skin = m.Str("defaultSkinTone") is { } hex && Color.TryFromHex(hex, out var c) ? c : null;
            var defaults = new List<string>();
            if (m.Str("markingLimits") is { } limId && _markingPoints.TryGetValue(limId, out var lim) && lim.Get("points") is YamlMappingNode pts)
            {
                foreach (var (_, pv) in pts.Children)
                {
                    if (pv is not YamlMappingNode p || p.Str("required") != "true" || p.Get("defaultMarkings") is not YamlSequenceNode dm)
                        continue;
                    foreach (var d in dm.Children.OfType<YamlScalarNode>())
                        if (d.Value is { } dv)
                            defaults.Add(dv);
                }
            }

            var name = m.Str("name") is { } loc ? Loc(loc) : id;
            Species[id] = new SpeciesInfo(id, name, map, skin, m.Str("skinColoration"), defaults, m.Str("roundStart") == "true");
        }
    }

    // ---------------- entities ----------------

    private static readonly Regex ParentRe = new(@"^\s*parent:\s*\[?\s*([\w-]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>RSI path of an entity's Clothing / Sprite / Item component (following parents), like preview.py.</summary>
    public string? EntitySprite(string id, int depth = 0)
    {
        if (depth > 15 || !_entityBlocks.TryGetValue(id, out var block))
            return null;
        foreach (var comp in new[] { "Clothing", "Sprite", "Item" })
        {
            var m = Regex.Match(block, @"- type: " + comp + @"\n((?:\s{4,}.*\n?)*)");
            if (!m.Success)
                continue;
            var s = Regex.Match(m.Groups[1].Value, @"^\s+sprite:\s*(\S+)", RegexOptions.Multiline);
            if (s.Success)
                return s.Groups[1].Value;
        }

        var p = ParentRe.Match(block);
        return p.Success ? EntitySprite(p.Groups[1].Value, depth + 1) : null;
    }

    public bool HasEntity(string id) => _entityBlocks.ContainsKey(id);
}
