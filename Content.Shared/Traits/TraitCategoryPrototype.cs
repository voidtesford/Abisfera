// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared.Traits;

/// <summary>A tab or group in the traits editor.</summary>
[Prototype]
public sealed partial class TraitCategoryPrototype : IPrototype
{
    public const string Default = "Default";

    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>Shown as a top-level tab when true.</summary>
    [DataField]
    public bool Root;

    [DataField]
    public List<ProtoId<TraitCategoryPrototype>> SubCategories = new();
}
