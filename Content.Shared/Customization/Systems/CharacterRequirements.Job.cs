// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Customization.Systems;

/// <summary>The selected job must be one of the listed jobs.</summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterJobRequirement : CharacterRequirement
{
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs = new();

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
        foreach (var id in Jobs)
        {
            if (!prototypeManager.TryIndex(id, out var jobProto))
                continue;
            names.Add(Loc.GetString(jobProto.Name));
        }

        reason = Loc.GetString("character-job-requirement", ("inverted", Inverted), ("jobs", string.Join(", ", names)));
        // No job selected yet (lobby). Return a raw value that stays valid after Invert is applied.
        if (job is null)
            return !Inverted;

        return Jobs.Contains(job.ID);
    }
}

/// <summary>The selected job must belong to one of the listed departments.</summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterDepartmentRequirement : CharacterRequirement
{
    [DataField(required: true)]
    public List<ProtoId<DepartmentPrototype>> Departments = new();

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
        foreach (var id in Departments)
        {
            if (!prototypeManager.TryIndex(id, out var dept))
                continue;
            names.Add(Loc.GetString(dept.Name));
        }

        reason = Loc.GetString("character-department-requirement",
            ("inverted", Inverted),
            ("departments", string.Join(", ", names)));

        // No job selected yet (lobby). Return a raw value that stays valid after Invert is applied.
        if (job is null)
            return !Inverted;

        return Departments.Any(d =>
            prototypeManager.TryIndex(d, out var dept) && dept.Roles.Contains(job.ID));
    }
}
