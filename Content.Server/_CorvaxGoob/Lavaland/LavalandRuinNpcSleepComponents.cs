// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._CorvaxGoob.Lavaland;

/// <summary>
/// Put on a lavaland ruin grid. NPCs that start on this grid are put to sleep while no player is nearby.
/// </summary>
[RegisterComponent, Access(typeof(LavalandRuinNpcSleepSystem))]
public sealed partial class LavalandRuinNpcSleepGridComponent : Component
{
    /// <summary>
    /// NPCs wake up when a living player-controlled mob is within this many tiles (meters).
    /// </summary>
    [DataField]
    public float WakeRange = 25f;

    /// <summary>
    /// An awake NPC goes back to sleep after no player has been within <see cref="WakeRange"/> for this long.
    /// </summary>
    [DataField]
    public TimeSpan SleepDelay = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Added to an NPC that was found on a <see cref="LavalandRuinNpcSleepGridComponent"/> grid.
/// It stays with the NPC when the NPC leaves the grid.
/// </summary>
[RegisterComponent, Access(typeof(LavalandRuinNpcSleepSystem))]
public sealed partial class LavalandRuinNpcSleepComponent : Component
{
    [ViewVariables]
    public float WakeRange = 25f;

    [ViewVariables]
    public TimeSpan SleepDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Last time a player was near or the NPC took damage.
    /// </summary>
    [ViewVariables]
    public TimeSpan LastActivity;

    /// <summary>
    /// True if this system put the NPC to sleep. We only wake NPCs we slept ourselves.
    /// </summary>
    [ViewVariables]
    public bool SleptByUs;
}
