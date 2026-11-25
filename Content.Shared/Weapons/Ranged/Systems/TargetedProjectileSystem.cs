// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Systems;

/// <summary>
/// Safe networking for <see cref="TargetedProjectileComponent"/>.
/// Avoids GetNetEntity on deleted entities (MetaData missing / n0D).
/// </summary>
public sealed class TargetedProjectileSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TargetedProjectileComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<TargetedProjectileComponent, ComponentHandleState>(OnHandleState);
    }

    private void OnGetState(EntityUid uid, TargetedProjectileComponent component, ref ComponentGetState args)
    {
        var net = NetEntity.Invalid;
        if (component.Target is { Valid: true } target
            && !TerminatingOrDeleted(target)
            && EntityManager.EntityExists(target))
        {
            net = GetNetEntity(target);
        }
        else if (component.Target != null)
        {
            // Stale reference — clear so we do not keep trying to network it.
            component.Target = null;
        }

        args.State = new TargetedProjectileComponentState { Target = net };
    }

    private void OnHandleState(EntityUid uid, TargetedProjectileComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not TargetedProjectileComponentState state)
            return;

        if (state.Target == NetEntity.Invalid)
        {
            component.Target = null;
            return;
        }

        component.Target = TryGetEntity(state.Target, out var entity) ? entity : null;
    }
}
