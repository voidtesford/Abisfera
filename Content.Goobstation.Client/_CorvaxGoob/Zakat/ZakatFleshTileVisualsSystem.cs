using Content.Client.IconSmoothing;
using Content.Client._Shitcode.Heretic;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat;

public enum ZakatFleshTileLayers : byte
{
    Detail,
}

/// <summary>
/// Captured flesh: one of three seamless variants per tile, a random growth on top (pustules, an eye, a maw...),
/// and a dead grey-brown look once the core is gone.
/// </summary>
public sealed partial class ZakatFleshTileVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    /// <summary>
    /// The flesh texture repeats every <see cref="Period"/> x <see cref="Period"/> tiles; each tile shows its own cell.
    /// </summary>
    public const int Period = 3;

    private static readonly string[] FloorDetails = { "detail_pustules", "detail_tumor", "detail_maw", "detail_tendrils", "detail_ribs", "detail_eye" };
    private static readonly string[] WallDetails = { "wdetail_tumors", "wdetail_drips" };

    private static readonly Color DeadColor = Color.FromHex("#8a7470");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZakatFleshTileComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ZakatFleshTileComponent, IconSmoothCornersInitializedEvent>(OnCorners);
        SubscribeLocalEvent<ZakatFleshTileComponent, AppearanceChangeEvent>(OnAppearance);
    }

    /// <summary>
    /// Stable per tile: every client sees the same variant and growth.
    /// </summary>
    private int Hash(EntityUid uid)
    {
        var id = GetNetEntity(uid).Id;
        unchecked
        {
            var h = id * 2654435761u;
            return (int) ((h >> 8) & 0x7fffffff);
        }
    }

    private void OnInit(Entity<ZakatFleshTileComponent> ent, ref ComponentInit args)
    {
        // before IconSmooth builds its corner layers: pick the cell of the texture by the tile (base "f0a" -> "f4a")
        if (!TryComp<IconSmoothComponent>(ent, out var smooth) || smooth.StateBase.Length != 3)
            return;

        var pos = Transform(ent).LocalPosition;
        var x = Mod((int) MathF.Floor(pos.X), Period);
        var y = Mod(-(int) MathF.Floor(pos.Y), Period); // the texture runs top to bottom
        smooth.StateBase = $"{smooth.StateBase[0]}{x + y * Period}{smooth.StateBase[2]}";
    }

    private static int Mod(int a, int b) => ((a % b) + b) % b;

    private void OnCorners(Entity<ZakatFleshTileComponent> ent, ref IconSmoothCornersInitializedEvent args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // the growth always sits on top of the four corner layers
        if (_sprite.LayerMapTryGet((ent, sprite), ZakatFleshTileLayers.Detail, out var old, false))
            _sprite.RemoveLayer((ent, sprite), old);

        var h = Hash(ent);
        var details = ent.Comp.Wall ? WallDetails : FloorDetails;
        // about two thirds of the tiles stay plain
        var pick = h % (details.Length * 3 + 1);
        if (pick >= details.Length)
            return;

        var layer = _sprite.AddLayer((ent, sprite), new SpriteSpecifier.Rsi(new ResPath("_CorvaxGoob/Zakat/flesh.rsi"), details[pick]));
        _sprite.LayerMapSet((ent, sprite), ZakatFleshTileLayers.Detail, layer);
        UpdateActive(ent, sprite);
    }

    private void OnAppearance(Entity<ZakatFleshTileComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is { } sprite)
            UpdateActive(ent, sprite);
    }

    private void UpdateActive(Entity<ZakatFleshTileComponent> ent, SpriteComponent sprite)
    {
        var active = !_appearance.TryGetData<bool>(ent, ZakatFleshVisuals.Active, out var value) || value;
        _sprite.SetColor((ent, sprite), active ? Color.White : DeadColor);
        // dead flesh stops pulsing
        foreach (var layer in sprite.AllLayers)
        {
            if (layer is SpriteComponent.Layer l)
                _sprite.LayerSetAutoAnimated(l, active);
        }
    }
}
