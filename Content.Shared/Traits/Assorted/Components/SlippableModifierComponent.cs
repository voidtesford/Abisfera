// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared.Traits.Assorted.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlippableModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float ParalyzeTimeMultiplier = 1f;
}
