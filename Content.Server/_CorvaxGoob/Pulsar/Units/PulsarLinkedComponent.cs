using Robust.Shared.Map;

namespace Content.Server._CorvaxGoob.Pulsar.Units;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 5: a minibot (medibot, weldbot, firebot, cleanbot) or a cyborg linked to the station AI
/// from the robotics console («Подключить к ЦИИ»). Only linked units take orders from the module, and only while
/// the module runs the core. A player in the unit (a borg brain with a mind, a sentient bot) is never driven: the
/// module can only talk to it on the radio. Anchoring a minibot with a wrench unlinks it.
/// </summary>
[RegisterComponent, Access(typeof(PulsarUnitsSystem))]
public sealed partial class PulsarLinkedComponent : Component
{
    /// <summary>The AI core it was linked to (the station's Pulsar core when linked from a console).</summary>
    [ViewVariables] public EntityUid? Core;

    [ViewVariables] public PulsarUnitKind Kind;

    /// <summary>The current order: a place to go to, maybe to stay in.</summary>
    [ViewVariables] public PulsarUnitOrder? Order;

    /// <summary>Stage machine of a borg task (heal, repair, anomaly).</summary>
    [ViewVariables] public PulsarBorgTask? Task;

    [ViewVariables] public TimeSpan NextUpdate;

    /// <summary>Progress watchdog for walks: closest distance so far and when it was reached.</summary>
    [ViewVariables] public float BestDistance = float.MaxValue;
    [ViewVariables] public TimeSpan BestDistanceAt;

    /// <summary>The module turned the chassis on itself (mindless borg) and must turn it off when it lets go.</summary>
    [ViewVariables] public bool ActivatedChassis;

    /// <summary>The module added ActiveNPC / steering to a borg.</summary>
    [ViewVariables] public bool Driving;

    /// <summary>Things it already tried and could not do, so it does not try them again at once.</summary>
    [ViewVariables] public readonly Dictionary<EntityUid, TimeSpan> Ignore = new();

    // ---- stage 5.2: where it is, the route ----

    /// <summary>Where it is by the module's map (grid, tile, the room's name), refreshed by its own sensors.</summary>
    [ViewVariables] public EntityUid? Grid;
    [ViewVariables] public Vector2i Tile;
    [ViewVariables] public string Where = string.Empty;

    /// <summary>Tiles its route must keep off (airlocks the module cannot open for it, blocked tiles).</summary>
    [ViewVariables] public readonly HashSet<(EntityUid Grid, Vector2i Tile)> Avoid = new();

    /// <summary>Re-plans of the route since it last got closer to the target.</summary>
    [ViewVariables] public int Replans;
    [ViewVariables] public TimeSpan LastReplan;

    /// <summary>Since when the engine keeps answering "no path" (a borg asks again for a few seconds).</summary>
    [ViewVariables] public TimeSpan? NoPathSince;

    [ViewVariables] public TimeSpan NextShove;

    /// <summary>Stage 5.3: next look for a free recharging station.</summary>
    [ViewVariables] public TimeSpan NextChargeSearch;

    /// <summary>Doors the module opened on its route (for the debug view).</summary>
    [ViewVariables] public int DoorsOpened;
}

public enum PulsarUnitKind : byte
{
    Medibot,
    Weldbot,
    Firebot,
    Cleanbot,
    BorgGeneric,
    BorgEngineering,
    BorgMedical,
    BorgService,
    BorgJanitor,
    BorgMining,
    BorgScience,
    /// <summary>Any other chassis (syndicate, drones…): may be linked, but only walks.</summary>
    BorgOther,
}

/// <summary>Who may order which units: the department a unit belongs to.</summary>
public enum PulsarUnitDepartment : byte
{
    None,
    Engineering,
    Medical,
    Service,
    Science,
    Cargo,
}

