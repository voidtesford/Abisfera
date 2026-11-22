using Content.Shared.Power.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._Starlight.Effects;

public sealed class ElectricSparksOnHitSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(MeleeHitEvent args)
    {
        // Fully defensive — never crash melee
        try
        {
            if (args.HitEntities == null || args.HitEntities.Count == 0)
                return;

            foreach (var target in args.HitEntities)
            {
                if (!target.IsValid())
                    continue;

                if (!HasComp<SharedApcPowerReceiverComponent>(target))
                    continue;

                var xform = Transform(target);
                if (xform?.Coordinates is not { } coords || !coords.IsValid(EntityManager))
                    continue;

                Spawn("EffectSparks", coords);
            }
        }
        catch
        {
            // swallow — sparks are cosmetic
        }
    }
}
