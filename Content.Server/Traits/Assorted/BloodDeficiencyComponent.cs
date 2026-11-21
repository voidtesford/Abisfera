// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server.Traits.Assorted;

[RegisterComponent]
public sealed partial class BloodDeficiencyComponent : Component
{
    [DataField(required: true)]
    public float BloodLossPercentage;

    [DataField]
    public bool Active = true;
}
