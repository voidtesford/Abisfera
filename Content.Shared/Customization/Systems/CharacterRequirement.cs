// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Customization.Systems;

/// <summary>
/// A requirement a character must meet to take a trait (or other customization entry).
/// Inversion is applied by <see cref="CharacterRequirementsSystem"/>, not by <see cref="IsValid"/>.
/// </summary>
[ImplicitDataDefinitionForInheritors, MeansImplicitUse]
[Serializable, NetSerializable]
public abstract partial class CharacterRequirement
{
    [DataField]
    public bool Inverted;

    public abstract bool IsValid(
        JobPrototype? job,
        HumanoidCharacterProfile profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        out string? reason,
        int depth = 0,
        MindComponent? mind = null);
}
