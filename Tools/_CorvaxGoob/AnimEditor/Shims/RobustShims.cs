// Minimal stand-ins for the engine / content types the linked ProcAnim sources mention, so the game's own evaluator
// (ProcAnimTimeline, ProcAnimFx) and puppet splitter (ProcAnimPuppet) compile into the editor unchanged.
// Only what those files need; behaviour matches the engine where the maths depends on it (Color, Angle).
global using System;
global using System.Collections.Generic;
global using Robust.Shared.Maths;
global using Robust.Shared.ViewVariables;
global using Robust.Shared.Serialization.Manager.Attributes;
global using Robust.Shared.Localization;

using System.Globalization;
using System.Numerics;

namespace Robust.Shared.Serialization.Manager.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DataFieldAttribute : Attribute
    {
        public readonly string? Tag;
        public bool Required { get; set; }
        public bool ReadOnly { get; set; }
        public bool ServerOnly { get; set; }
        public Type? CustomTypeSerializer { get; set; }
        public DataFieldAttribute(string? tag = null, bool readOnly = false, int priority = 1, bool required = false,
            bool serverOnly = false, Type? customTypeSerializer = null)
        {
            Tag = tag;
            Required = required;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class IdDataFieldAttribute : DataFieldAttribute
    {
        public IdDataFieldAttribute(int priority = 1, Type? customTypeSerializer = null) : base("id") { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class DataDefinitionAttribute : Attribute { }
}

namespace Robust.Shared.ViewVariables
{
    public enum VVAccess : byte { ReadOnly, ReadWrite }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ViewVariablesAttribute : Attribute
    {
        public ViewVariablesAttribute(VVAccess access = VVAccess.ReadOnly) { }
    }
}

namespace Robust.Shared.Serialization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
    public sealed class NetSerializableAttribute : Attribute { }
}

namespace Robust.Shared.Prototypes
{
    public interface IPrototype
    {
        string ID { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PrototypeAttribute : Attribute
    {
        public readonly string? Type;
        public PrototypeAttribute(string? type = null, int loadPriority = 1) => Type = type;
    }

    public readonly record struct EntProtoId(string Id)
    {
        public static implicit operator EntProtoId(string id) => new(id);
        public override string ToString() => Id;
    }

    public readonly record struct ProtoId<T>(string Id)
    {
        public static implicit operator ProtoId<T>(string id) => new(id);
        public override string ToString() => Id;
    }
}

namespace Robust.Shared.Utility
{
    public readonly record struct ResPath(string CanonPath)
    {
        public override string ToString() => CanonPath;
    }
}

namespace Robust.Shared.Audio
{
    /// <summary>Kept opaque: sounds are not played by the editor (the YAML text is preserved as written).</summary>
    public abstract class SoundSpecifier { }

    public sealed class SoundPathSpecifier : SoundSpecifier
    {
        public string Path = "";
    }

    public sealed class SoundCollectionSpecifier : SoundSpecifier
    {
        public string? Collection;
        public SoundCollectionSpecifier(string collection) => Collection = collection;
    }
}

namespace Robust.Shared.Localization
{
    public readonly record struct LocId(string Id)
    {
        public static implicit operator LocId(string id) => new(id);
        public override string ToString() => Id;
    }
}

namespace Content.Shared.Damage
{
    /// <summary>Kept opaque (damage is server-side; the YAML text is preserved as written).</summary>
    public sealed class DamageSpecifier
    {
        public Dictionary<string, float> DamageDict = new();
    }
}

namespace Content.Shared.Popups
{
    public enum PopupType : byte { Small, SmallCaution, Medium, MediumCaution, Large, LargeCaution }
}

namespace Content.Shared.Tag
{
    public sealed class TagPrototype { }
}

namespace Content.Shared._Shitmed.Targeting
{
    [Flags]
    public enum TargetBodyPart : ushort
    {
        Head = 1,
        Chest = 1 << 1,
        Groin = 1 << 2,
        LeftArm = 1 << 3,
        LeftHand = 1 << 4,
        RightArm = 1 << 5,
        RightHand = 1 << 6,
        LeftLeg = 1 << 7,
        LeftFoot = 1 << 8,
        RightLeg = 1 << 9,
        RightFoot = 1 << 10,
        Hands = LeftHand | RightHand,
        Arms = LeftArm | RightArm,
        Legs = LeftLeg | RightLeg,
        Feet = LeftFoot | RightFoot,
        FullArms = Arms | Hands,
        FullLegs = Feet | Legs,
        BodyMiddle = Chest | Groin | FullArms,
        FullLegsGroin = FullLegs | Groin,
        All = Head | Chest | Groin | LeftArm | LeftHand | RightArm | RightHand | LeftLeg | LeftFoot | RightLeg | RightFoot,
        Vital = Head | Chest | Groin,
    }
}

namespace Robust.Shared.Maths
{
    public enum Direction : sbyte
    {
        Invalid = -1,
        South = 0,
        SouthEast = 1,
        East = 2,
        NorthEast = 3,
        North = 4,
        NorthWest = 5,
        West = 6,
        SouthWest = 7,
    }

    /// <summary>Angle in radians (engine semantics: RotateVec is counter-clockwise).</summary>
    public readonly struct Angle
    {
        public readonly double Theta;
        public static readonly Angle Zero = new(0);
        public Angle(double theta) => Theta = theta;
        public static Angle FromDegrees(double deg) => new(deg * Math.PI / 180.0);
        public double Degrees => Theta * 180.0 / Math.PI;

        public Vector2 RotateVec(in Vector2 vec)
        {
            var (s, c) = Math.SinCos(Theta);
            return new Vector2((float) (c * vec.X - s * vec.Y), (float) (s * vec.X + c * vec.Y));
        }
    }

    /// <summary>Linear float RGBA colour, like Robust's (components 0..1; FromHex parses #rgb, #rgba, #rrggbb, #rrggbbaa).</summary>
    public readonly struct Color : IEquatable<Color>
    {
        public readonly float R, G, B, A;

        public Color(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public Color(byte r, byte g, byte b, byte a = 255) : this(r / 255f, g / 255f, b / 255f, a / 255f) { }

        public static readonly Color White = new(1f, 1f, 1f);
        public static readonly Color Black = new(0f, 0f, 0f);
        public static readonly Color Transparent = new(0f, 0f, 0f, 0f);

        public byte RByte => (byte) Math.Clamp(MathF.Round(R * 255f), 0, 255);
        public byte GByte => (byte) Math.Clamp(MathF.Round(G * 255f), 0, 255);
        public byte BByte => (byte) Math.Clamp(MathF.Round(B * 255f), 0, 255);
        public byte AByte => (byte) Math.Clamp(MathF.Round(A * 255f), 0, 255);

        public Color WithAlpha(float a) => new(R, G, B, a);

        public static Color FromHex(string hex, Color? fallback = null)
        {
            if (TryFromHex(hex, out var c))
                return c;
            if (fallback is { } f)
                return f;
            throw new ArgumentException($"bad colour '{hex}'");
        }

        public static bool TryFromHex(string? hex, out Color color)
        {
            color = White;
            if (string.IsNullOrWhiteSpace(hex))
                return false;
            var s = hex.Trim();
            if (s.StartsWith('#'))
                s = s[1..];
            if (s.Length is 3 or 4)
                s = string.Concat(System.Linq.Enumerable.Select(s, ch => new string(ch, 2)));
            if (s.Length is not (6 or 8))
                return false;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return false;
            if (s.Length == 6)
                v = (v << 8) | 0xFF;
            color = new Color((byte) (v >> 24), (byte) (v >> 16), (byte) (v >> 8), (byte) v);
            return true;
        }

        /// <summary>The handful of named colours prototypes use.</summary>
        public static bool TryFromName(string name, out Color color)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "white": color = White; return true;
                case "black": color = Black; return true;
                case "red": color = new Color(1f, 0f, 0f); return true;
                case "green": color = new Color(0f, 0.5f, 0f); return true;
                case "blue": color = new Color(0f, 0f, 1f); return true;
                case "yellow": color = new Color(1f, 1f, 0f); return true;
                case "gray": case "grey": color = new Color(0.5f, 0.5f, 0.5f); return true;
                case "transparent": color = Transparent; return true;
                default: color = White; return false;
            }
        }

        public string ToHex() => AByte == 255 ? $"#{RByte:x2}{GByte:x2}{BByte:x2}" : $"#{RByte:x2}{GByte:x2}{BByte:x2}{AByte:x2}";
        public bool Equals(Color o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object? obj) => obj is Color c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    }
}
