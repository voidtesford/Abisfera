// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Traits;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Customization.Systems;

/// <summary>The profile species must be one of the listed species.</summary>
[UsedImplicitly, Serializable, NetSerializable]
public sealed partial class CharacterSpeciesRequirement : CharacterRequirement
{
    [DataField(required: true)]
    public List<ProtoId<SpeciesPrototype>> Species = new();

    public override bool IsValid(
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
        MindComponent? mind = null)
    {
        var names = Species
            .Where(s => prototypeManager.TryIndex(s, out _))
            .Select(s => Loc.GetString(prototypeManager.Index(s).Name));
        reason = Loc.GetString("character-species-requirement",
            ("inverted", Inverted),
            ("species", string.Join(", ", names)));
        return Species.Contains(profile.Species);
    }
}

/// <summary>The profile must have one of the listed traits selected.</summary>
[UsedImplicitly, Serializable, NetSerializable]
public sealed partial class CharacterTraitRequirement : CharacterRequirement
{
    [DataField(required: true)]
    public List<ProtoId<TraitPrototype>> Traits = new();

    public override bool IsValid(
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
        MindComponent? mind = null)
    {
        var names = new List<string>();
        foreach (var id in Traits)
        {
            if (prototypeManager.TryIndex(id, out var trait))
                names.Add(TraitPrototype.GetName(trait));
            else
                names.Add(id.ToString());
        }

        reason = Loc.GetString("character-trait-requirement",
            ("inverted", Inverted),
            ("traits", string.Join(", ", names)));
        return Traits.Any(t => profile.TraitPreferences.Contains(t));
    }
}
