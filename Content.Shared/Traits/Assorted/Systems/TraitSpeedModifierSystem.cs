// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Traits.Assorted.Components;

namespace Content.Shared.Traits.Assorted.Systems;

public sealed class TraitSpeedModifierSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TraitSpeedModifierComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TraitSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, TraitSpeedModifierComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.WalkModifier, component.SprintModifier, bypassImmunity: true);
    }

    private void OnStartup(EntityUid uid, TraitSpeedModifierComponent component, ComponentStartup args)
    {
        if (!HasComp<MovementSpeedModifierComponent>(uid))
            return;
        _movement.RefreshMovementSpeedModifiers(uid);
    }
}
