using Content.Shared.Medical.SuitSensors;
using System.Linq;
using System.Numerics;
using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.Medical.SuitSensors;
using Content.Server.Radio.EntitySystems;
using Content.Server.StationRecords.Systems;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Chat;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationRecords;
using Robust.Shared.Map.Components;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 2: understanding speech and opening airlocks on request.
/// <para>Heard lines (radio + holopads, never whispers) are queued and understood on the next tick. The same
/// utterance from the same speaker arriving on several channels within
/// <see cref="PulsarAutonomousAiComponent.UtteranceDedupeWindow"/> is understood once (first-match-wins:
/// announcements, then bolts, clarifications, door open/close, armory follow-up, questions, small talk,
/// bare name, not recognized). A line is for
/// the module when it names it (<see cref="PulsarLexiconPrototype.Names"/>) or when the same person addressed it
/// within <see cref="PulsarLexiconPrototype.ConversationWindow"/> (or has an armory request waiting).
/// A request has an "open" word and a target: a room from the <see cref="PulsarRoomPrototype"/> table, "this door"
/// next to the speaker, or a station beacon name. The door is checked against the rules (README) and opened the
/// way the AI player opens it: a remote open with the AI's own checks (AI wire, power, bolts, welding).</para>
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly SuitSensorSystem _sensors = default!;

    private sealed class LexiconCache
    {
        public PulsarLexiconPrototype Proto = default!;
        public List<PulsarSpeech.Phrase> Names = default!;
        public List<PulsarSpeech.Phrase> Open = default!;
        public List<PulsarSpeech.Phrase> Here = default!;
        public List<PulsarSpeech.Phrase> Threat = default!;
        public List<PulsarSpeech.Phrase> Stop = default!;
        public List<PulsarSpeech.Phrase> Bolt = default!;
        public List<PulsarSpeech.Phrase> Unbolt = default!;
        public List<PulsarSpeech.Phrase> EnemyWords = default!;
        public List<PulsarSpeech.Phrase> Where = default!;
        public List<PulsarSpeech.Phrase> AlertQuestion = default!;
        public List<PulsarSpeech.Phrase> ShiftQuestion = default!;
        public List<PulsarSpeech.Phrase> Heads = default!;
        public List<PulsarSpeech.Phrase> Security = default!;
        public List<PulsarSpeech.Phrase> TimeQuestion = default!;
        public List<(string Job, PulsarSpeech.Phrase Phrase)> JobWords = default!;

        // Stage 4
        public List<PulsarSpeech.Phrase> Close = default!;
        public List<PulsarSpeech.Phrase> DoorWords = default!;
        public List<PulsarSpeech.Phrase> Announce = default!;
        public List<PulsarSpeech.Phrase> AnnounceTrim = default!;
        public List<(PulsarDirection Dir, PulsarSpeech.Phrase Phrase)> Directions = default!;
        public List<List<PulsarSpeech.Phrase>> Ordinals = default!;
        public List<(PulsarChatTopic Topic, List<PulsarSpeech.Phrase> Phrases)> Chat = default!;
    }

    private sealed class RoomCache
    {
        public PulsarRoomPrototype Proto = default!;
        public List<PulsarSpeech.Phrase> Keywords = default!;
        public List<PulsarSpeech.Phrase> Beacons = default!;
        public List<PulsarSpeech.Phrase> DoorNames = default!;
    }

    private readonly Dictionary<string, LexiconCache> _lexicons = new();
    private List<RoomCache>? _rooms;
    private Dictionary<string, string>? _jobByIcon;

    private enum Verdict : byte
    {
        Allow,
        Deny,
        AskThreat,
    }

    private void ClearSpeechCaches()
    {
        _lexicons.Clear();
        _rooms = null;
        _jobByIcon = null;
    }

    private LexiconCache GetLexicon(PulsarAutonomousAiComponent comp)
    {
        if (_lexicons.TryGetValue(comp.Lexicon, out var cache))
            return cache;

        var proto = _proto.Index(comp.Lexicon);
        cache = new LexiconCache
        {
            Proto = proto,
            Names = PulsarSpeech.Parse(proto.Names, strict: true),
            Open = PulsarSpeech.Parse(proto.OpenWords),
            Here = PulsarSpeech.Parse(proto.HereWords),
            Threat = PulsarSpeech.Parse(proto.ThreatPhrases),
            Stop = PulsarSpeech.Parse(proto.StopWords),
            Bolt = PulsarSpeech.Parse(proto.BoltWords),
            Unbolt = PulsarSpeech.Parse(proto.UnboltWords),
            EnemyWords = PulsarSpeech.Parse(proto.EnemyWords),
            Where = PulsarSpeech.Parse(proto.WhereWords),
            AlertQuestion = PulsarSpeech.Parse(proto.AlertQuestion),
            ShiftQuestion = PulsarSpeech.Parse(proto.ShiftQuestion),
            Heads = PulsarSpeech.Parse(proto.HeadsWords),
            Security = PulsarSpeech.Parse(proto.SecurityWords),
            TimeQuestion = PulsarSpeech.Parse(proto.TimeQuestion),
            JobWords = proto.JobWords
                .SelectMany(kv => PulsarSpeech.Parse(kv.Value).Select(p => (kv.Key.Id, p)))
                .ToList(),
            Close = PulsarSpeech.Parse(proto.CloseWords),
            DoorWords = PulsarSpeech.Parse(proto.DoorWords),
            Announce = PulsarSpeech.Parse(proto.AnnounceWords),
            AnnounceTrim = PulsarSpeech.Parse(proto.AnnounceTrimWords),
            Directions = new (PulsarDirection, List<string>)[]
                {
                    (PulsarDirection.North, proto.NorthWords), (PulsarDirection.South, proto.SouthWords),
                    (PulsarDirection.West, proto.WestWords), (PulsarDirection.East, proto.EastWords),
                    (PulsarDirection.Left, proto.LeftWords), (PulsarDirection.Right, proto.RightWords),
                    (PulsarDirection.Front, proto.FrontWords), (PulsarDirection.Back, proto.BackWords),
                }
                .SelectMany(d => PulsarSpeech.Parse(d.Item2).Select(p => (d.Item1, p)))
                .ToList(),
            Ordinals = proto.OrdinalWords.Select(o => PulsarSpeech.Parse(o)).ToList(),
            Chat = proto.ChatTopics.Select(t => (t, PulsarSpeech.Parse(t.Phrases))).ToList(),
        };
        _lexicons[comp.Lexicon] = cache;
        return cache;
    }

    private List<RoomCache> GetRooms()
    {
        if (_rooms != null)
            return _rooms;

        _rooms = new List<RoomCache>();
        foreach (var room in _proto.EnumeratePrototypes<PulsarRoomPrototype>())
        {
            _rooms.Add(new RoomCache
            {
                Proto = room,
                Keywords = PulsarSpeech.Parse(room.Keywords),
                Beacons = PulsarSpeech.Parse(room.Beacons),
                DoorNames = PulsarSpeech.Parse(room.DoorNames),
            });
        }

        return _rooms;
    }

    // ---- public API ----

    /// <summary>Does the line address the module by one of its names (whole-word rules applied)?</summary>
    public bool IsAddressed(Entity<PulsarAutonomousAiComponent> ent, string message)
    {
        return PulsarSpeech.Any(PulsarSpeech.Tokenize(message), GetLexicon(ent.Comp).Names);
    }

    /// <summary>
    /// Queues a line for understanding, as if it was heard. Channel = it came by radio (the reply goes to the same
    /// channel), holopad = heard at that holopad (the reply is spoken there). Used by hearing and pulsar_request.
    /// </summary>
    public void HandleSpeech(Entity<PulsarAutonomousAiComponent> ent, EntityUid speaker, string message, string? channel = null, EntityUid? holopad = null)
    {
        if (ent.Comp.State != PulsarState.Active)
            return;

        var incoming = ent.Comp.Incoming;
        var norm = PulsarSpeech.Normalize(message);
        for (var i = 0; i < incoming.Count; i++)
        {
            var line = incoming[i];
            if (line.Speaker != speaker || PulsarSpeech.Normalize(line.Message) != norm)
                continue;

            // Same utterance already queued (radio + holopad in one tick): keep one line, remember both routes.
            incoming[i] = line with
            {
                Channel = line.Channel ?? channel,
                Holopad = line.Holopad ?? holopad,
            };
            return;
        }

        incoming.Add(new PulsarIncomingLine(speaker, message, channel, holopad));
    }

    private void ProcessIncoming(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        FlushDeferredReplies(ent, now);
        if (comp.Incoming.Count == 0)
            return;

        var lines = comp.Incoming.ToArray();
        comp.Incoming.Clear();
        ForgetOldUtterances(comp, now);

        foreach (var line in lines)
        {
            if (comp.State != PulsarState.Active)
                return;

            if (AlreadyHeardUtterance(comp, line, now))
                continue;

            RememberUtterance(comp, line, now);
            Understand(ent, line, now);
        }
    }

    private static bool AlreadyHeardUtterance(PulsarAutonomousAiComponent comp, PulsarIncomingLine line, TimeSpan now)
    {
        return comp.RecentUtterances.TryGetValue((line.Speaker, PulsarSpeech.Normalize(line.Message)), out var seen)
               && now - seen <= comp.UtteranceDedupeWindow;
    }

    private static void RememberUtterance(PulsarAutonomousAiComponent comp, PulsarIncomingLine line, TimeSpan now)
    {
        comp.RecentUtterances[(line.Speaker, PulsarSpeech.Normalize(line.Message))] = now;
    }

    private static void ForgetOldUtterances(PulsarAutonomousAiComponent comp, TimeSpan now)
    {
        if (comp.RecentUtterances.Count == 0)
            return;

        foreach (var (key, seen) in comp.RecentUtterances.ToArray())
        {
            if (now - seen > comp.UtteranceDedupeWindow)
                comp.RecentUtterances.Remove(key);
        }
    }

    private void Understand(Entity<PulsarAutonomousAiComponent> ent, PulsarIncomingLine line, TimeSpan now)
    {
        var comp = ent.Comp;
        var speaker = line.Speaker;

        // Only people: no AIs, borgs, holopads or itself.
        if (TerminatingOrDeleted(speaker) || speaker == ent.Owner || !HasComp<HumanoidAppearanceComponent>(speaker)
            || HasComp<StationAiHeldComponent>(speaker))
            return;

        var lex = GetLexicon(comp);
        var tokens = PulsarSpeech.Tokenize(line.Message);
        // An empty line is the wake-up after pointing (see NotePointing).
        if (tokens.Count == 0 && !comp.Clarifications.ContainsKey(speaker))
            return;

        // Forget stale armory requests and questions.
        if (comp.PendingArmory.TryGetValue(speaker, out var pending) && pending.Expires < now)
        {
            comp.PendingArmory.Remove(speaker);
            pending = default;
        }

        if (comp.Clarifications.TryGetValue(speaker, out var clar) && clar.Expires < now)
        {
            comp.Clarifications.Remove(speaker);
            clar = null;
        }

        var hasPending = pending.Door.IsValid();
        var named = PulsarSpeech.Any(tokens, lex.Names);
        var talking = comp.Conversations.TryGetValue(speaker, out var until) && until >= now;

        if (!named && !talking && !hasPending && clar == null)
            return;

        comp.Conversations[speaker] = now + lex.Proto.ConversationWindow;

        var open = PulsarSpeech.Any(tokens, lex.Open);
        var close = !open && PulsarSpeech.Any(tokens, lex.Close);
        var threat = PulsarSpeech.Any(tokens, lex.Threat);
        var ctx = new RequestContext(speaker, line, SpeakerName(speaker), GetJob(speaker));

        // First-match-wins, one reply: announcements (the text may contain door words), then bolts,
        // answers to our own question, open/close, a pending armory threat, questions, small talk,
        // a door named without a verb, the name alone, or "not understood".
        if (PulsarSpeech.Any(tokens, lex.Announce))
        {
            comp.Clarifications.Remove(speaker);
            HandleAnnounceRequest(ent, ctx, lex, tokens, now);
            return;
        }

        // Stage 3: bolting (checked before "open"/"close": «заблокируй шлюзы», «закрой на болты»).
        var unbolt = PulsarSpeech.Any(tokens, lex.Unbolt);
        if (unbolt || PulsarSpeech.Any(tokens, lex.Bolt))
        {
            comp.Clarifications.Remove(speaker);
            HandleBoltRequest(ent, ctx, lex, tokens, bolt: !unbolt, now);
            return;
        }

        // Stage 4: the answer to its own question, no name needed.
        if (clar != null && !open && !close)
        {
            if (clar.Kind == PulsarClarifyKind.Announce)
            {
                comp.Clarifications.Remove(speaker);
                HandleAnnounceText(ent, ctx, lex, StripNames(lex, line.Message), now);
                return;
            }

            if (TryAnswerClarification(ent, ctx, lex, clar, tokens, threat, now))
                return;
        }

        // Stage 5.3: «не трогай аномалию» from science.
        if (clar == null && TryAnomalyRequest(ent, ctx, lex, tokens, now))
            return;

        // Stage 5: units linked to the AI («пришли медибота в медбей», «закрепи борга…», «отзови…»).
        if (clar == null && TryUnitRequest(ent, ctx, lex, tokens, now))
            return;

        if (open || close)
        {
            comp.Clarifications.Remove(speaker);
            HandleDoorRequest(ent, ctx, lex, tokens, close ? PulsarClarifyKind.Close : PulsarClarifyKind.Open, threat, now);
            return;
        }

        if (hasPending)
        {
            if (threat)
            {
                comp.PendingArmory.Remove(speaker);
                OpenDoor(ent, ctx, pending.Door, pending.Target, "armory-threat-named", now);
            }
            else if (named)
            {
                Decide(ent, ctx, pending.Target, pending.Door, PulsarDecisionOutcome.AskThreat, "armory-low-alert", "pulsar-reply-specify-threat", now);
            }

            return;
        }

        // Stage 3: questions.
        if (TryAnswer(ent, ctx, lex, tokens, now))
            return;

        // Stage 4: small talk.
        if (TryChat(ent, ctx, lex, tokens, now))
            return;

        // «Шлюз в техническое»: a door word and a place without a verb.
        if (PulsarSpeech.Any(tokens, lex.DoorWords) && clar == null)
        {
            HandleDoorRequest(ent, ctx, lex, tokens, PulsarClarifyKind.Open, threat, now);
            return;
        }

        if (!named || clar != null)
            return;

        // The name alone: "on air". Anything else it did not get: ask to rephrase.
        if (OnlyAddress(lex, tokens))
        {
            if (!comp.NextAck.TryGetValue(speaker, out var nextAck) || nextAck <= now)
            {
                comp.NextAck[speaker] = now + comp.AckCooldown;
                Decide(ent, ctx, null, null, PulsarDecisionOutcome.Acknowledged, "called", PickReply(lex.Proto.AckReplies, "pulsar-reply-ack"), now);
            }

            return;
        }

        if (!ChatAllowed(ent, ctx, lex, now))
            return;
        Decide(ent, ctx, null, null, PulsarDecisionOutcome.NotUnderstood, "not-understood",
            PickReply(lex.Proto.UnknownReplies, "pulsar-reply-not-understood"), now);
    }

    private readonly record struct RequestContext(EntityUid Speaker, PulsarIncomingLine Line, string Name, string? Job);

    private PulsarBeacon? NearestBeacon(PulsarAutonomousAiComponent comp, LexiconCache lex, EntityUid grid, Vector2i tile)
    {
        PulsarBeacon? best = null;
        var bestDist = lex.Proto.BeaconRange * lex.Proto.BeaconRange;
        foreach (var beacon in comp.Beacons)
        {
            if (beacon.Grid != grid)
                continue;
            var d = (beacon.Tile - tile).LengthSquared;
            if (d > bestDist)
                continue;
            bestDist = d;
            best = beacon;
        }

        return best;
    }

    private void RefreshBeacons(Entity<PulsarAutonomousAiComponent> ent)
    {
        var comp = ent.Comp;
        comp.Beacons.Clear();

        var query = EntityQueryEnumerator<Content.Shared.Pinpointer.NavMapBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beacon, out var xform))
        {
            if (!beacon.Enabled || xform.GridUid is not { } grid || !comp.StationGrids.Contains(grid)
                || !_gridQuery.TryComp(grid, out var gridComp))
                continue;

            var name = beacon.Text ?? (beacon.DefaultText is { } loc ? Loc.GetString(loc) : Name(uid));
            if (string.IsNullOrWhiteSpace(name))
                continue;

            comp.Beacons.Add(new PulsarBeacon(uid, grid, _maps.LocalToTile(grid, gridComp, xform.Coordinates), name,
                PulsarSpeech.Tokenize(name).ToArray()));
        }
    }

    // ---- rules ----

    private (Verdict, string Reason, string Reply) Authorize(
        Entity<PulsarAutonomousAiComponent> ent,
        LexiconCache lex,
        EntityUid speaker,
        string? job,
        EntityUid door,
        bool threat)
    {
        // Stage 3: the laws come first.
        var policy = ent.Comp.Policy;
        if (IsMaster(policy, speaker))
            return (Verdict.Allow, "law-master", string.Empty);

        if (policy.Kind is PulsarLawPolicyKind.Hostile or PulsarLawPolicyKind.ObeySyndicate)
        {
            return IsWearingEnemySuit(ent.Comp, speaker)
                ? (Verdict.Allow, "law-syndicate", string.Empty)
                : (Verdict.Deny, "law-refuse", "pulsar-reply-law-refuse");
        }

        if (!policy.ServesCrew)
            return (Verdict.Deny, "law-not-crew", "pulsar-reply-law-refuse");

        // Rule 1: the requester's own ID opens it.
        if (_accessReader.IsAllowed(speaker, door))
            return (Verdict.Allow, "own-access", string.Empty);

        var access = DoorAccess(door);
        var station = _station.GetOwningStation(ent);
        var proto = lex.Proto;

        // Rule 2: captain's quarters for department heads.
        if (access.Any(set => set.Contains(proto.CaptainAccess.Id)) && job != null && proto.HeadJobs.Contains(job))
        {
            if (!InManifest(station, proto.CaptainJob))
                return (Verdict.Allow, "captain-not-in-manifest", string.Empty);

            if (IsRedOrHigher(station, proto) && IsCaptainDeadOnSensors(proto))
                return (Verdict.Allow, "red-alert-captain-dead", string.Empty);

            return (Verdict.Deny, "captain-on-shift", "pulsar-reply-captain-on-shift");
        }

        // Rule 3: the armory for security when neither the warden nor the HoS is on shift.
        if (access.Any(set => set.Contains(proto.ArmoryAccess.Id)) && job != null && IsSecurity(proto, job))
        {
            if (InManifest(station, proto.WardenJob) || InManifest(station, proto.HeadOfSecurityJob))
                return (Verdict.Deny, "warden-or-hos-on-shift", "pulsar-reply-ask-warden");

            if (IsRedOrHigher(station, proto))
                return (Verdict.Allow, "armory-red-or-higher", string.Empty);

            if (threat)
                return (Verdict.Allow, "armory-threat-named", string.Empty);

            return (Verdict.AskThreat, "armory-low-alert", "pulsar-reply-specify-threat");
        }

        return (Verdict.Deny, "no-access", "pulsar-reply-no-access");
    }

    private List<HashSet<string>> DoorAccess(EntityUid door)
    {
        var result = new List<HashSet<string>>();
        if (_accessReader.GetMainAccessReader(door, out var reader))
        {
            foreach (var set in reader.Value.Comp.AccessLists)
                result.Add(set.Select(a => a.Id).ToHashSet());
        }

        return result;
    }

    private bool IsSecurity(PulsarLexiconPrototype proto, string job)
    {
        return _proto.TryIndex(proto.SecurityDepartment, out var dept) && dept.Roles.Any(r => r.Id == job);
    }

    /// <summary>Is someone with this job in the station's crew manifest (general station records)?</summary>
    public bool InManifest(EntityUid? station, string job)
    {
        if (station == null || !HasComp<Content.Shared.StationRecords.StationRecordsComponent>(station))
            return false;

        foreach (var (_, record) in _records.GetRecordsOfType<GeneralStationRecord>(station.Value))
        {
            if (record.JobPrototype == job)
                return true;
        }

        return false;
    }

    /// <summary>Red or any level not listed as low (gamma, delta, epsilon, unknown ones).</summary>
    private bool IsRedOrHigher(EntityUid? station, PulsarLexiconPrototype proto)
    {
        if (station == null || !TryComp<AlertLevelComponent>(station, out var alert) || string.IsNullOrEmpty(alert.CurrentLevel))
            return false;

        return !proto.LowAlertLevels.Contains(alert.CurrentLevel.ToLowerInvariant());
    }

    /// <summary>A captain's suit sensor (by the captain job icon) is on and reports them dead. Sensors off = not dead.</summary>
    private bool IsCaptainDeadOnSensors(PulsarLexiconPrototype proto)
    {
        if (!_proto.TryIndex(proto.CaptainJob, out var captain))
            return false;

        var icon = captain.Icon.Id;
        var query = EntityQueryEnumerator<SuitSensorComponent>();
        while (query.MoveNext(out var uid, out var sensor))
        {
            if (sensor.User == null || sensor.Mode == SuitSensorMode.SensorOff)
                continue;

            if (_sensors.GetSensorState((uid, sensor, null)) is { } status && status.JobIcon == icon && !status.IsAlive)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The speaker's job: from the station record of their ID, the ID's job, the job icon on the ID (what the AI
    /// sees) or, without an ID, their mind's job.
    /// </summary>
    public string? GetJob(EntityUid speaker)
    {
        if (_idCard.TryFindIdCard(speaker, out var id))
        {
            if (TryComp<StationRecordKeyStorageComponent>(id, out var keyStorage) && keyStorage.Key is { } key
                && _records.TryGetRecord<GeneralStationRecord>(key, out var record) && !string.IsNullOrEmpty(record.JobPrototype))
                return record.JobPrototype;

            if (id.Comp.JobPrototype is { } jobProto)
                return jobProto.Id;

            if (JobByIcon(id.Comp.JobIcon) is { } byIcon)
                return byIcon;

            return null;
        }

        if (_minds.TryGetMind(speaker, out var mindId, out _) && _jobs.MindTryGetJobId(mindId, out var mindJob) && mindJob != null)
            return mindJob.Value.Id;

        return null;
    }

    private string? JobByIcon(string icon)
    {
        if (_jobByIcon == null)
        {
            _jobByIcon = new Dictionary<string, string>();
            foreach (var job in _proto.EnumeratePrototypes<JobPrototype>().OrderBy(j => j.ID))
                _jobByIcon.TryAdd(job.Icon.Id, job.ID);
        }

        return _jobByIcon.GetValueOrDefault(icon);
    }

    // ---- acting ----

    private void OpenDoor(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, EntityUid door, string target, string reason, TimeSpan now)
    {
        if (TerminatingOrDeleted(door) || !_doorQuery.TryComp(door, out var doorComp))
        {
            Decide(ent, ctx, target, door, PulsarDecisionOutcome.NoTarget, "door-gone", "pulsar-reply-no-target", now);
            return;
        }

        string? problem = null;
        string? problemReply = null;

        if (doorComp.State is DoorState.Open or DoorState.Opening)
        {
            Decide(ent, ctx, target, door, PulsarDecisionOutcome.AlreadyOpen, reason, "pulsar-reply-already-open", now);
            return;
        }

        if (doorComp.State == DoorState.Welded)
            (problem, problemReply) = ("welded", "pulsar-reply-welded");
        else if (!TryComp<StationAiWhitelistComponent>(door, out var whitelist) || !whitelist.Enabled)
            (problem, problemReply) = ("ai-control-cut", "pulsar-reply-no-control");
        else if (!_power.IsPowered(door))
            (problem, problemReply) = ("no-power", "pulsar-reply-no-power");
        else if (_boltQuery.TryComp(door, out var bolts) && bolts.BoltsDown)
            (problem, problemReply) = ("bolted", "pulsar-reply-bolted");

        // Same as the AI player clicking the door: no user, so no ID check, but power/bolts/welding still apply.
        if (problem == null && !_doors.TryOpen(door, doorComp, user: null))
            (problem, problemReply) = ("open-failed", "pulsar-reply-open-failed");

        if (problem != null)
        {
            Decide(ent, ctx, target, door, PulsarDecisionOutcome.NotControllable, $"{reason}; {problem}", problemReply!, now, ("target", target));
            return;
        }

        Decide(ent, ctx, target, door, PulsarDecisionOutcome.Opened, reason, "pulsar-reply-opening", now, ("target", target));
    }

    private void Decide(
        Entity<PulsarAutonomousAiComponent> ent,
        RequestContext ctx,
        string? target,
        EntityUid? door,
        PulsarDecisionOutcome outcome,
        string reason,
        string replyLoc,
        TimeSpan now,
        params (string, object)[] args)
    {
        string? reply = null;
        if (!string.IsNullOrEmpty(replyLoc))
        {
            if (args.Length == 0 && target != null)
                args = new (string, object)[] { ("target", target) };
            reply = Loc.GetString(replyLoc, args);
            if (!Reply(ent, ctx, reply, now, canWait: outcome is not (PulsarDecisionOutcome.Acknowledged or PulsarDecisionOutcome.NotUnderstood)))
                reply = null;
        }

        var comp = ent.Comp;
        comp.Decisions.Add(new PulsarDecision(now, ctx.Speaker, ctx.Name, ctx.Job, ctx.Line.Message, target, door, outcome, reason, reply));
        var excess = comp.Decisions.Count - Math.Max(1, comp.DecisionLimit);
        if (excess > 0)
            comp.Decisions.RemoveRange(0, excess);
    }

    /// <summary>
    /// Says the reply where the request came from: same radio channel, the holopad, or the core. Rate-limited per
    /// speaker and globally; a limited reply waits (latest per speaker) instead of being lost, except plain
    /// acknowledgements, which are just dropped.
    /// </summary>
    private bool Reply(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, string text, TimeSpan now, bool canWait)
    {
        var comp = ent.Comp;
        if (!CanReply(comp, ctx.Speaker, now))
        {
            if (!canWait)
                return false;

            comp.DeferredReplies[ctx.Speaker] = new PulsarDeferredReply(text, ctx.Line.Channel, ctx.Line.Holopad, now + comp.DeferredReplyTime);
            return true;
        }

        comp.DeferredReplies.Remove(ctx.Speaker);
        Say(ent, ctx.Speaker, text, ctx.Line.Channel, ctx.Line.Holopad, now);
        return true;
    }

    private static bool CanReply(PulsarAutonomousAiComponent comp, EntityUid speaker, TimeSpan now)
    {
        if (comp.NextReply.TryGetValue(speaker, out var next) && next > now)
            return false;

        comp.RecentReplies.RemoveAll(t => now - t > comp.GlobalReplyWindow);
        return comp.RecentReplies.Count < comp.GlobalReplyLimit;
    }

    private void FlushDeferredReplies(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.DeferredReplies.Count == 0)
            return;

        foreach (var (speaker, reply) in comp.DeferredReplies.ToArray())
        {
            if (reply.Expires < now)
            {
                comp.DeferredReplies.Remove(speaker);
                continue;
            }

            if (!CanReply(comp, speaker, now))
                continue;

            comp.DeferredReplies.Remove(speaker);
            Say(ent, speaker, reply.Text, reply.Channel, reply.Holopad, now);
        }
    }

    private void Say(Entity<PulsarAutonomousAiComponent> ent, EntityUid speaker, string text, string? channel, EntityUid? holopad, TimeSpan now)
    {
        var comp = ent.Comp;
        comp.NextReply[speaker] = now + comp.ReplyCooldown;
        comp.RecentReplies.Add(now);

        if (channel != null)
        {
            _radio.SendRadioMessage(ent, text, channel, ent);
            return;
        }

        var source = holopad is { } pad && !TerminatingOrDeleted(pad) ? pad : ent.Owner;
        _chat.TrySendInGameICMessage(source, text, InGameICChatType.Speak, ChatTransmitRange.Normal,
            nameOverride: Name(ent), checkRadioPrefix: false, ignoreActionBlocker: true);
    }
}
