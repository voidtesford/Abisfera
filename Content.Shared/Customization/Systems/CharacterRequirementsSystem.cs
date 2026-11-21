// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Customization.Systems;

/// <summary>Validates lists of <see cref="CharacterRequirement"/> against a profile (and optional job).</summary>
public sealed class CharacterRequirementsSystem : EntitySystem
{
    public static bool CheckRequirementValid(
        CharacterRequirement requirement,
        JobPrototype? job,
        HumanoidCharacterProfile profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        out string? reason,
        int depth = 0)
    {
        var valid = requirement.IsValid(job, profile, playTimes, whitelisted, prototype,
            entityManager, prototypeManager, configManager, out reason, depth);
        return requirement.Inverted ? !valid : valid;
    }

    public static bool CheckRequirementsValid(
        List<CharacterRequirement> requirements,
        JobPrototype? job,
        HumanoidCharacterProfile profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        out List<string> reasons,
        int depth = 0,
        MindComponent? mind = null)
    {
        reasons = new List<string>();
        var valid = true;

        foreach (var requirement in requirements)
        {
            var ok = requirement.IsValid(job, profile, playTimes, whitelisted, prototype,
                entityManager, prototypeManager, configManager, out var reason, depth, mind);
            ok = requirement.Inverted ? !ok : ok;
            if (!ok)
                valid = false;
            if (reason != null)
                reasons.Add(reason);
        }

        return valid;
    }

    public static FormattedMessage GetRequirementsText(List<string> reasons)
    {
        return FormattedMessage.FromMarkupOrThrow(GetRequirementsMarkup(reasons));
    }

    public static string GetRequirementsMarkup(List<string> reasons)
    {
        var text = new StringBuilder();
        foreach (var reason in reasons)
        {
            if (!string.IsNullOrEmpty(reason))
                text.Append('\n').Append(reason);
        }
        return text.ToString().Trim();
    }
}
