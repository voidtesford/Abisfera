using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Robust.Shared.Serialization.Manager.Attributes;
using YamlDotNet.RepresentationModel;

namespace Abisfera.AnimEditor.Model;

/// <summary>
/// Reads a YAML node into the game's own data classes the way the engine serializer does for the field kinds ProcAnim
/// uses: <c>[DataField]</c> members (tag = explicit or the camelCased member name), numbers, bools, strings, enums
/// (case-insensitive), <c>"x, y"</c> vectors, hex colours, lists, dictionaries and nested data definitions. Types the
/// editor does not evaluate (sounds, damage) are skipped; their YAML is kept untouched by the node model.
/// </summary>
public sealed class NodeMapper
{
    public readonly List<string> Problems = new();

    private static readonly Dictionary<Type, List<(string Tag, MemberInfo Member, Type Type)>> FieldCache = new();

    public T Read<T>(YamlNode node) where T : new()
    {
        var obj = new T();
        Fill(obj, node, typeof(T).Name);
        return obj;
    }

    public static List<(string Tag, MemberInfo Member, Type Type)> Fields(Type type)
    {
        lock (FieldCache)
        {
            if (FieldCache.TryGetValue(type, out var list))
                return list;
            list = new();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var m in t.GetMembers(flags))
                {
                    var attr = m.GetCustomAttribute<DataFieldAttribute>();
                    if (attr == null)
                        continue;
                    var mt = m switch
                    {
                        FieldInfo f => f.FieldType,
                        PropertyInfo p => p.PropertyType,
                        _ => null,
                    };
                    if (mt == null)
                        continue;
                    var tag = attr.Tag ?? char.ToLowerInvariant(m.Name[0]) + m.Name[1..];
                    list.Add((tag, m, mt));
                }
            }

            FieldCache[type] = list;
            return list;
        }
    }

    private void Fill(object obj, YamlNode node, string path)
    {
        if (node is not YamlMappingNode map)
        {
            Problems.Add($"{path}: ожидался словарь");
            return;
        }

        var fields = Fields(obj.GetType());
        foreach (var (k, v) in map.Children)
        {
            if (k is not YamlScalarNode ks)
                continue;
            var key = ks.Value ?? "";
            if (key == "type" && obj is Robust.Shared.Prototypes.IPrototype)
                continue;
            var field = fields.FirstOrDefault(f => f.Tag == key);
            if (field.Member == null)
            {
                Problems.Add($"{path}.{key}: неизвестное поле");
                continue;
            }

            try
            {
                var value = Convert(v, field.Type, $"{path}.{key}");
                if (value == Skip)
                    continue;
                switch (field.Member)
                {
                    case FieldInfo f:
                        f.SetValue(obj, value);
                        break;
                    case PropertyInfo p:
                        (p.SetMethod ?? p.DeclaringType!.GetProperty(p.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetMethod)
                            ?.Invoke(obj, new[] { value });
                        break;
                }
            }
            catch (Exception e)
            {
                Problems.Add($"{path}.{key}: {e.Message}");
            }
        }
    }

    private static readonly object Skip = new();

    private object? Convert(YamlNode node, Type type, string path)
    {
        var under = Nullable.GetUnderlyingType(type);
        if (under != null)
        {
            if (node is YamlScalarNode { Value: "null" or "~" or "" })
                return null;
            return Convert(node, under, path);
        }

        if (type == typeof(Robust.Shared.Audio.SoundSpecifier) || type == typeof(Content.Shared.Damage.DamageSpecifier))
            return Skip;

        if (node is YamlScalarNode s)
        {
            var v = s.Value ?? "";
            if (type == typeof(string))
                return v;
            if (type == typeof(float))
                return float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (type == typeof(double))
                return double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (type == typeof(int))
                return int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (type == typeof(bool))
                return v.Trim().ToLowerInvariant() switch
                {
                    "true" or "yes" or "on" => true,
                    "false" or "no" or "off" => false,
                    _ => throw new FormatException($"не bool: '{v}'"),
                };
            if (type.IsEnum)
                return ParseEnum(type, v);
            if (type == typeof(Vector2))
                return ParseVector(v);
            if (type == typeof(Color))
            {
                if (Color.TryFromHex(v, out var c) || Color.TryFromName(v, out c))
                    return c;
                throw new FormatException($"не цвет: '{v}'");
            }

            if (type == typeof(Robust.Shared.Utility.ResPath))
                return new Robust.Shared.Utility.ResPath(v);
            if (type == typeof(LocId))
                return new LocId(v);
            if (type == typeof(Robust.Shared.Prototypes.EntProtoId))
                return new Robust.Shared.Prototypes.EntProtoId(v);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Robust.Shared.Prototypes.ProtoId<>))
                return Activator.CreateInstance(type, v);
            if (v is "null" or "~")
                return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var et = type.GetGenericArguments()[0];
            var list = (IList) Activator.CreateInstance(type)!;
            if (node is not YamlSequenceNode q)
                throw new FormatException("ожидался список");
            for (var i = 0; i < q.Children.Count; i++)
            {
                var item = Convert(q.Children[i], et, $"{path}[{i}]");
                if (item != Skip)
                    list.Add(item);
            }

            return list;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var args = type.GetGenericArguments();
            var dict = (IDictionary) Activator.CreateInstance(type)!;
            if (node is not YamlMappingNode m)
                throw new FormatException("ожидался словарь");
            foreach (var (k, v) in m.Children)
            {
                var key = Convert(k, args[0], path);
                var val = Convert(v, args[1], $"{path}.{(k as YamlScalarNode)?.Value}");
                if (key != null && val != Skip)
                    dict[key] = val;
            }

            return dict;
        }

        if (type.GetCustomAttribute<DataDefinitionAttribute>() != null || type.GetCustomAttribute<Robust.Shared.Prototypes.PrototypeAttribute>() != null)
        {
            var obj = Activator.CreateInstance(type, nonPublic: true)!;
            Fill(obj, node, path);
            return obj;
        }

        throw new FormatException($"тип {type.Name} не поддерживается редактором");
    }

    public static object ParseEnum(Type type, string v)
    {
        if (Enum.TryParse(type, v.Trim(), true, out var e))
            return e!;
        throw new FormatException($"нет значения '{v}' в {type.Name}");
    }

    public static Vector2 ParseVector(string v)
    {
        var parts = v.Split(',');
        if (parts.Length != 2)
            throw new FormatException($"ожидалось 'x, y': '{v}'");
        return new Vector2(float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture), float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture));
    }
}
