// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared.Traits.Assorted.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ConsumeDelayModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float FoodDelayMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float DrinkDelayMultiplier = 1f;
}
