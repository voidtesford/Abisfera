using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// The sentient disease «Закат» itself: an incorporeal observer that owns one disease strain.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZakatComponent : Component
{
    /// <summary>
    /// Current biomass, the only currency.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Biomass;

    /// <summary>
    /// Biomass for every organism that becomes a host for the first time.
    /// </summary>
    [DataField]
    public float BiomassPerNewHost = 5f;

    /// <summary>
    /// Biomass per living host every <see cref="PassiveInterval"/>.
    /// </summary>
    [DataField]
    public float BiomassPerLivingHost = 1f;

    [DataField]
    public TimeSpan PassiveInterval = TimeSpan.FromSeconds(60);

    [ViewVariables]
    public TimeSpan NextPassive;

    /// <summary>
    /// Free, guaranteed starting infections left. There is no other way to infect by hand.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int StartPicksLeft = 2;

    [DataField]
    public EntProtoId InfectAction = "ActionZakatInfect";

    [ViewVariables]
    public EntityUid? InfectActionEntity;

    [DataField]
    public ProtoId<AlertPrototype> BiomassAlert = "ZakatBiomass";

    /// <summary>
    /// Bought «Рост» levels of the core (see <see cref="ZakatGrowth"/>).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Growth;

    [DataField]
    public EntProtoId PossessAction = "ActionZakatPossess";

    [ViewVariables]
    public EntityUid? PossessActionEntity;

    [DataField]
    public TimeSpan PossessDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Host the disease is driving right now (Рост 1).
    /// </summary>
    [ViewVariables]
    public EntityUid? PossessedHost;

    [ViewVariables]
    public TimeSpan PossessEnd;

    /// <summary>
    /// Where the host's own mind waits while the disease drives the body.
    /// </summary>
    [ViewVariables]
    public EntityUid? VictimStash;

    [ViewVariables]
    public EntityUid? VictimMind;

    [DataField]
    public string HiveMind = "ZakatHivemind";

    // Рост 3 «рождение»

    [DataField]
    public EntProtoId BirthAction = "ActionZakatBirth";

    [ViewVariables]
    public EntityUid? BirthActionEntity;

    [DataField]
    public EntProtoId CorePrototype = "ZakatCore";

    [DataField]
    public EntProtoId FleshFloorPrototype = "ZakatFleshFloor";

    [DataField]
    public EntProtoId FleshWallPrototype = "ZakatFleshWall";

    [DataField]
    public EntProtoId FleshWindowPrototype = "ZakatFleshWindow";

    [DataField]
    public EntProtoId CaptureController = "ZakatFleshController";

    /// <summary>
    /// What the born disease can do: buildings, «освободить от оков», «создать преторианца», back to the core.
    /// </summary>
    [DataField]
    public List<EntProtoId> FleshActions = new()
    {
        "ActionZakatBuildSpewer",
        "ActionZakatBuildCocoonSmall",
        "ActionZakatBuildCocoonHunter",
        "ActionZakatBuildCocoonLarge",
        "ActionZakatBuildWall",
        "ActionZakatBuildDoor",
        "ActionZakatBuildSporeSpewer",
        "ActionZakatLiberate",
        "ActionZakatPraetorian",
        "ActionZakatToCore",
    };

    [ViewVariables]
    public List<EntityUid> FleshActionEntities = new();

    /// <summary>
    /// The core of flesh, once born.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Core;

    [ViewVariables, AutoNetworkedField]
    public bool Born;

    /// <summary>
    /// The core was destroyed: «Закат» has lost.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Defeated;

    [ViewVariables]
    public EntityUid? ControllerEntity;

    /// <summary>
    /// Active captured floor tiles (Рост 4 needs <see cref="ZakatGrowth.VineTiles"/>).
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public int CapturedTiles;

    /// <summary>
    /// Living infected hosts right now (not the flesh's creatures): gates «Рост» 2 and 3.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public int LivingHosts;

    // Рост 4: the vines

    [DataField]
    public EntProtoId VinePrototype = "ZakatVine";

    /// <summary>
    /// The vines have burst out of the core.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool VinesReleased;

    /// <summary>
    /// After the vines burst out the round ends (as when the command ends it) and restarts after this.
    /// </summary>
    [DataField]
    public TimeSpan VineRoundRestart = TimeSpan.FromMinutes(5);

    [DataField]
    public bool VinesEndRound = true;

    [ViewVariables]
    public TimeSpan NextCapture;

    /// <summary>upd1: next blow of the flesh at a wall / window / airlock.</summary>
    [ViewVariables]
    public TimeSpan NextAttack;

    /// <summary>upd1: passive spread of the flesh onto free floor next to it (never through walls, doors or windows).</summary>
    [DataField]
    public TimeSpan PassiveSpreadInterval = TimeSpan.FromSeconds(5);

    /// <summary>upd1: tiles the passive spread tries to take per interval.</summary>
    [DataField]
    public int PassiveSpreadTiles = 1;

    [ViewVariables]
    public TimeSpan NextPassiveSpread;

    // contagion base chances (multiplied by the host's infectivity, then by protective gear)
    [DataField]
    public float ContactChance = 0.08f;

    [DataField]
    public float AerialChance = 0.4f;

    [DataField]
    public float VomitChance = 0.3f;

    [DataField]
    public float VomitRange = 1.5f;

    [DataField]
    public EntProtoId StrainPrototype = "DiseaseZakat";

    [ViewVariables]
    public EntityUid? Strain;

    [ViewVariables, AutoNetworkedField]
    public int Genotype;

    [ViewVariables, AutoNetworkedField]
    public HashSet<NetEntity> Infected = new();

    [ViewVariables, AutoNetworkedField]
    public HashSet<NetEntity> Immune = new();

    [ViewVariables, AutoNetworkedField]
    public int HealthyCount;

    /// <summary>
    /// Living station crew (humanoids with a mind; animals don't count) and how many of them are infected.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public int CrewTotal;

    [ViewVariables, AutoNetworkedField]
    public int CrewInfected;

    [ViewVariables]
    public HashSet<EntityUid> KnownHosts = new();

    [ViewVariables]
    public int PeakInfected;

    [ViewVariables]
    public float PeakCrewShare;

    [ViewVariables]
    public bool Victory;

    /// <summary>
    /// Host whose evolution map is open.
    /// </summary>
    [ViewVariables]
    public EntityUid? ViewedHost;

    [ViewVariables]
    public TimeSpan NextUpdate;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
}
