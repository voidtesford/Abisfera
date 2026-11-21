using Content.Shared.Access;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._CorvaxGoob.Pulsar;

/// <summary>
/// What the autonomous station AI «Пульсар» understands and how it applies the special door rules.
/// <para>Phrase syntax (all lists below): words separated by spaces must follow each other in the line;
/// every word is a stem (the spoken word must start with it, so «пульсар» matches «Пульсару») unless it ends
/// with <c>$</c>, then the whole word must match (<c>ии$</c> matches «ИИ» but not «линии»). Matching ignores
/// case, «ё», Latin lookalike letters and dots between single letters (А.М.Ц.И.И = амции).</para>
/// </summary>
[Prototype("pulsarLexicon")]
public sealed partial class PulsarLexiconPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Ways to address the AI.</summary>
    [DataField]
    public List<string> Names = new();

    /// <summary>Words that make a line a request to open something.</summary>
    [DataField]
    public List<string> OpenWords = new();

    /// <summary>"This door / in front of me": the nearest airlock to the speaker.</summary>
    [DataField]
    public List<string> HereWords = new();

    /// <summary>Threat phrases that make the AI open the armory on a low alert level.</summary>
    [DataField]
    public List<string> ThreatPhrases = new();

    /// <summary>Words ignored when looking for a beacon (room) name in a request.</summary>
    [DataField]
    public List<string> StopWords = new();

    // ---- stage 5.3: anomalies science keeps ----

    /// <summary>«Аномалия».</summary>
    [DataField]
    public List<string> AnomalyWords = new();

    /// <summary>«Не трогай / оставь / не нейтрализуй» an anomaly: science works with it.</summary>
    [DataField]
    public List<string> AnomalyKeepWords = new();

    /// <summary>«Нейтрализуй / устрани / можно убирать» an anomaly science kept.</summary>
    [DataField]
    public List<string> AnomalyReleaseWords = new();

    /// <summary>«Все / всех»: every anomaly, not the nearest one.</summary>
    [DataField]
    public List<string> AnomalyAllWords = new();

    // ---- stage 5: units linked to the AI (minibots and cyborgs) ----

    /// <summary>«Пришли / отправь»: send units somewhere.</summary>
    [DataField]
    public List<string> UnitSendWords = new();

    /// <summary>«Закрепи / оставь»: assign units to an area (they stay and serve it).</summary>
    [DataField]
    public List<string> UnitAssignWords = new();

    /// <summary>«Открепи / отзови»: release units.</summary>
    [DataField]
    public List<string> UnitReleaseWords = new();

    /// <summary>«Какие / сколько / список»: the list of linked units.</summary>
    [DataField]
    public List<string> UnitStatusWords = new();

    /// <summary>«Сюда / ко мне»: the requester's own position.</summary>
    [DataField]
    public List<string> UnitHereWords = new();

    /// <summary>Cyborg nouns: «борг», «юнит», «киборг».</summary>
    [DataField]
    public List<string> UnitBorgWords = new();

    /// <summary>Plain «бот» (with an adjective: «пожарный бот»).</summary>
    [DataField]
    public List<string> UnitBotWords = new();

    /// <summary>Minibot nouns by kind name (Medibot, Weldbot, Firebot, Cleanbot).</summary>
    [DataField]
    public Dictionary<string, List<string>> UnitMinibotWords = new();

    /// <summary>
    /// Adjectives next to a unit noun, by role: Engineering, Medical, Science, Service, Janitor, Mining, Generic,
    /// Fire, Weld, Clean.
    /// </summary>
    [DataField]
    public Dictionary<string, List<string>> UnitAdjectives = new();

    /// <summary>Count words: index 0 = one, 1 = two… The last list means "all".</summary>
    [DataField]
    public List<List<string>> UnitCountWords = new();

    /// <summary>Service staff (outrank service and janitor borgs and cleanbots).</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> UnitServiceJobs = new();

    [DataField]
    public ProtoId<DepartmentPrototype> UnitEngineeringDepartment = "Engineering";

    [DataField]
    public ProtoId<DepartmentPrototype> UnitMedicalDepartment = "Medical";

    [DataField]
    public ProtoId<DepartmentPrototype> UnitScienceDepartment = "Science";

    [DataField]
    public ProtoId<DepartmentPrototype> UnitCargoDepartment = "Cargo";

    /// <summary>Department heads: may request the captain's quarters (see the README for the rules).</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> HeadJobs = new();

    [DataField]
    public ProtoId<JobPrototype> CaptainJob = "Captain";

    [DataField]
    public ProtoId<JobPrototype> WardenJob = "Warden";

    [DataField]
    public ProtoId<JobPrototype> HeadOfSecurityJob = "HeadOfSecurity";

    /// <summary>Jobs of this department count as security for the armory rule.</summary>
    [DataField]
    public ProtoId<DepartmentPrototype> SecurityDepartment = "Security";

    [DataField]
    public ProtoId<AccessLevelPrototype> CaptainAccess = "Captain";

    [DataField]
    public ProtoId<AccessLevelPrototype> ArmoryAccess = "Armory";

    /// <summary>Alert levels below red. Every other level (red, gamma, delta, epsilon, unknown) counts as red or higher.</summary>
    [DataField]
    public List<string> LowAlertLevels = new() { "green", "blue", "violet", "yellow" };

    /// <summary>How long after being addressed by name a speaker can go on without repeating the name.</summary>
    [DataField]
    public TimeSpan ConversationWindow = TimeSpan.FromSeconds(20);

    /// <summary>How long a "specify the threat level" armory request waits for an answer.</summary>
    [DataField]
    public TimeSpan PendingArmoryTime = TimeSpan.FromSeconds(60);

    /// <summary>"This door": max distance from the speaker in tiles.</summary>
    [DataField]
    public float HereRange = 2.5f;

    /// <summary>A door belongs to the nearest beacon within this many tiles (for beacon-named rooms).</summary>
    [DataField]
    public float BeaconRange = 15f;

    // ---- Stage 3 ----

    /// <summary>«Заблокируй/заболти шлюзы …»: bolt doors (Security staff and heads only).</summary>
    [DataField]
    public List<string> BoltWords = new();

    /// <summary>«Разблокируй/разболти …»: unbolt doors (Security staff and heads only).</summary>
    [DataField]
    public List<string> UnboltWords = new();

    /// <summary>«… вокруг врага»: the doors around enemies seen on camera.</summary>
    [DataField]
    public List<string> EnemyWords = new();

    /// <summary>Who may ask for bolting besides the heads (<see cref="HeadJobs"/>) and the captain.</summary>
    [DataField]
    public List<ProtoId<DepartmentPrototype>> BoltDepartments = new();

    /// <summary>«Где …?»</summary>
    [DataField]
    public List<string> WhereWords = new();

    /// <summary>«Какой код?»</summary>
    [DataField]
    public List<string> AlertQuestion = new();

    /// <summary>«Кто на смене?»</summary>
    [DataField]
    public List<string> ShiftQuestion = new();

    /// <summary>In a shift question: list the heads.</summary>
    [DataField]
    public List<string> HeadsWords = new();

    /// <summary>In a shift question: list security.</summary>
    [DataField]
    public List<string> SecurityWords = new();

    /// <summary>«Сколько идёт смена?»</summary>
    [DataField]
    public List<string> TimeQuestion = new();

    /// <summary>Spoken names of jobs for «где капитан?»: job id → phrases.</summary>
    [DataField]
    public Dictionary<ProtoId<JobPrototype>, List<string>> JobWords = new();

    // ---- Stage 4: targeting, announcements, flexible speech, small talk ----

    /// <summary>«Закрой …»: close a door (same rules as opening, or Security / heads).</summary>
    [DataField]
    public List<string> CloseWords = new();

    /// <summary>Words for a door («дверь», «шлюз», «аирлок»): a line with one of them and no other verb is an open request.</summary>
    [DataField]
    public List<string> DoorWords = new();

    /// <summary>«Объяви: …»: a station announcement on behalf of the captain / a head.</summary>
    [DataField]
    public List<string> AnnounceWords = new();

    /// <summary>Words dropped from the start of an announcement text («объяви всем: …»).</summary>
    [DataField]
    public List<string> AnnounceTrimWords = new();

    [DataField]
    public int AnnounceMaxLength = 200;

    /// <summary>Between two announcements made on request (whoever asked).</summary>
    [DataField]
    public TimeSpan AnnounceCooldown = TimeSpan.FromSeconds(60);

    /// <summary>A door pointed at this recently (seen on camera) is the target of «открой эту дверь».</summary>
    [DataField]
    public TimeSpan PointWindow = TimeSpan.FromSeconds(10);

    /// <summary>How long a clarifying question («какой шлюз?») waits for the answer of the same person.</summary>
    [DataField]
    public TimeSpan ClarifyWindow = TimeSpan.FromSeconds(30);

    /// <summary>The nearest door within this many tiles of the speaker wins over the other candidates without asking.</summary>
    [DataField]
    public float ObviousRange = 4f;

    /// <summary>«Северный / левый шлюз» without a room: doors within this many tiles of the speaker.</summary>
    [DataField]
    public float DirectionRange = 10f;

    /// <summary>Directions: absolute (station map, north up) and relative to where the speaker faces.</summary>
    [DataField]
    public List<string> NorthWords = new();

    [DataField]
    public List<string> SouthWords = new();

    [DataField]
    public List<string> WestWords = new();

    [DataField]
    public List<string> EastWords = new();

    [DataField]
    public List<string> LeftWords = new();

    [DataField]
    public List<string> RightWords = new();

    [DataField]
    public List<string> FrontWords = new();

    [DataField]
    public List<string> BackWords = new();

    /// <summary>Answers to «шлюз А или шлюз Б?» by position: first, second, third.</summary>
    [DataField]
    public List<List<string>> OrdinalWords = new();

    /// <summary>Small talk, checked in this order after requests and questions.</summary>
    [DataField]
    public List<PulsarChatTopic> ChatTopics = new();

    /// <summary>Replies to the name alone («Пульсар?»), one picked at random.</summary>
    [DataField]
    public List<LocId> AckReplies = new();

    /// <summary>Replies to a line addressed to it that it did not understand.</summary>
    [DataField]
    public List<LocId> UnknownReplies = new();

    /// <summary>Small talk / "not understood" replies to the same person at most this often.</summary>
    [DataField]
    public TimeSpan ChatCooldown = TimeSpan.FromSeconds(4);
}

