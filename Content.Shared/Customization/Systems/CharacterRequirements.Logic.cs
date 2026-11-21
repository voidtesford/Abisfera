// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Customization.Systems;

[ImplicitDataDefinitionForInheritors, MeansImplicitUse]
[Serializable, NetSerializable]
public abstract partial class CharacterLogicRequirement : CharacterRequirement
{
    [DataField]
    public List<CharacterRequirement> Requirements { get; private set; } = new();
}

[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterLogicAndRequirement : CharacterLogicRequirement
{
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
        var ok = CharacterRequirementsSystem.CheckRequirementsValid(Requirements, job, profile, playTimes, whitelisted, prototype,
            entityManager, prototypeManager, configManager, out var reasons, depth + 1, mind);
        reason = Format(reasons, "character-logic-and-requirement", "character-logic-and-requirement-listprefix", depth);
        return ok;
    }

    private string? Format(List<string> reasons, string key, string prefixKey, int depth)
    {
        if (reasons.Count == 0)
            return null;
        var builder = new StringBuilder();
        foreach (var message in reasons)
            builder.Append(Loc.GetString(prefixKey, ("indent", new string(' ', depth * 2))) + message);
        return Loc.GetString(key, ("inverted", Inverted), ("options", builder.ToString()));
    }
}

[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterLogicOrRequirement : CharacterLogicRequirement
{
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
        var ok = false;
        var reasons = new List<string>();
        foreach (var requirement in Requirements)
        {
            if (CharacterRequirementsSystem.CheckRequirementValid(requirement, job, profile, playTimes, whitelisted, prototype,
                    entityManager, prototypeManager, configManager, out var why, depth + 1))
            {
                ok = true;
                break;
            }

            if (why != null)
                reasons.Add(why);
        }

        if (reasons.Count == 0)
        {
            reason = null;
            return ok;
        }

        var builder = new StringBuilder();
        foreach (var message in reasons)
            builder.Append(Loc.GetString("character-logic-or-requirement-listprefix",
                ("indent", new string(' ', depth * 2))) + message);
        reason = Loc.GetString("character-logic-or-requirement", ("inverted", Inverted), ("options", builder.ToString()));
        return ok;
    }
}

[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterLogicXorRequirement : CharacterLogicRequirement
{
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
        var hits = 0;
        var reasons = new List<string>();
        foreach (var requirement in Requirements)
        {
            if (CharacterRequirementsSystem.CheckRequirementValid(requirement, job, profile, playTimes, whitelisted, prototype,
                    entityManager, prototypeManager, configManager, out var why, depth + 1))
                hits++;
            else if (why != null)
                reasons.Add(why);
        }

        var builder = new StringBuilder();
        foreach (var message in reasons)
            builder.Append(Loc.GetString("character-logic-xor-requirement-listprefix",
                ("indent", new string(' ', depth * 2))) + message);
        reason = reasons.Count == 0
            ? null
            : Loc.GetString("character-logic-xor-requirement", ("inverted", Inverted), ("options", builder.ToString()));
        return hits == 1;
    }
}
