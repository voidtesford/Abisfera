// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Lavaland.Procedural.Prototypes;

/// <summary>
/// Contains information about Lavaland ruin configuration.
/// </summary>
[Prototype]
public sealed partial class LavalandGridRuinPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField] public LocId Name = "lavaland-ruin-unknown";

    [DataField(required: true)]
    public ResPath Path;

    [DataField]
    public int SpawnAttempts = 8;

    /// <summary>
    /// Overrides the pool's MinDistance for this specific ruin.
    /// </summary>
    [DataField]
    public int? MinDistance;

    /// <summary>
    /// Overrides the pool's MaxDistance for this specific ruin.
    /// </summary>
    [DataField]
    public int? MaxDistance;

    [DataField]
    public bool PatchToPlanet = true;

    [DataField(required: true)]
    public int Priority = int.MinValue;

    /// <summary>
    /// List of components to grant to entities that enter the ruin.
    /// </summary>
    [DataField]
    public ComponentRegistry ComponentsToGrant = new();

    // Corvax-Goob-start: lavaland ruin performance
    /// <summary>
    /// Optional spawn-limit group. The ruin pool can cap how many ruins of one group
    /// spawn per round (see <see cref="LavalandRuinPoolPrototype.GroupLimits"/>).
    /// </summary>
    [DataField]
    public string? LimitGroup;

    /// <summary>
    /// If true, NPCs on this ruin grid (non-patched ruins only) are put to sleep while no player is nearby.
    /// </summary>
    [DataField]
    public bool NpcSleep = true;

    /// <summary>
    /// If true, tiles of this ruin grid (non-patched ruins only) that are open to the outside get the planet
    /// atmosphere before the first atmos tick, instead of the station air saved in the map.
    /// </summary>
    [DataField]
    public bool PrefillPlanetAtmosphere = true;
    // Corvax-Goob-end
}
