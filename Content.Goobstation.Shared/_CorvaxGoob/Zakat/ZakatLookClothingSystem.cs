using Content.Shared.Inventory.Events;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// The praetorian's copied clothes are part of its flesh: nobody takes them off.
/// </summary>
public sealed class ZakatLookClothingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZakatLookClothingComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
    }

    private void OnUnequipAttempt(EntityUid uid, ZakatLookClothingComponent comp, BeingUnequippedAttemptEvent args)
    {
        args.Cancel();
    }
}
