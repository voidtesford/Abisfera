// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared.Traits.Assorted.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class DeadModifierComponent : Component
{
    [DataField]
    public int DeadThresholdModifier;
}
