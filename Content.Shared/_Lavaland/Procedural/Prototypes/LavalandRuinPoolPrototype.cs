// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared._Lavaland.Procedural.Prototypes;

[Prototype]
public sealed partial class LavalandRuinPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>
    /// Distance in-between ruins.
    /// </summary>
    [DataField]
    public int RuinDistance = 24;

    /// <summary>
    /// Max distance that Ruins can generate.
    /// </summary>
    [DataField]
    public int MaxDistance = 256;

    /// <summary>
    /// Min distance that Ruins can generate.
    /// </summary>
    [DataField]
    public int MinDistance;

    /// <summary>
    /// List of all grid ruins and their count.
    /// Used for ruins that are loaded as proper grids.
    /// </summary>
    // Corvax-Goob-start: lavaland ruin performance
    /// <summary>
    /// Max number of grid ruins per <see cref="LavalandGridRuinPrototype.LimitGroup"/> that may spawn in one round,
    /// counted across all lavaland planets.
    /// </summary>
    [DataField]
    public Dictionary<string, int> GroupLimits = new();
    // Corvax-Goob-end

    [DataField]
    public Dictionary<ProtoId<LavalandGridRuinPrototype>, ushort> GridRuins = new();

    /// <summary>
    /// List of all dungeon ruins and their count.
    /// Used for ruins that are generated with Dungeon configs.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<LavalandDungeonRuinPrototype>, ushort> DungeonRuins = new();

    /// <summary>
    /// List of all marker ruins and their count.
    /// Used for ruins that are generated with Dungeon markers.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<LavalandMarkerRuinPrototype>, ushort> MarkerRuins = new();
}