public sealed class PulsarUnitOrder
{
    /// <summary>Where to go.</summary>
    public EntityCoordinates Target;
    /// <summary>Spoken name of the place («инженерный отдел»).</summary>
    public string Place = string.Empty;
    /// <summary>Stay in the area until released (assignment) instead of a one-time visit.</summary>
    public bool Assigned;
    /// <summary>How far it may roam from <see cref="Target"/> while staying.</summary>
    public float Radius = 8f;
    /// <summary>Rank of who gave the order (<see cref="PulsarOrderRank"/>); a lower rank cannot override it.</summary>
    public int Rank;
    /// <summary>Who gave it (for the refusal line), empty for the module's own orders.</summary>
    public string By = string.Empty;
    /// <summary>The module's own dispatch (fire, a patient, an anomaly) and not a person's order.</summary>
    public bool Autonomous;
    /// <summary>When a visit ends (it then goes back to its assignment or free roaming).</summary>
    public TimeSpan Expires;
    /// <summary>Arrived at least once.</summary>
    public bool Arrived;
    /// <summary>The thing the module sent it for (patient, anomaly, damaged window), if any.</summary>
    public EntityUid? Focus;
    /// <summary>The assignment to go back to when this visit ends.</summary>
    public PulsarUnitOrder? Previous;

    // ---- stage 5.2: leadership ----

    /// <summary>Units given the same order at once share a group: one report when they are all there.</summary>
    public int Group;
    /// <summary>What kinds may carry it out (to hand it over to another unit if this one cannot get there).</summary>
    public HashSet<PulsarUnitKind>? Kinds;
    /// <summary>The module's own need it serves (dispatch), for the same reason.</summary>
    public PulsarNeed? Need;
    /// <summary>Units that already failed it.</summary>
    public HashSet<EntityUid> Tried = new();

    /// <summary>A copy for another unit (same place, rank, group), remembering who already failed.</summary>
    public PulsarUnitOrder Handover(EntityUid failed)
    {
        var copy = (PulsarUnitOrder) MemberwiseClone();
        copy.Tried = new HashSet<EntityUid>(Tried) { failed };
        copy.Arrived = false;
        copy.Previous = null;
        return copy;
    }
}

/// <summary>Order ranks. Bigger wins; equal replaces.</summary>
public static class PulsarOrderRank
{
    /// <summary>The module's own dispatch.</summary>
    public const int Autonomous = 5;
    /// <summary>Any crew member.</summary>
    public const int Crew = 10;
    /// <summary>Staff of the unit's department (engineers → engineering borgs and weldbots…).</summary>
    public const int Department = 50;
    /// <summary>Any head of department, over all units.</summary>
    public const int Head = 80;
    /// <summary>Security and the captain on red alert or higher.</summary>
    public const int RedAlertSecurity = 90;
    /// <summary>The captain, always.</summary>
    public const int Captain = 100;
}

public enum PulsarBorgTaskKind : byte
{
    Heal,
    Repair,
    Anomaly,
    Extinguish,
    /// <summary>Stage 5.2: pry an unpowered airlock on its route open with a crowbar.</summary>
    Pry,
    /// <summary>Stage 5.3: carry a body to medbay (crew: revive) or to the morgue.</summary>
    Corpse,
    /// <summary>Stage 5.3: go to a cyborg recharging station and charge.</summary>
    Recharge,
}

public enum PulsarBorgTaskStage : byte
{
    Approach,
    Act,
    // Anomaly only:
    Scan,
    ToApe,
    UnanchorApe,
    PullApe,
    AnchorApe,
    Fire,
    Done,
    // Corpse / recharge (stage 5.3):
    Haul,
    Revive,
    Charge,
}

public sealed class PulsarBorgTask
{
    public PulsarBorgTaskKind Kind;
    public PulsarBorgTaskStage Stage;
    public EntityUid Target;
    /// <summary>A.P.E. used against an anomaly.</summary>
    public EntityUid? Ape;
    /// <summary>Where the A.P.E. is set up.</summary>
    public EntityCoordinates? ApeSpot;
    public Angle ApeFacing;
    public TimeSpan StageStarted;
    public TimeSpan Deadline;
    public int Attempts;

    // Stage 5.3, healing: did the last item help?
    public string? UsedItem;
    public float LastDamage;
    public int NoEffect;
    public HashSet<string> Excluded = new();

    // Stage 5.3, a body: where to and what for.
    public EntityUid? Destination;
    public bool Crew;
    public bool Reported;
}

/// <summary>Raised on an NPC whose path goes through an access door it cannot open itself.</summary>
[ByRefEvent]
public record struct NPCAccessDoorBlockedEvent(EntityUid Door, bool Handled = false);
