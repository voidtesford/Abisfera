using Content.Shared._CorvaxGoob.ShogkkOperative;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffectNew;

namespace Content.Server._CorvaxGoob.ShogkkOperative;

/// <summary>
/// Removes the SSDIndicator from the operatives and the zombified (see <see cref="ShogkkNoSsdIndicatorComponent"/>).
/// SSDIndicatorSystem puts every uncontrolled human body into SSD sleep after ic.ssd_sleep_time, and that is what put
/// these NPCs to sleep on their own.
/// </summary>
public sealed class ShogkkNoSsdIndicatorSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShogkkNoSsdIndicatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShogkkNoSsdIndicatorComponent, BeforeStatusEffectAddedEvent>(OnBeforeStatusEffect);
    }

    // RT 293: the SSD sleep status also carries Stunned + Knockdown, so cancelling TryingToSleepEvent is not enough.
    private void OnBeforeStatusEffect(Entity<ShogkkNoSsdIndicatorComponent> ent, ref BeforeStatusEffectAddedEvent args)
    {
        if (args.Effect == SSDIndicatorSystem.StatusEffectSSDSleeping)
            args.Cancelled = true;
    }

    private void OnMapInit(Entity<ShogkkNoSsdIndicatorComponent> ent, ref MapInitEvent args)
    {
        RemCompDeferred<SSDIndicatorComponent>(ent);
    }
}
