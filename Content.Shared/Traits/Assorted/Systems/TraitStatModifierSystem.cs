// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted.Components;

namespace Content.Shared.Traits.Assorted.Systems;

public sealed class TraitStatModifierSystem : EntitySystem
{
    [Dependency] private readonly MobThresholdSystem _threshold = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CritModifierComponent, ComponentStartup>(OnCritStartup);
        SubscribeLocalEvent<DeadModifierComponent, ComponentStartup>(OnDeadStartup);
        SubscribeLocalEvent<StaminaCritModifierComponent, ComponentStartup>(OnStaminaCritStartup);
    }

    private void OnCritStartup(EntityUid uid, CritModifierComponent component, ComponentStartup args)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out _))
            return;
        if (!_threshold.TryGetThresholdForState(uid, MobState.Critical, out var crit) || crit == 0)
            return;
        _threshold.SetMobStateThreshold(uid, crit.Value + component.CritThresholdModifier, MobState.Critical);
    }

    private void OnDeadStartup(EntityUid uid, DeadModifierComponent component, ComponentStartup args)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out _))
            return;
        if (!_threshold.TryGetThresholdForState(uid, MobState.Dead, out var dead) || dead == 0)
            return;
        _threshold.SetMobStateThreshold(uid, dead.Value + component.DeadThresholdModifier, MobState.Dead);
    }

    private void OnStaminaCritStartup(EntityUid uid, StaminaCritModifierComponent component, ComponentStartup args)
    {
        if (!TryComp<StaminaComponent>(uid, out var stamina))
            return;
        stamina.CritThreshold += component.CritThresholdModifier;
        stamina.BaseCritThreshold += component.CritThresholdModifier;
        Dirty(uid, stamina);
    }
}
