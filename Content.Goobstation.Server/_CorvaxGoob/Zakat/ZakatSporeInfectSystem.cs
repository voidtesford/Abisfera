using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Components;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// upd1: the spores infect whoever breathes them (see <see cref="ZakatSporeInfect"/>).
/// </summary>
public sealed partial class ZakatSporeInfectSystem : EntityEffectSystem<MobStateComponent, ZakatSporeInfect>
{
    [Dependency] private ZakatContagionSystem _contagion = default!;

    protected override void Effect(Entity<MobStateComponent> entity, ref EntityEffectEvent<ZakatSporeInfect> args)
    {
        _contagion.TrySporeInfect(entity, args.Effect.Chance * args.Scale);
    }
}
