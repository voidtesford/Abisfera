// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Customization.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared.Traits.Assorted.Components;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class DescriptionExtension
{
    [DataField]
    public List<CharacterRequirement>? Requirements;

    [DataField]
    public string Description = "";

    [DataField]
    public string RequirementsNotMetDescription = "";

    [DataField]
    public int FontSize = 12;

    [DataField]
    public string Color = "#ffffff";

    [DataField]
    public bool RequireDetailRange = true;
}

[RegisterComponent]
public sealed partial class ExtendDescriptionComponent : Component
{
    [DataField]
    public List<DescriptionExtension> DescriptionList = new();
}
