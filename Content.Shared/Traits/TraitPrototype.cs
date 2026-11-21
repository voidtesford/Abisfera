// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Customization.Systems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared.Traits;

/// <summary>Describes a trait in the points-based character editor.</summary>
[Prototype]
public sealed partial class TraitPrototype : IPrototype, IComparable
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Display name. Falls back to trait-name-{ID} when empty.</summary>
    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>Display description. Falls back to trait-description-{ID} when empty.</summary>
    [DataField]
    public LocId? Description { get; private set; }

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>Optional leftover from the old system; prefer <see cref="Functions"/>.</summary>
    [DataField]
    public ComponentRegistry Components { get; private set; } = new();

    [DataField(serverOnly: true)]
    public List<JobSpecial> Specials { get; private set; } = new();

    [DataField]
    public EntProtoId? TraitGear;

    /// <summary>
    /// Points this trait adds to the character budget. Negative spends points (an advantage),
    /// positive refunds them (a disadvantage). Zero is free but still uses slots.
    /// </summary>
    [DataField]
    public int Points;

    /// <summary>How many of the global trait slots this uses.</summary>
    [DataField]
    public int Slots = 1;

    [DataField]
    public List<CharacterRequirement> Requirements = new();

    [DataField(serverOnly: true)]
    public TraitFunction[] Functions { get; private set; } = Array.Empty<TraitFunction>();

    /// <summary>Lower values are applied earlier when the character spawns.</summary>
    [DataField]
    public int Priority = 0;

    [DataField]
    public ProtoId<TraitCategoryPrototype>? Category;

    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>> ExcludedSpecies = new();

    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>> IncludedSpecies = new();

    [DataField]
    public List<string>? LanguagesSpoken { get; private set; }

    [DataField]
    public List<string>? LanguagesUnderstood { get; private set; }

    [DataField]
    public List<string>? RemoveLanguagesSpoken { get; private set; }

    [DataField]
    public List<string>? RemoveLanguagesUnderstood { get; private set; }

    public int CompareTo(object? obj)
    {
        if (obj is not TraitPrototype other)
            return -1;
        var byPriority = Priority.CompareTo(other.Priority);
        return byPriority != 0 ? byPriority : string.CompareOrdinal(ID, other.ID);
    }

    public static string GetName(TraitPrototype trait)
    {
        if (!string.IsNullOrEmpty(trait.Name))
            return Loc.GetString(trait.Name);
        return Loc.GetString($"trait-name-{trait.ID}");
    }

    public static string? GetDescription(TraitPrototype trait)
    {
        if (trait.Description is { } desc && !string.IsNullOrEmpty(desc))
            return Loc.GetString(desc);
        var key = $"trait-description-{trait.ID}";
        var text = Loc.GetString(key);
        return text == key ? null : text;
    }
}

/// <summary>Hook that mutates a spawned player who took this trait.</summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class TraitFunction
{
    public abstract void OnPlayerSpawn(
        EntityUid mob,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager);
}
