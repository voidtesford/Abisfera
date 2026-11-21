using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared.Alert.Components;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat;

/// <summary>
/// Biomass counter on the alert bar.
/// </summary>
public sealed class ZakatAlertSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZakatComponent, GetGenericAlertCounterAmountEvent>(OnGetCounter);
    }

    private void OnGetCounter(Entity<ZakatComponent> ent, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled || args.Alert.ID != ent.Comp.BiomassAlert.Id)
            return;

        args.Amount = Math.Clamp((int) MathF.Floor(ent.Comp.Biomass), 0, 999);
    }
}
