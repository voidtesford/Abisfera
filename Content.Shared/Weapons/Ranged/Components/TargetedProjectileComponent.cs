// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Optional target for a projectile. Null = hits everything.
/// Networked manually so deleted targets do not crash PVS (n0D / missing MetaData).
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedGunSystem), typeof(TargetedProjectileSystem))]
public sealed partial class TargetedProjectileComponent : Component
{
    [DataField]
    public EntityUid? Target;
}

[Serializable, NetSerializable]
public sealed class TargetedProjectileComponentState : ComponentState
{
    public NetEntity Target;
}
