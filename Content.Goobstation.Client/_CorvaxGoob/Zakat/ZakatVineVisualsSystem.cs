using Content.Client.IconSmoothing;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat;

/// <summary>
/// The vines are one periodic texture of <see cref="Period"/> x <see cref="Period"/> tiles drawn through IconSmooth corners:
/// each tile picks its own cell by position, so neighbouring vine tiles join seamlessly.
/// </summary>
public sealed partial class ZakatVineVisualsSystem : EntitySystem
{
    public const int Period = 4;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZakatVineComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<ZakatVineComponent> ent, ref ComponentInit args)
    {
        // before IconSmooth builds its corner layers: base "v0a" -> "v13a"
        if (!TryComp<IconSmoothComponent>(ent, out var smooth))
            return;

        var pos = Transform(ent).LocalPosition;
        var x = Mod((int) MathF.Floor(pos.X), Period);
        var y = Mod(-(int) MathF.Floor(pos.Y), Period); // the texture runs top to bottom
        smooth.StateBase = $"v{x + y * Period}a";
    }

    private static int Mod(int a, int b) => ((a % b) + b) % b;
}
