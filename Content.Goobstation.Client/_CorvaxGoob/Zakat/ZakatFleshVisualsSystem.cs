using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat;

public enum ZakatVisualLayers : byte
{
    BleedingEyes,
    Growths,
    PraetorianClaws,
}

/// <summary>
/// Flesh overlays of «Закат»: blood from the eyes of a driven host, growths of «гнилые наросты».
/// </summary>
public sealed partial class ZakatFleshVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly ResPath Rsi = new("_CorvaxGoob/Zakat/flesh_overlays.rsi");
    private static readonly ResPath ClawsRsi = new("_CorvaxGoob/Zakat/praetorian.rsi");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatBleedingEyesComponent, ComponentStartup>((uid, _, _) => AddLayer(uid, ZakatVisualLayers.BleedingEyes, "bleeding_eyes"));
        SubscribeLocalEvent<ZakatBleedingEyesComponent, ComponentShutdown>((uid, _, _) => RemoveLayer(uid, ZakatVisualLayers.BleedingEyes));
        SubscribeLocalEvent<ZakatRotGrowthsComponent, ComponentStartup>((uid, _, _) => AddLayer(uid, ZakatVisualLayers.Growths, "growths"));
        SubscribeLocalEvent<ZakatRotGrowthsComponent, ComponentShutdown>((uid, _, _) => RemoveLayer(uid, ZakatVisualLayers.Growths));
        SubscribeLocalEvent<ZakatPraetorianClawsComponent, ComponentStartup>((uid, _, _) => AddLayer(uid, ZakatVisualLayers.PraetorianClaws, "claws", ClawsRsi));
        SubscribeLocalEvent<ZakatPraetorianClawsComponent, ComponentShutdown>((uid, _, _) => RemoveLayer(uid, ZakatVisualLayers.PraetorianClaws));
    }

    private void AddLayer(EntityUid uid, ZakatVisualLayers key, string state, ResPath? rsi = null)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || _sprite.LayerMapTryGet((uid, sprite), key, out _, false))
            return;

        var layer = _sprite.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(rsi ?? Rsi, state));
        _sprite.LayerMapSet((uid, sprite), key, layer);
    }

    private void RemoveLayer(EntityUid uid, ZakatVisualLayers key)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || !_sprite.LayerMapTryGet((uid, sprite), key, out var layer, false))
            return;

        _sprite.RemoveLayer((uid, sprite), layer);
    }
}
