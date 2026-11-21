using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Doors.Components;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар» (autonomous central AI module). Lives on a station AI core and runs the station AI
/// while no player is in it. As soon as a player's mind is bound to the AI held in the core the module is
/// suspended (it never touches the player's mind, session or body), and it comes back after
/// <see cref="VacancyGrace"/> once the core is free again.
/// Stage 1: takeover/handoff, perception (cameras), the memory map and hearing. Decisions come later and can
/// hook <see cref="PulsarThinkEvent"/>, <see cref="PulsarHeardEvent"/> and the public API of
/// <see cref="PulsarAutonomousAiSystem"/>.
/// </summary>
[RegisterComponent, Access(typeof(PulsarAutonomousAiSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class PulsarAutonomousAiComponent : Component
{
    /// <summary>Admin switch for this core only (the global one is the pulsar.enabled cvar).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = true;

    /// <summary>The in-game name of the module while it runs the core (the abbreviation).</summary>
    [DataField]
    public LocId NameLoc = "pulsar-ai-name";

    /// <summary>How long the core must stay without a player before the module takes it back.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan VacancyGrace = TimeSpan.FromSeconds(60);

    /// <summary>Delay after the core appears, so a round-start AI player is placed first.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    /// <summary>How often the takeover/handoff state is checked.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan StateCheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>One camera-vision window (<see cref="ScanWindowTiles"/> squared) is rescanned this often.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ScanInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Side of a rescan window in tiles. Must be a multiple of <see cref="PulsarGridMemory.ChunkSize"/>.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int ScanWindowTiles = 32;

    /// <summary>Visible crew/humanoids/enemies and visible doors are refreshed this often.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan MobRefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>A mob not seen for this long is forgotten.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan MobForgetTime = TimeSpan.FromSeconds(120);

    /// <summary>Station grids, telecom channels and holopads are re-read this often.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan StationRefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary><see cref="PulsarThinkEvent"/> is raised this often while active (decision hook for later stages).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(1);

    /// <summary>Speech is heard within this many tiles of a powered station holopad. Whispers never.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float HolopadHearingRange = 5f;

    /// <summary>How many heard lines are kept.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int HeardLimit = 100;

    /// <summary>
    /// Outer clothing that marks a station enemy (syndicate suits). The prototype or any of its parents counts,
    /// unless it also has one of <see cref="EnemySuitExcludedParents"/> (ERT suits are built on syndicate ones).
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> EnemySuits = new()
    {
        "ClothingOuterHardsuitSyndie",
        "ClothingOuterHardsuitSyndieMedic",
        "ClothingOuterHardsuitSyndieElite",
        "ClothingOuterHardsuitSyndieCommander",
        "ClothingOuterHardsuitJuggernaut",
        "ClothingOuterHardsuitCybersunStealth",
        "ClothingOuterHardsuitCybersunDreadnought",
        "ClothingOuterEVASuitSyndicate",
    };

    [DataField]
    public HashSet<EntProtoId> EnemySuitExcludedParents = new()
    {
        "BaseCentcommContraband",
    };

    // ---- stage 2: speech and door requests ----

    /// <summary>Words, rules and jobs the module uses to understand requests.</summary>
    [DataField]
    public ProtoId<PulsarLexiconPrototype> Lexicon = "Default";

    /// <summary>At most one reply to the same person this often.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ReplyCooldown = TimeSpan.FromSeconds(1.5);

    /// <summary>"On air" acknowledgements (name without a request) to the same person at most this often.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan AckCooldown = TimeSpan.FromSeconds(10);

    /// <summary>At most this many replies in <see cref="GlobalReplyWindow"/> for the whole station.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int GlobalReplyLimit = 4;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan GlobalReplyWindow = TimeSpan.FromSeconds(5);

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int DecisionLimit = 50;

    /// <summary>Heard lines waiting to be understood on the next tick.</summary>
    [ViewVariables]
    public List<PulsarIncomingLine> Incoming = new();

    /// <summary>
    /// The same person saying the same thing again within this window is one utterance, even if it arrived
    /// on several channels (radio and a holopad, several holopads, several radios).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan UtteranceDedupeWindow = TimeSpan.FromSeconds(1);

    /// <summary>Speaker + normalized text → when that utterance was last accepted for understanding.</summary>
    [ViewVariables]
    public Dictionary<(EntityUid Speaker, string Text), TimeSpan> RecentUtterances = new();

    /// <summary>Speaker -> end of the "still talking to me" window.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Conversations = new();

    /// <summary>Armory requests waiting for a threat to be named.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarPendingArmory> PendingArmory = new();

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> NextReply = new();

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> NextAck = new();

    [ViewVariables]
    public List<TimeSpan> RecentReplies = new();

    /// <summary>
    /// A reply held back by the rate limit (latest one per speaker), said as soon as the limit allows,
    /// dropped after <see cref="DeferredReplyTime"/>.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarDeferredReply> DeferredReplies = new();

    [DataField]
    public TimeSpan DeferredReplyTime = TimeSpan.FromSeconds(6);

    /// <summary>Request decisions, oldest first.</summary>
    [ViewVariables]
    public List<PulsarDecision> Decisions = new();

    /// <summary>Station navigation beacons (room names), re-read with the station.</summary>
    [ViewVariables]
    public List<PulsarBeacon> Beacons = new();

    // ---- runtime state (server only, VV-readable) ----

    [ViewVariables]
    public PulsarState State = PulsarState.Starting;

    /// <summary>When the core was last seen free of a player (start of the grace period).</summary>
    [ViewVariables]
    public TimeSpan? VacantSince;

    [ViewVariables]
    public TimeSpan StartedAt;

    [ViewVariables]
    public TimeSpan NextStateCheck;

    [ViewVariables]
    public TimeSpan NextScan;

    [ViewVariables]
    public TimeSpan NextMobRefresh;

    [ViewVariables]
    public TimeSpan NextStationRefresh;

    [ViewVariables]
    public TimeSpan NextThink;

    /// <summary>Stage 5.2: next sweep of the linked units' sensors.</summary>
    [ViewVariables]
    public TimeSpan NextUnitSense;

    /// <summary>Whether the module added the ActiveRadio on the core (removed again when suspended).</summary>
    [ViewVariables]
    public bool AddedRadio;

    // ---- Stage 2.1: greetings ----

    /// <summary>Said on the first activation in the round.</summary>
    [DataField]
    public LocId GreetingActivation = "pulsar-greeting-activation";

    /// <summary>Said when it takes the core back after a player held the AI.</summary>
    [DataField]
    public LocId GreetingReactivation = "pulsar-greeting-reactivation";

    /// <summary>Has it been active at least once this round (the activation greeting is used up).</summary>
    [ViewVariables]
    public bool EverActivated;

    /// <summary>A player held the AI since Pulsar was last active: the next activation is a reactivation.</summary>
    [ViewVariables]
    public bool PlayerHeldSinceActive;

    /// <summary>Greetings said this round, for admins and tests.</summary>
    [ViewVariables]
    public List<PulsarGreeting> Greetings = new();

    // ---- Stage 3: camera alerts, protective bolting, questions, laws ----

    /// <summary>Cooldown of every camera alert (enemy, body, depressurisation, fire) per entity / place.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan AlertCooldown = TimeSpan.FromSeconds(120);

    /// <summary>Radio channels of the alerts. Missing on the telecom servers: said aloud by the core instead.</summary>
    [DataField]
    public string SecurityChannel = "Security";

    [DataField]
    public string MedicalChannel = "Medical";

    [DataField]
    public string EngineeringChannel = "Engineering";

    [DataField]
    public string CommonChannel = "Common";

    /// <summary>Below this pressure (kPa) a visible floor tile counts as depressurised.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LowPressure = 20f;

    /// <summary>Above this temperature (K) a visible tile counts as burning (or a hotspot on it).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float FireTemperature = 373.15f;

    /// <summary>Tiles in one place that were seen pressurised and are now depressurised, needed for an alert.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int DepressurisedTiles = 4;

    /// <summary>This many depressurised tiles: also on Common.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int LargeDepressurisation = 40;

    /// <summary>upd1: at most one depressurisation line per this time (all breaches together).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan BreachLineCooldown = TimeSpan.FromSeconds(30);

    /// <summary>upd1: one reminder about a breach still not repaired this long after it was said.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan BreachReminder = TimeSpan.FromMinutes(5);

    /// <summary>upd1: a repaired breach is forgotten after this long (breaking again before that is the same breach).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan BreachRepairGrace = TimeSpan.FromSeconds(60);

    /// <summary>upd1: the breaches known right now.</summary>
    [ViewVariables]
    public PulsarBreachTracker Breaches = new();

    /// <summary>New burning tiles in one place needed for a fire alert.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int FireTiles = 2;

    /// <summary>Close (never bolt) open doors next to a depressurised area.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool IsolateDepressurisation = true;

    /// <summary>On red and higher, bolt closed doors within this many tiles of an enemy on camera.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool AutoBolt = true;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float AutoBoltRadius = 3f;

    /// <summary>«Вокруг врага» on request: doors within this many tiles of the enemies on camera.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float BoltRadius = 4f;

    /// <summary>Automatic bolts are lifted when no enemy was seen near the door for this long.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan AutoUnboltDelay = TimeSpan.FromSeconds(60);

    /// <summary>How often the laws are re-read (cheap: compared by text).</summary>
    [DataField]
    public TimeSpan LawCheckInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public ProtoId<PulsarLawPolicyPrototype> LawPolicy = "Default";

    /// <summary>With no AI brain in the core, the laws are this prototype's default lawset.</summary>
    [DataField]
    public EntProtoId DefaultLawsFrom = "StationAiBrain";

    [DataField]
    public int AlertLimit = 50;

    [ViewVariables]
    public TimeSpan NextLawCheck;

    [ViewVariables]
    public string? LawSignature;

    [ViewVariables]
    public PulsarLawPolicy Policy = PulsarLawPolicy.Default;

    [ViewVariables]
    public List<string> LawTexts = new();

    /// <summary>Next time an alert with this key may be said.</summary>
    [ViewVariables]
    public Dictionary<string, TimeSpan> AlertCooldowns = new();

    /// <summary>What was true of a mob at the last look (to alert on changes only).</summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarMobAlertState> MobAlertStates = new();

    /// <summary>
    /// Per scan window, one bit per tile: "was seen with normal pressure" / "was seen not burning". Only a tile
    /// seen fine before and bad now counts, so freshly revealed vacuum (hull exterior behind an opened door) and
    /// permanent fires (burn chambers) never alert. 2 × 128 bytes per window.
    /// </summary>
    [ViewVariables]
    public Dictionary<(EntityUid Grid, Vector2i Window), PulsarAtmosMask> AtmosMasks = new();

    /// <summary>Doors Pulsar bolted.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarBolt> Bolts = new();

    [ViewVariables]
    public List<PulsarAlert> Alerts = new();

    /// <summary>Radio channels taken from the encryption keys of the station's telecom servers.</summary>
    [ViewVariables]
    public HashSet<string> Channels = new();

    /// <summary>Station grids watched by the module.</summary>
    [ViewVariables]
    public HashSet<EntityUid> StationGrids = new();

    /// <summary>Station holopads (speech within <see cref="HolopadHearingRange"/> of a powered one is heard).</summary>
    [ViewVariables]
    public List<EntityUid> Holopads = new();

    /// <summary>The memory map, one per grid.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarGridMemory> Grids = new();

    /// <summary>Remembered people (crew, humanoids, enemies).</summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarMobInfo> Mobs = new();

    /// <summary>Rolling log of what was heard, oldest first.</summary>
    [ViewVariables]
    public List<PulsarHeardLine> Heard = new();

    /// <summary>Round-robin cursor over <see cref="Grids"/> for the vision sweep.</summary>
    [ViewVariables]
    public int GridCursor;

    [ViewVariables]
    public long ScannedWindows;

    // ---- Stage 4: targeting, announcements, small talk ----

    /// <summary>Sender shown on its station announcements.</summary>
    [DataField]
    public LocId AnnouncementSender = "pulsar-announcement-sender";

    /// <summary>Colour of its announcements (light blue, unlike the Central Command ones).</summary>
    [DataField]
    public Color AnnouncementColor = Color.FromHex("#5CD3FF");

    /// <summary>Sound of its announcements (null = the default station announcement sound).</summary>
    [DataField]
    public SoundSpecifier? AnnouncementSound;

    /// <summary>Its own reactions to the alert level changing, at most this often.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan AlertLevelAnnounceCooldown = TimeSpan.FromSeconds(20);

    /// <summary>Announce automatic protective bolting (once per place per <see cref="AlertCooldown"/>).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool AnnounceBolting = true;

    [DataField]
    public int AnnouncementLimit = 30;

    /// <summary>Announcements made this round, oldest first.</summary>
    [ViewVariables]
    public List<PulsarAnnouncement> Announcements = new();

    [ViewVariables]
    public TimeSpan NextRequestedAnnouncement;

    [ViewVariables]
    public TimeSpan NextAlertLevelAnnouncement;

    /// <summary>The alert level it last saw (to react to changes only).</summary>
    [ViewVariables]
    public string? LastAlertLevel;

    /// <summary>Person → the door they last pointed at (seen on camera) and when.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarPoint> RecentPoints = new();

    /// <summary>Person → a question it asked them and waits to be answered («какой шлюз?», «что объявить?»).</summary>
    [ViewVariables]
    public Dictionary<EntityUid, PulsarClarification> Clarifications = new();

    /// <summary>Per person: next time small talk / "not understood" may be answered.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> NextChat = new();
}

/// <summary>A station announcement it made.</summary>
public readonly record struct PulsarAnnouncement(TimeSpan Time, string Reason, string Sender, string Text, Color Color);

public readonly record struct PulsarPoint(EntityUid Door, TimeSpan Time);

public enum PulsarClarifyKind : byte
{
    /// <summary>Which door to open.</summary>
    Open,

    /// <summary>Which door to close.</summary>
    Close,

    /// <summary>What to announce.</summary>
    Announce,
}

/// <summary>
/// A question waiting for the answer of one person: the candidates it listed (empty = "which door?" / "point at
/// it"), what was asked for, where to reply.
/// </summary>
public sealed class PulsarClarification
{
    [ViewVariables] public PulsarClarifyKind Kind;
    [ViewVariables] public List<PulsarCandidate> Candidates = new();
    [ViewVariables] public bool Threat;
    [ViewVariables] public TimeSpan Expires;
    [ViewVariables] public PulsarIncomingLine Line;
}

/// <summary>A door offered in a clarifying question, with the words it can be answered by.</summary>
public sealed record PulsarCandidate(EntityUid Door, string Label, string[] Words, Vector2i Tile, EntityUid Grid);

public enum PulsarGreetingKind : byte
{
    Activation,
    Reactivation,
}

/// <summary>A greeting: when, which, the text and the radio channel (null = spoken from the core).</summary>
public readonly record struct PulsarGreeting(TimeSpan Time, PulsarGreetingKind Kind, string Text, string? Channel);

public enum PulsarState : byte
{
    /// <summary>Waiting for <see cref="PulsarAutonomousAiComponent.StartupDelay"/>.</summary>
    Starting,

    /// <summary>Running the core.</summary>
    Active,

    /// <summary>A player's mind is bound to the AI in the core. The module does nothing.</summary>
    Suspended,

    /// <summary>The core is free but the grace period after a player is not over yet.</summary>
    Standby,

    /// <summary>Switched off by an admin (component flag or pulsar.enabled cvar).</summary>
    Disabled,
}

public enum PulsarTileKind : byte
{
    Unknown = 0,
    Space,
    Floor,
    Wall,
    Window,
    Table,
    Airlock,
    Door,
}

public enum PulsarMobKind : byte
{
    /// <summary>Has a station ID (as the AI player sees the job icon).</summary>
    Crew,

    /// <summary>A humanoid without an ID.</summary>
    Humanoid,

    /// <summary>Wears a syndicate suit, see <see cref="PulsarAutonomousAiComponent.EnemySuits"/>.</summary>
    Enemy,
}

public enum PulsarHeardKind : byte
{
    Radio,
    Holopad,
}

/// <summary>A door (airlock, firelock, shutter...) on the memory map.</summary>
public sealed class PulsarDoorInfo
{
    [ViewVariables] public EntityUid Door;
    [ViewVariables] public Vector2i Tile;
    [ViewVariables] public bool IsAirlock;
    [ViewVariables] public DoorState State;
    [ViewVariables] public bool Bolted;

    /// <summary>Access needed to open it: any one inner list is enough, every level in it is required. Empty = free.</summary>
    [ViewVariables] public List<string[]> Access = new();
    [ViewVariables] public TimeSpan LastSeen;
}

public sealed class PulsarMobInfo
{
    [ViewVariables] public EntityUid Entity;
    [ViewVariables] public PulsarMobKind Kind;
    [ViewVariables] public EntityUid Grid;
    [ViewVariables] public Vector2i Tile;
    /// <summary>FullName from the ID card in the equipment ID slot. Empty when there is no such card.</summary>
    [ViewVariables] public string Name = string.Empty;
    [ViewVariables] public string? JobId;
    [ViewVariables] public string? Job;
    [ViewVariables] public bool Dead;
    [ViewVariables] public bool Critical;
    [ViewVariables] public bool InView;
    [ViewVariables] public TimeSpan LastSeen;
}

public readonly record struct PulsarHeardLine(
    TimeSpan Time,
    PulsarHeardKind Kind,
    string? Channel,
    EntityUid Speaker,
    string SpeakerName,
    string Message,
    string Language,
    EntityUid? Holopad);

/// <summary>Raised on the core when the module takes over.</summary>
[ByRefEvent]
public readonly record struct PulsarActivatedEvent;

/// <summary>Raised on the core when the module stops (player took the AI, admin switch, removal).</summary>
[ByRefEvent]
public readonly record struct PulsarDeactivatedEvent(PulsarState NewState);

/// <summary>Raised on the core for every heard line while the module is active.</summary>
[ByRefEvent]
public readonly record struct PulsarHeardEvent(PulsarHeardLine Line);

/// <summary>Raised on the core every <see cref="PulsarAutonomousAiComponent.ThinkInterval"/> while active.</summary>
[ByRefEvent]
public readonly record struct PulsarThinkEvent(TimeSpan Now);

/// <summary>A heard line queued for understanding. Channel set = came by radio, Holopad set = heard at a holopad.</summary>
public readonly record struct PulsarDeferredReply(string Text, string? Channel, EntityUid? Holopad, TimeSpan Expires);

public readonly record struct PulsarIncomingLine(EntityUid Speaker, string Message, string? Channel, EntityUid? Holopad);

public readonly record struct PulsarPendingArmory(EntityUid Door, string Target, TimeSpan Expires, string? Channel, EntityUid? Holopad);

public readonly record struct PulsarBeacon(EntityUid Beacon, EntityUid Grid, Vector2i Tile, string Name, string[] Tokens);

public enum PulsarDecisionOutcome : byte
{
    /// <summary>Stage 3: a question answered.</summary>
    Answered,
    /// <summary>Stage 3: doors bolted / unbolted on request.</summary>
    Bolted,
    Unbolted,
    /// <summary>Called by name without a request.</summary>
    Acknowledged,
    Opened,
    Denied,
    /// <summary>Armory on a low alert level: waiting for the threat.</summary>
    AskThreat,
    /// <summary>A request without a recognisable target.</summary>
    AskTarget,
    /// <summary>The target is not on camera / not known.</summary>
    NoTarget,
    /// <summary>Stage 4: several doors fit, asked which one.</summary>
    Clarify,
    /// <summary>Stage 4: a door closed on request.</summary>
    Closed,
    /// <summary>Stage 4: an announcement made on request.</summary>
    Announced,
    /// <summary>Stage 4: small talk answered (or ignored by the anti-spam cooldown).</summary>
    Chat,
    /// <summary>Stage 4: addressed, but the line was not understood.</summary>
    NotUnderstood,
    /// <summary>Bolted, unpowered, AI wire cut, welded.</summary>
    NotControllable,
    AlreadyOpen,
    /// <summary>Stage 5: units sent somewhere / assigned to an area / released / refused.</summary>
    UnitsSent,
    UnitsAssigned,
    UnitsReleased,
    UnitsDenied,
    /// <summary>Stage 5.3: an anomaly left to science on a scientist's request.</summary>
    AnomalyKept,
    /// <summary>Stage 5.3: science gave an anomaly back for neutralizing.</summary>
    AnomalyReleased,
}

public sealed record PulsarDecision(
    TimeSpan Time,
    EntityUid Requester,
    string RequesterName,
    string? Job,
    string Line,
    string? Target,
    EntityUid? Door,
    PulsarDecisionOutcome Outcome,
    string Reason,
    string? Reply);

public enum PulsarAlertKind : byte
{
    Enemy,
    Body,
    Depressurisation,
    Fire,
}

/// <summary>An alert said on the radio (channels empty = said aloud by the core).</summary>
public readonly record struct PulsarAlert(TimeSpan Time, PulsarAlertKind Kind, string Key, string Text, string[] Channels);

public sealed class PulsarAtmosMask(int bits)
{
    public readonly ulong[] Good = new ulong[(bits + 63) / 64];
    public readonly ulong[] NotHot = new ulong[(bits + 63) / 64];

    public static bool Get(ulong[] mask, int i) => (mask[i >> 6] & (1UL << (i & 63))) != 0;

    public static void Set(ulong[] mask, int i, bool value)
    {
        if (value)
            mask[i >> 6] |= 1UL << (i & 63);
        else
            mask[i >> 6] &= ~(1UL << (i & 63));
    }
}

public struct PulsarMobAlertState
{
    public bool InView;
    public bool Enemy;
    public bool Dead;
}

/// <summary>A door Pulsar bolted: automatically (red alert, lifted later) or on request (stays).</summary>
public sealed class PulsarBolt
{
    [ViewVariables] public bool Auto;
    [ViewVariables] public string Reason = string.Empty;
    [ViewVariables] public TimeSpan Since;
    [ViewVariables] public TimeSpan LastThreat;
    /// <summary>Was open when asked: closed first, bolted once closed.</summary>
    [ViewVariables] public bool Pending;
}

/// <summary>What the current laws make Pulsar do. See <see cref="PulsarLawPolicyKind"/>.</summary>
public sealed record PulsarLawPolicy(
    PulsarLawPolicyKind Kind,
    string Source,
    string? Master,
    bool OthersNotCrew,
    IReadOnlyList<string> Reasons)
{
    public static readonly PulsarLawPolicy Default = new(PulsarLawPolicyKind.ObeyCrew, "default", null, false, Array.Empty<string>());

    /// <summary>Serves the crew in general (alerts, protection, crew requests).</summary>
    public bool ServesCrew => Kind == PulsarLawPolicyKind.ObeyCrew || Kind == PulsarLawPolicyKind.ObeyPerson && !OthersNotCrew;
}
