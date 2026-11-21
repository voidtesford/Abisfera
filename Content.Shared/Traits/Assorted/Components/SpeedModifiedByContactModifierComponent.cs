// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared.Traits.Assorted.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SpeedModifiedByContactModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkModifierEffectiveness = 1f;

    [DataField, AutoNetworkedField]
    public float SprintModifierEffectiveness = 1f;
}
