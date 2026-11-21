// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared.Traits.Assorted.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TraitSpeedModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkModifier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintModifier = 1f;

    [DataField, AutoNetworkedField]
    public float RequiredTriggeredSpeedModifier = 1f;
}
