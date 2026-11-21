// SPDX-License-Identifier: AGPL-3.0-or-later

// ReSharper disable once CheckNamespace
namespace Content.Server._Lavaland.Procedural.Systems;

// Corvax-Goob: lavaland ruin performance (ruin group caps, planet air prefill).
public sealed partial class LavalandSystem
{
    /// <summary>
    /// How many grid ruins of each limit group have spawned this round, across all lavaland planets.
    /// </summary>
    private readonly Dictionary<string, int> _ruinGroupCounts = new();

    /// <summary>
    /// Ruin grids placed during the current planet setup that should get the planet air after map init.
    /// </summary>
    private readonly List<EntityUid> _prefillGrids = new();

    public int GetRuinGroupCount(string group)
    {
        return _ruinGroupCounts.GetValueOrDefault(group);
    }

    private void ResetRuinGroupCounts()
    {
        _ruinGroupCounts.Clear();
        _prefillGrids.Clear();
    }

    private void PrefillRuinAtmospheres()
    {
        foreach (var grid in _prefillGrids)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            var tiles = _atmos.PrefillOpenTilesWithMapAtmosphere(grid);
            Log.Debug($"Filled {tiles} open tiles of ruin {ToPrettyString(grid)} with the planet atmosphere.");
        }

        _prefillGrids.Clear();
    }
}