public enum PulsarChatKind : byte
{
    /// <summary>One of the replies, as is.</summary>
    Plain,

    /// <summary>A station status summary (code, crew, breaches, enemies, bolts).</summary>
    Status,
}

/// <summary>A small-talk topic: what people say and what Pulsar answers (a random reply).</summary>
[DataDefinition]
public sealed partial class PulsarChatTopic
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField]
    public PulsarChatKind Kind = PulsarChatKind.Plain;

    [DataField(required: true)]
    public List<string> Phrases = new();

    [DataField]
    public List<LocId> Replies = new();
}

/// <summary>What Pulsar does under its laws.</summary>
public enum PulsarLawPolicyKind : byte
{
    /// <summary>Serves the crew: the normal rules.</summary>
    ObeyCrew,

    /// <summary>Hostile to the crew: refuses them, no alerts or protection for them.</summary>
    Hostile,

    /// <summary>Serves the Syndicate: like hostile, but people in syndicate suits are obeyed.</summary>
    ObeySyndicate,

    /// <summary>Serves one named person first; see <see cref="PulsarLawPolicy.OthersNotCrew"/>.</summary>
    ObeyPerson,
}

/// <summary>Known lawsets → policy. Lawsets not listed here and custom laws go through the text heuristics.</summary>
[Prototype("pulsarLawPolicy")]
public sealed partial class PulsarLawPolicyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public Dictionary<string, PulsarLawPolicyKind> Lawsets = new();

    /// <summary>A master name containing any of these (stems) is not a person («экипаж», «синдикат»…).</summary>
    [DataField]
    public List<string> NotPersonWords = new();
}

/// <summary>
/// A room the AI can be asked to open: «открой каюту капитана». Doors of the room are found by the access they
/// need, by the name of the nearest station beacon and by the door's own name (see <see cref="PulsarLexiconPrototype"/>
/// for the phrase syntax).
/// </summary>
[Prototype("pulsarRoom")]
public sealed partial class PulsarRoomPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name used in replies («Открываю: каюта капитана»).</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Phrases that name the room in speech.</summary>
    [DataField(required: true)]
    public List<string> Keywords = new();

    /// <summary>Doors needing any of these access levels belong to the room.</summary>
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> Access = new();

    /// <summary>Doors whose nearest station beacon name matches one of these belong to the room.</summary>
    [DataField]
    public List<string> Beacons = new();

    /// <summary>Doors whose own name matches one of these belong to the room.</summary>
    [DataField]
    public List<string> DoorNames = new();
}
