using System.Text;
using Content.Shared._CorvaxGoob.ProcAnim;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using static Abisfera.AnimEditor.Model.YamlText;

namespace Abisfera.AnimEditor.Model;

/// <summary>
/// One <c>procAnimScene</c> being edited: the YAML node tree is the model (unknown / server-side fields survive as
/// written), rebuilt into the game's <see cref="ProcAnimScenePrototype"/> for posing after every change.
/// Saving rewrites only the changed spans of the source file, so comments and layout elsewhere stay.
/// </summary>
public sealed partial class SceneDocument
{
    public string FilePath { get; private set; }
    public string Id { get; private set; }
    public bool IsNew { get; private set; }

    /// <summary>Working tree (edited).</summary>
    public YamlMappingNode Node { get; private set; }

    private YamlMappingNode? _saved;
    private readonly Stack<YamlMappingNode> _undo = new();
    private readonly Stack<YamlMappingNode> _redo = new();

    public ProcAnimScenePrototype Proto { get; private set; } = default!;
    public List<string> Problems { get; private set; } = new();
    public int Revision { get; private set; }

    public event Action? Changed;

    private SceneDocument(string file, string id, YamlMappingNode node, bool isNew)
    {
        FilePath = file;
        Id = id;
        Node = node;
        IsNew = isNew;
        _savedId = isNew ? null : id;
        _saved = isNew ? null : (YamlMappingNode) Clone(node);
        Rebuild();
    }

    public bool Dirty => _saved == null || !DeepEquals(_saved, Node);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public static SceneDocument Load(string file, string id)
    {
        var text = File.ReadAllText(file);
        var node = FindScene(Parse(text), id) ?? throw new InvalidDataException($"в {file} нет procAnimScene {id}");
        return new SceneDocument(file, id, (YamlMappingNode) Clone(node), false);
    }

    public static SceneDocument FromNode(string file, string id, YamlMappingNode node)
        => new(file, id, node, true);

    public static YamlMappingNode? FindScene(YamlStream ys, string id)
    {
        foreach (var doc in ys.Documents)
        {
            if (doc.RootNode is not YamlSequenceNode seq)
                continue;
            foreach (var item in seq.Children)
                if (item is YamlMappingNode m && m.Str("type") == "procAnimScene" && m.Str("id") == id)
                    return m;
        }

        return null;
    }

    public void Rebuild()
    {
        var mapper = new NodeMapper();
        var proto = mapper.Read<ProcAnimScenePrototype>(Node);
        var problems = new List<string>(mapper.Problems);
        try
        {
            problems.AddRange(ProcAnimValidation.Validate(proto));
        }
        catch (Exception e)
        {
            problems.Add("проверка: " + e.Message);
        }

        Proto = proto;
        Problems = problems;
        Revision++;
        Changed?.Invoke();
    }

    // ---------------- undo / redo ----------------

    /// <summary>Call before a change (once per drag).</summary>
    public void Checkpoint()
    {
        _undo.Push((YamlMappingNode) Clone(Node));
        _redo.Clear();
        if (_undo.Count > 300)
        {
            var keep = _undo.Take(300).Reverse().ToList();
            _undo.Clear();
            foreach (var k in keep)
                _undo.Push(k);
        }
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Push(Node);
        Node = _undo.Pop();
        Rebuild();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Push(Node);
        Node = _redo.Pop();
        Rebuild();
    }

    /// <summary>Replaces the whole scene (raw YAML tab).</summary>
    public void ReplaceNode(YamlMappingNode node)
    {
        Checkpoint();
        Node = node;
        if (node.Str("id") is { } id)
            Id = id;
        Rebuild();
    }

    // ---------------- saving ----------------

    public string SceneText(string nl = "\n")
        => "- " + Emit(Node, 2, false, nl);

    /// <summary>Writes the scene back to its file. Returns the path written.</summary>
    public string Save()
    {
        var nl = "\n";
        if (IsNew || !File.Exists(FilePath))
        {
            var exists = File.Exists(FilePath);
            var existing = exists ? File.ReadAllText(FilePath) : "";
            nl = existing.Contains("\r\n") ? "\r\n" : Environment.NewLine == "\r\n" && !exists ? "\r\n" : "\n";
            var sb = new StringBuilder(existing);
            if (!exists)
                sb.Append("# ProcAnim scenes made in the Abisfera animation editor (Tools/_CorvaxGoob/AnimEditor).").Append(nl).Append(nl);
            else if (!existing.EndsWith('\n'))
                sb.Append(nl).Append(nl);
            else
                sb.Append(nl);
            sb.Append(SceneText(nl)).Append(nl);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            WriteKeepingBom(FilePath, sb.ToString(), null);
        }
        else
        {
            var raw = File.ReadAllBytes(FilePath);
            var bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            var text = Encoding.UTF8.GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            var orig = FindScene(Parse(text), _savedId ?? Id) ?? throw new InvalidDataException($"в {FilePath} больше нет сцены {_savedId ?? Id}");
            var result = ApplyDiff(text, orig, Node);
            // Safety net: the result must parse back to exactly the edited tree.
            var check = FindScene(Parse(result), Id);
            if (check == null || !DeepEquals(check, Node))
                throw new InvalidDataException("внутренняя ошибка сохранения: результат не совпал с редактируемой сценой, файл не изменён");
            WriteKeepingBom(FilePath, result, bom);
        }

        IsNew = false;
        _saved = (YamlMappingNode) Clone(Node);
        _savedId = Id;
        Changed?.Invoke();
        return FilePath;
    }

    private string? _savedId;

    private static void WriteKeepingBom(string path, string text, bool? bom)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(bom ?? false));
        File.Move(tmp, path, true);
    }

    /// <summary>Copy under a new id into another file (or the same one).</summary>
    public SceneDocument SaveAsNew(string file, string newId)
    {
        var node = (YamlMappingNode) Clone(Node);
        node.Set("id", S(newId));
        var doc = FromNode(file, newId, node);
        doc.Save();
        return doc;
    }
}
