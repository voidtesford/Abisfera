using System.Numerics;
using SkiaSharp;

namespace Abisfera.AnimEditor.Rendering;

/// <summary>2D affine map p' = (A x + C y + E, B x + D y + F) (column vectors, composes like the numpy preview).</summary>
public readonly record struct Affine(float A, float B, float C, float D, float E, float F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public static Affine Translate(float x, float y) => new(1, 0, 0, 1, x, y);

    public static Affine Scale(float sx, float sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Counter-clockwise in a y-up frame.</summary>
    public static Affine Rotate(float deg)
    {
        var (s, c) = MathF.SinCos(deg * MathF.PI / 180f);
        return new(c, s, -s, c, 0, 0);
    }

    public static Affine operator *(Affine m, Affine n) => new(
        m.A * n.A + m.C * n.B,
        m.B * n.A + m.D * n.B,
        m.A * n.C + m.C * n.D,
        m.B * n.C + m.D * n.D,
        m.A * n.E + m.C * n.F + m.E,
        m.B * n.E + m.D * n.F + m.F);

    public Vector2 Apply(Vector2 p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    public Affine Inverse()
    {
        var det = A * D - B * C;
        if (MathF.Abs(det) < 1e-9f)
            return Identity;
        var ia = D / det;
        var ib = -B / det;
        var ic = -C / det;
        var id = A / det;
        return new(ia, ib, ic, id, -(ia * E + ic * F), -(ib * E + id * F));
    }

    public SKMatrix ToSk() => new(A, C, E, B, D, F, 0, 0, 1);
}
