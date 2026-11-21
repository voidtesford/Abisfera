// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Traits.Assorted.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SelfAwareComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public HashSet<ProtoId<DamageTypePrototype>> AnalyzableTypes = new();

    [DataField(required: true), AutoNetworkedField]
    public HashSet<ProtoId<DamageGroupPrototype>> DetectableGroups = new();

    public List<FixedPoint2> Thresholds = new()
    {
        FixedPoint2.New(0.10),
        FixedPoint2.New(0.25),
        FixedPoint2.New(0.40),
        FixedPoint2.New(0.60),
    };
}
