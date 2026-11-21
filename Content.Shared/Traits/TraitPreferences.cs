// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Customization.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared.Traits;

/// <summary>
/// Maps legacy trait IDs, checks requirements and silently trims a trait set so it fits the point and slot budget.
/// </summary>
public static class TraitPreferences
{
    /// <summary>Old Goob (or EE) IDs that we keep under a different prototype ID.</summary>
    public static readonly Dictionary<string, string> IdMigration = new(StringComparer.Ordinal)
    {
        // Keep our historical IDs as the canonical ones.
        ["Nearsighted"] = "PoorVision",
        ["Southern"] = "SouthernAccent",
        ["Stutter"] = "SocialAnxiety",
        ["british"] = "BritishAccent",
        ["British"] = "BritishAccent",
        ["SocialAnxietyTrait"] = "SocialAnxiety",
    };

    public static string MapId(string id)
    {
        return IdMigration.TryGetValue(id, out var mapped) ? mapped : id;
    }

    /// <summary>
    /// Maps IDs, drops unknown prototypes, drops traits that fail species/trait/(optional) job requirements,
    /// then silently removes traits until the set fits <see cref="CCVars.GameTraitsDefaultPoints"/> and
    /// <see cref="CCVars.GameTraitsMax"/>.
    /// </summary>
    public static HashSet<ProtoId<TraitPrototype>> MapAndTrim(
        IEnumerable<ProtoId<TraitPrototype>> traits,
        HumanoidCharacterProfile profile,
        IPrototypeManager prototypes,
        IConfigurationManager config,
        IEntityManager entityManager,
        JobPrototype? job = null)
    {
        if (!config.GetCVar(CCVars.GameTraitsEnabled))
            return new HashSet<ProtoId<TraitPrototype>>();

        var mapped = new List<ProtoId<TraitPrototype>>();
        var seen = new HashSet<string>();
        foreach (var raw in traits)
        {
            var id = MapId(raw.ToString());
            if (!seen.Add(id))
                continue;
            if (!prototypes.TryIndex<TraitPrototype>(id, out _))
                continue;
            mapped.Add(id);
        }

        // First-wins: keep a trait only if the whole trial set remains valid.
        // Conflicting accents in an old profile therefore collapse to a single valid one, not to none.
        var keptIds = new List<ProtoId<TraitPrototype>>();
        var playTimes = new Dictionary<string, TimeSpan>();
        foreach (var id in mapped)
        {
            var proto = prototypes.Index(id);
            if (proto.IncludedSpecies.Count > 0 && !proto.IncludedSpecies.Contains(profile.Species)
                || proto.ExcludedSpecies.Contains(profile.Species))
                continue;

            var trialIds = new List<ProtoId<TraitPrototype>>(keptIds) { id };
            var trialProfile = profile.WithTraitPreferences(trialIds);
            var ok = true;
            foreach (var trialId in trialIds)
            {
                var trialProto = prototypes.Index(trialId);
                if (trialProto.Requirements.Count > 0 &&
                    !CharacterRequirementsSystem.CheckRequirementsValid(
                        trialProto.Requirements, job, trialProfile, playTimes, true, trialProto,
                        entityManager, prototypes, config, out _))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                keptIds.Add(id);
        }

        var kept = keptIds.Select(i => prototypes.Index(i)).ToList();
        kept.Sort();
        TrimToBudget(kept, config);
        return kept.Select(t => (ProtoId<TraitPrototype>) t.ID).ToHashSet();
    }

    /// <summary>Drops traits until the remaining set has a non-negative point total and enough slots.</summary>
    public static void TrimToBudget(List<TraitPrototype> traits, IConfigurationManager config)
    {
        var points = config.GetCVar(CCVars.GameTraitsDefaultPoints);
        var slots = config.GetCVar(CCVars.GameTraitsMax);

        bool Legal()
        {
            var p = points;
            var s = slots;
            foreach (var t in traits)
            {
                p += t.Points;
                s -= t.Slots;
            }
            return p >= 0 && s >= 0;
        }

        while (traits.Count > 0 && !Legal())
        {
            // Prefer dropping the costliest advantage (most negative points). If the problem is only slots,
            // drop a free trait first, then the least valuable disadvantage (smallest positive points).
            var p = points;
            var s = slots;
            foreach (var t in traits)
            {
                p += t.Points;
                s -= t.Slots;
            }

            TraitPrototype drop;
            if (p < 0)
            {
                drop = traits.OrderBy(t => t.Points).ThenBy(t => t.ID).First();
            }
            else
            {
                var zero = traits.Where(t => t.Points == 0).OrderBy(t => t.ID).FirstOrDefault();
                drop = zero ?? traits.OrderBy(t => t.Points).ThenBy(t => t.ID).First();
            }

            traits.Remove(drop);
        }
    }

    public static int PointsRemaining(IEnumerable<TraitPrototype> traits, IConfigurationManager config)
    {
        var points = config.GetCVar(CCVars.GameTraitsDefaultPoints);
        foreach (var t in traits)
            points += t.Points;
        return points;
    }

    public static int SlotsRemaining(IEnumerable<TraitPrototype> traits, IConfigurationManager config)
    {
        var slots = config.GetCVar(CCVars.GameTraitsMax);
        foreach (var t in traits)
            slots -= t.Slots;
        return slots;
    }

    public static int PointsRemaining(
        IEnumerable<ProtoId<TraitPrototype>> ids,
        IPrototypeManager proto,
        IConfigurationManager config)
    {
        return PointsRemaining(Resolve(ids, proto), config);
    }

    public static int SlotsRemaining(
        IEnumerable<ProtoId<TraitPrototype>> ids,
        IPrototypeManager proto,
        IConfigurationManager config)
    {
        return SlotsRemaining(Resolve(ids, proto), config);
    }

    private static List<TraitPrototype> Resolve(IEnumerable<ProtoId<TraitPrototype>> ids, IPrototypeManager proto)
    {
        var list = new List<TraitPrototype>();
        foreach (var id in ids)
        {
            if (proto.TryIndex(id, out var trait))
                list.Add(trait);
        }
        return list;
    }
}
