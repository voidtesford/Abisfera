using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Starlight.Effects;

public sealed class ElectricSparksOnHitSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        // MeleeHitEvent вызывается на ОРУЖИИ, поэтому подписываемся вместе с его компонентом.
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, MeleeWeaponComponent component, MeleeHitEvent args)
    {
        if (args.HitEntities == null)
            return;

        // Ищем компонент питания по имени, чтобы не зависеть от того,
        // как класс называется и где лежит в этой сборке (Shared/Server).
        if (!_componentFactory.TryGetRegistration("ApcPowerReceiver", out var powerReg))
            return;

        foreach (var target in args.HitEntities)
        {
            if (Deleted(target) || !EntityManager.HasComponent(target, powerReg.Type))
                continue;

            Spawn("EffectElectricHitSparks", Transform(target).Coordinates);
        }
    }
}