// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server.Traits.Assorted;

[RegisterComponent, Access(typeof(KillOnDamageSystem))]
public sealed partial class KillOnDamageComponent : Component
{
    [DataField]
    public string DamageType = "Shock";

    [DataField]
    public float Threshold = 5.0f;

    [DataField]
    public LocId Popup = "fragileCircuits-kill-popup";
}
