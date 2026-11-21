using System.Linq;
using System.Numerics;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Doors.Components;
using Content.Shared.Pointing;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 4: which door the person means.
/// <list type="number">
/// <item>Pointing: a door the person pointed at (the pointing seen on camera) within
/// <see cref="PulsarLexiconPrototype.PointWindow"/>, or right after Pulsar asked which door.</item>
/// <item>Names: a room from the table, a station beacon name, the door's own name.</item>
/// <item>«Этот / рядом со мной / передо мной»: the nearest door next to the person, the one they face first.</item>
/// <item>Directions: «северный / южный / западный / восточный» (station map, north up) and
/// «левый / правый / передний / задний» (where the person faces). With a room: the door of that room lying that way.</item>
/// </list>
/// Several doors in different places fit and none is right next to the person: Pulsar asks
/// («Уточните: шлюз у отметки «Медотсек» или шлюз у отметки «Хим. лаборатория»?») and takes the answer of the same
/// person for <see cref="PulsarLexiconPrototype.ClarifyWindow"/> without the name: a place word, «первый / второй»,
/// a direction, «этот», or pointing at the door.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    private const int MaxOptions = 3;

    // English door names as tokenized (Latin lookalikes become Cyrillic).
    private static readonly string Maint = PulsarSpeech.Normalize("maint");
    private static readonly string Hatch = PulsarSpeech.Normalize("hatch");

    private enum TargetResult : byte
    {
        Found,
        Ambiguous,
        Fail,
    }

    private readonly record struct DoorCandidate(EntityUid Door, PulsarDoorInfo Info, EntityUid Grid, PulsarGridMemory Mem, int Score);

    private void InitializeTargeting()
    {
        SubscribeLocalEvent<DoorComponent, AfterGotPointedAtEvent>(OnDoorPointedAt);
    }

    // ---- pointing ----

    private void OnDoorPointedAt(Entity<DoorComponent> door, ref AfterGotPointedAtEvent args)
    {
        NotePointing(args.Pointer, door);
    }

    /// <summary>
    /// Someone pointed at a door. Every active module that knows the door and sees the person on camera remembers
    /// it; an open "which door?" question of that person is answered on the next tick.
    /// </summary>
    public void NotePointing(EntityUid pointer, EntityUid door)
    {
        var now = _timing.CurTime;
        var doorXform = Transform(door);
        if (doorXform.GridUid is not { } grid || !_gridQuery.TryComp(grid, out var gridComp))
            return;

        var doorTile = _maps.LocalToTile(grid, gridComp, doorXform.Coordinates);
        var query = EntityQueryEnumerator<PulsarAutonomousAiComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.State != PulsarState.Active || !comp.Grids.TryGetValue(grid, out var mem)
                || !mem.Doors.TryGetValue(doorTile, out var info) || info.Door != door)
                continue;

            if (!IsOnCamera(comp, pointer))
                continue;

            comp.RecentPoints[pointer] = new PulsarPoint(door, now);
            if (comp.Clarifications.TryGetValue(pointer, out var clar) && clar.Kind != PulsarClarifyKind.Announce && clar.Expires >= now)
                comp.Incoming.Add(new PulsarIncomingLine(pointer, string.Empty, clar.Line.Channel, clar.Line.Holopad));
        }
    }

    private bool IsOnCamera(PulsarAutonomousAiComponent comp, EntityUid uid)
    {
        var xform = Transform(uid);
        return xform.GridUid is { } grid && comp.Grids.TryGetValue(grid, out var mem)
               && _gridQuery.TryComp(grid, out var gridComp)
               && mem.IsSeen(_maps.LocalToTile(grid, gridComp, xform.Coordinates));
    }

    private EntityUid? FreshPoint(PulsarAutonomousAiComponent comp, LexiconCache lex, EntityUid speaker, TimeSpan now, TimeSpan? since = null)
    {
        if (!comp.RecentPoints.TryGetValue(speaker, out var point))
            return null;

        if (now - point.Time > lex.Proto.PointWindow || since != null && point.Time < since.Value || TerminatingOrDeleted(point.Door))
        {
            if (now - point.Time > lex.Proto.PointWindow)
                comp.RecentPoints.Remove(speaker);
            return null;
        }

        return point.Door;
    }

    // ---- requests ----

    private void HandleDoorRequest(
        Entity<PulsarAutonomousAiComponent> ent,
        RequestContext ctx,
        LexiconCache lex,
        List<string> tokens,
        PulsarClarifyKind kind,
        bool threat,
        TimeSpan now)
    {
        var top = new List<EntityUid>();
        var result = ResolveTarget(ent, lex, tokens, ctx.Speaker, now, true, out var door, out var target,
            out var failReason, out var failReply, out var options, top);

        switch (result)
        {
            case TargetResult.Found:
                ActOnDoor(ent, ctx, kind, door, target, threat, now);
                if (kind == PulsarClarifyKind.Open && ent.Comp.Decisions.Count > 0
                    && ent.Comp.Decisions[^1] is { Outcome: PulsarDecisionOutcome.Opened } opened && opened.Door == door)
                    OpenPartners(door, top);
                return;
            case TargetResult.Ambiguous:
                AskWhich(ent, ctx, lex, kind, threat, options, now);
                return;
        }

        // Nothing named / nothing next to them: wait for the answer or for pointing.
        if (failReason is "no-target-named" or "no-door-near-speaker")
            Remember(ent, ctx, lex, kind, threat, new List<PulsarCandidate>(), now);

        var outcome = failReason is "no-target-named" or "no-door-near-speaker"
            ? PulsarDecisionOutcome.AskTarget
            : PulsarDecisionOutcome.NoTarget;
        Decide(ent, ctx, target, null, outcome, failReason, failReply, now);
    }

    /// <summary>
    /// «Открой мостик»: the other leaf of a double entrance (doors of the same best tier right next to the opened one)
    /// opens too, so the way in is really open.
    /// </summary>
    private void OpenPartners(EntityUid door, List<EntityUid> top)
    {
        var pos = _xforms.GetWorldPosition(door);
        foreach (var other in top)
        {
            if (other == door || TerminatingOrDeleted(other) || !_doorQuery.TryComp(other, out var otherDoor)
                || (_xforms.GetWorldPosition(other) - pos).Length() > 3.2f
                || otherDoor.State != DoorState.Closed
                || !TryComp<StationAiWhitelistComponent>(other, out var whitelist) || !whitelist.Enabled
                || !_power.IsPowered(other)
                || _boltQuery.TryComp(other, out var bolts) && bolts.BoltsDown)
                continue;
            _doors.TryOpen(other, otherDoor, user: null);
        }
    }

    private void Remember(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, PulsarClarifyKind kind,
        bool threat, List<PulsarCandidate> options, TimeSpan now)
    {
        ent.Comp.Clarifications[ctx.Speaker] = new PulsarClarification
        {
            Kind = kind,
            Candidates = options,
            Threat = threat,
            Expires = now + lex.Proto.ClarifyWindow,
            Line = ctx.Line,
        };
    }

    private void AskWhich(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, PulsarClarifyKind kind,
        bool threat, List<PulsarCandidate> options, TimeSpan now)
    {
        Remember(ent, ctx, lex, kind, threat, options, now);
        var list = string.Join(Loc.GetString("pulsar-clarify-or"), options.Select(o => o.Label));
        Decide(ent, ctx, null, options[0].Door, PulsarDecisionOutcome.Clarify, "ambiguous", "pulsar-reply-clarify", now,
            ("options", list));
    }

    private void ActOnDoor(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, PulsarClarifyKind kind, EntityUid door,
        string target, bool threat, TimeSpan now)
    {
        var comp = ent.Comp;
        var lex = GetLexicon(comp);
        if (kind == PulsarClarifyKind.Close)
        {
            var (allowed, closeReason, closeReply) = AuthorizeClose(ent, lex, ctx.Speaker, ctx.Job, door);
            if (allowed)
                CloseDoor(ent, ctx, door, target, closeReason, now);
            else
                Decide(ent, ctx, target, door, PulsarDecisionOutcome.Denied, closeReason, closeReply, now);
            return;
        }

        var (verdict, reason, reply) = Authorize(ent, lex, ctx.Speaker, ctx.Job, door, threat);
        switch (verdict)
        {
            case Verdict.Allow:
                comp.PendingArmory.Remove(ctx.Speaker);
                OpenDoor(ent, ctx, door, target, reason, now);
                break;
            case Verdict.AskThreat:
                comp.PendingArmory[ctx.Speaker] = new PulsarPendingArmory(door, target, now + lex.Proto.PendingArmoryTime, ctx.Line.Channel, ctx.Line.Holopad);
                Decide(ent, ctx, target, door, PulsarDecisionOutcome.AskThreat, reason, reply, now);
                break;
            default:
                Decide(ent, ctx, target, door, PulsarDecisionOutcome.Denied, reason, reply, now);
                break;
        }
    }

    /// <summary>
    /// The answer to «какой шлюз?». True when it was one (the door is then acted on, or the question asked again
    /// with fewer options).
    /// </summary>
    private bool TryAnswerClarification(
        Entity<PulsarAutonomousAiComponent> ent,
        RequestContext ctx,
        LexiconCache lex,
        PulsarClarification clar,
        List<string> tokens,
        bool threat,
        TimeSpan now)
    {
        var comp = ent.Comp;
        var speaker = ctx.Speaker;
        var asked = clar.Expires - lex.Proto.ClarifyWindow;
        threat |= clar.Threat;

        // 1. Pointing after the question.
        if (FreshPoint(comp, lex, speaker, now, asked) is { } pointed)
        {
            comp.Clarifications.Remove(speaker);
            ActOnDoor(ent, ctx, clar.Kind, pointed, DoorLabel(ent, pointed), threat, now);
            return true;
        }

        // The pointing wake-up line has no words: nothing else to look at.
        if (tokens.Count == 0)
            return true;

        if (clar.Candidates.Count > 0)
        {
            var picked = PickCandidate(ent, lex, clar.Candidates, tokens, speaker);
            if (picked.Count == 1)
            {
                comp.Clarifications.Remove(speaker);
                ActOnDoor(ent, ctx, clar.Kind, picked[0].Door, picked[0].Label, threat, now);
                return true;
            }

            if (picked.Count > 1 && picked.Count < clar.Candidates.Count)
            {
                AskWhich(ent, ctx, lex, clar.Kind, threat, picked, now);
                return true;
            }
        }

        // 2. A new description of the door («оружейная», «этот», «северный»).
        if (!LooksLikeTarget(lex, tokens))
            return false;

        var result = ResolveTarget(ent, lex, tokens, speaker, now, true, out var door, out var target, out _, out _, out var options);
        switch (result)
        {
            case TargetResult.Found:
                comp.Clarifications.Remove(speaker);
                ActOnDoor(ent, ctx, clar.Kind, door, target, threat, now);
                return true;
            case TargetResult.Ambiguous:
                AskWhich(ent, ctx, lex, clar.Kind, threat, options, now);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A short line with a place, a direction or "this one" in it (not just any talk on the radio).</summary>
    private bool LooksLikeTarget(LexiconCache lex, List<string> tokens)
    {
        var words = ContentWords(lex, tokens);
        if (words.Count > 5)
            return false;

        if (PulsarSpeech.Any(tokens, lex.Here) || ParseDirection(lex, tokens) != PulsarDirection.None)
            return true;

        foreach (var room in GetRooms())
        {
            if (PulsarSpeech.Any(tokens, room.Keywords))
                return true;
        }

        return words.Count > 0 && words.Count <= 3;
    }

    private List<PulsarCandidate> PickCandidate(Entity<PulsarAutonomousAiComponent> ent, LexiconCache lex, List<PulsarCandidate> candidates,
        List<string> tokens, EntityUid speaker)
    {
        // «Первый», «второй», «третий».
        for (var i = 0; i < lex.Ordinals.Count && i < candidates.Count; i++)
        {
            if (PulsarSpeech.Any(tokens, lex.Ordinals[i]))
                return new List<PulsarCandidate> { candidates[i] };
        }

        // Words of the place / door name.
        var words = ContentWords(lex, tokens);
        var byWord = candidates.Where(c => words.Any(w => c.Words.Any(cw => PulsarSpeech.SameWord(w, cw)))).ToList();
        if (byWord.Count > 0)
            return byWord;

        // A direction or "this one".
        var dir = ParseDirection(lex, tokens);
        if (dir != PulsarDirection.None)
        {
            var best = FilterByDirection(candidates.Select(c => (c, c.Grid, c.Tile)).ToList(), dir, speaker);
            if (best.Count > 0)
                return best;
        }

        if (PulsarSpeech.Any(tokens, lex.Here) && SpeakerPosition(speaker) is { } pos)
        {
            var near = candidates
                .Where(c => c.Grid == pos.Grid && (TileCenter(c.Tile) - pos.Local).Length() <= Math.Max(lex.Proto.HereRange, lex.Proto.ObviousRange))
                .OrderBy(c => (TileCenter(c.Tile) - pos.Local).LengthSquared())
                .Take(1)
                .ToList();
            if (near.Count > 0)
                return near;
        }

        return new List<PulsarCandidate>();
    }

    /// <summary>Tokens that can name a place: not names, verbs, door words, stop words, directions or "this".</summary>
    private List<string> ContentWords(LexiconCache lex, List<string> tokens)
    {
        return tokens.Where(t => t.Length >= 3
                                 && !PulsarSpeech.IsWord(t, lex.Stop)
                                 && !PulsarSpeech.IsWord(t, lex.Open)
                                 && !PulsarSpeech.IsWord(t, lex.Close)
                                 && !PulsarSpeech.IsWord(t, lex.DoorWords)
                                 && !PulsarSpeech.IsWord(t, lex.Names)
                                 && !PulsarSpeech.IsWord(t, lex.Here)
                                 && !lex.Directions.Any(d => d.Phrase.Words.Length == 1 && d.Phrase.WordMatches(0, t))
                                 && !lex.Ordinals.Any(o => PulsarSpeech.IsWord(t, o))).ToList();
    }

    // ---- resolving ----

    /// <summary>
    /// For tests: what a door request in <paramref name="line"/> resolves to: the result, the door taken and every
    /// door of the best tier.
    /// </summary>
    public (string Result, EntityUid Door, List<EntityUid> All, string Target) ResolveDoorRequest(
        Entity<PulsarAutonomousAiComponent> ent, EntityUid speaker, string line)
    {
        var lex = GetLexicon(ent.Comp);
        var all = new List<EntityUid>();
        var result = ResolveTarget(ent, lex, PulsarSpeech.Tokenize(line), speaker, _timing.CurTime, true, out var door,
            out var target, out var reason, out _, out var options, all);
        if (result == TargetResult.Ambiguous)
            all = options.Select(o => o.Door).ToList();
        return (result == TargetResult.Fail ? reason : result.ToString(), door, all, target);
    }

    /// <summary>Old entry point (bolting): every door of the best tier in <paramref name="all"/>, never asks.</summary>
    private bool TryResolveTarget(
        Entity<PulsarAutonomousAiComponent> ent,
        LexiconCache lex,
        List<string> tokens,
        EntityUid speaker,
        out EntityUid door,
        out string target,
        out string failReason,
        out string failReply,
        List<EntityUid>? all = null)
    {
        return ResolveTarget(ent, lex, tokens, speaker, _timing.CurTime, false, out door, out target, out failReason,
            out failReply, out _, all) == TargetResult.Found;
    }

    private TargetResult ResolveTarget(
        Entity<PulsarAutonomousAiComponent> ent,
        LexiconCache lex,
        List<string> tokens,
        EntityUid speaker,
        TimeSpan now,
        bool allowAsk,
        out EntityUid door,
        out string target,
        out string failReason,
        out string failReply,
        out List<PulsarCandidate> options,
        List<EntityUid>? all = null)
    {
        door = default;
        target = string.Empty;
        failReason = string.Empty;
        failReply = string.Empty;
        options = new List<PulsarCandidate>();

        var comp = ent.Comp;
        var proto = lex.Proto;
        var candidates = new List<DoorCandidate>();
        var dir = ParseDirection(lex, tokens);
        var here = PulsarSpeech.Any(tokens, lex.Here);
        var pointed = FreshPoint(comp, lex, speaker, now);

        // 1. A room from the table: the longest matching keyword wins.
        RoomCache? room = null;
        var best = 0;
        foreach (var r in GetRooms())
        {
            foreach (var keyword in r.Keywords)
            {
                if (keyword.Weight > best && keyword.Find(tokens) >= 0)
                {
                    best = keyword.Weight;
                    room = r;
                }
            }
        }

        var named = false;
        if (room != null)
        {
            named = true;
            target = Loc.GetString(room.Proto.Name);
            var spoken = ContentWords(lex, tokens);
            foreach (var (grid, mem) in comp.Grids)
            {
                foreach (var info in mem.Doors.Values)
                {
                    var score = RoomScore(comp, lex, room, grid, info, spoken);
                    if (score > 0)
                        candidates.Add(new DoorCandidate(info.Door, info, grid, mem, score));
                }
            }
        }
        else
        {
            // 2. A station beacon name («открой бар», «шлюз у хим лаборатории») or the door's own name.
            var words = ContentWords(lex, tokens);
            if (words.Count > 0)
            {
                var matched = new HashSet<EntityUid>();
                foreach (var beacon in comp.Beacons)
                {
                    if (!words.Any(w => beacon.Tokens.Any(b => b.Length >= 3 && PulsarSpeech.SameWord(w, b))))
                        continue;
                    matched.Add(beacon.Beacon);
                    if (target.Length == 0)
                        target = beacon.Name;
                }

                foreach (var (grid, mem) in comp.Grids)
                {
                    foreach (var info in mem.Doors.Values)
                    {
                        var score = 0;
                        if (matched.Count > 0 && NearestBeacon(comp, lex, grid, info.Tile) is { } beacon && matched.Contains(beacon.Beacon))
                            score += 1;
                        if (!TerminatingOrDeleted(info.Door) && DoorNameMatches(lex, Name(info.Door), words))
                            score += 2;
                        if (score > 0)
                            candidates.Add(new DoorCandidate(info.Door, info, grid, mem, score));
                    }
                }

                named = candidates.Count > 0 || matched.Count > 0;
            }
        }

        // 3. Nothing named: the door pointed at, the one next to the speaker, or the one that way.
        if (!named)
        {
            if (pointed is { } p && KnownDoor(comp, p) is { } pc)
            {
                candidates.Add(pc with { Score = 1 });
            }
            else if (here || dir != PulsarDirection.None)
            {
                if (SpeakerPosition(speaker) is not { } pos || !comp.Grids.TryGetValue(pos.Grid, out var mem) || !mem.IsSeen(pos.Tile))
                {
                    failReason = "speaker-not-visible";
                    failReply = "pulsar-reply-cant-see-you";
                    return TargetResult.Fail;
                }

                var range = here ? proto.HereRange : proto.DirectionRange;
                var near = new List<(DoorCandidate C, EntityUid Grid, Vector2i Tile)>();
                foreach (var info in mem.Doors.Values)
                {
                    if ((TileCenter(info.Tile) - pos.Local).Length() <= range && mem.IsSeen(info.Tile)
                        && !TerminatingOrDeleted(info.Door) && HasComp<StationAiWhitelistComponent>(info.Door))
                        near.Add((new DoorCandidate(info.Door, info, pos.Grid, mem, 1), pos.Grid, info.Tile));
                }

                List<DoorCandidate> picked;
                if (dir != PulsarDirection.None)
                {
                    picked = FilterByDirection(near, dir, speaker, cone: true);
                }
                else
                {
                    // «Передо мной»: the door the speaker faces wins over one behind them.
                    var facing = pos.Facing;
                    picked = near.OrderBy(n =>
                    {
                        var d = TileCenter(n.Tile) - pos.Local;
                        var len = d.Length();
                        var cos = len > 0.01f ? Vector2.Dot(d / len, facing) : 1f;
                        return len - cos * 1.2f;
                    }).Select(n => n.C).ToList();
                }

                if (picked.Count == 0)
                {
                    failReason = "no-door-near-speaker";
                    failReply = here ? "pulsar-reply-point-at-door" : "pulsar-reply-no-door-that-way";
                    return TargetResult.Fail;
                }

                // Only the best one counts for "this door"/direction.
                candidates.Add(picked[0]);
            }
            else
            {
                failReason = "no-target-named";
                failReply = "pulsar-reply-which-door";
                return TargetResult.Fail;
            }
        }

        // Only doors the AI can work with, on camera now.
        var usable = candidates.Where(c => !TerminatingOrDeleted(c.Door)
                                           && HasComp<StationAiWhitelistComponent>(c.Door)
                                           && c.Mem.IsSeen(c.Info.Tile)).ToList();
        if (usable.Count == 0)
        {
            failReason = candidates.Count == 0 ? "no-door-known" : "door-not-visible";
            failReply = "pulsar-reply-no-target";
            return TargetResult.Fail;
        }

        // The door pointed at, if it fits what was named.
        if (named && pointed is { } pd && usable.FirstOrDefault(c => c.Door == pd) is { Door.Valid: true } byPoint)
        {
            all?.Add(byPoint.Door);
            door = byPoint.Door;
            if (target.Length == 0)
                target = DoorLabel(ent, door);
            return TargetResult.Found;
        }

        var topScore = usable.Max(c => c.Score);
        var top = usable.Where(c => c.Score == topScore).ToList();

        // «Северный шлюз медбея»: the room's door lying that way.
        if (named && dir != PulsarDirection.None && top.Count > 1)
        {
            var filtered = FilterByDirection(top.Select(c => (c, c.Grid, c.Info.Tile)).ToList(), dir, speaker);
            if (filtered.Count > 0)
                top = filtered;
        }

        all?.AddRange(top.Select(c => c.Door));

        var speakerPos = _xforms.GetWorldPosition(speaker);
        top = top.OrderBy(c => (_xforms.GetWorldPosition(c.Door) - speakerPos).LengthSquared()).ToList();
        var chosen = top[0];

        if (allowAsk && top.Count > 1
            && (_xforms.GetWorldPosition(chosen.Door) - speakerPos).Length() > proto.ObviousRange)
        {
            // Different places: ask. Doors of the same place are one answer (the nearest is taken).
            var groups = top.GroupBy(c => PlaceKey(ent, c.Grid, c.Info.Tile)).Select(g => g.First()).Take(MaxOptions).ToList();
            if (groups.Count > 1)
            {
                options = MakeOptions(ent, groups);
                return TargetResult.Ambiguous;
            }
        }

        door = chosen.Door;
        if (target.Length == 0)
            target = DoorLabel(ent, door);
        return TargetResult.Found;
    }

    /// <summary>
    /// How well a door fits a room from the table. The room's access and the door's own name (map doors are named in
    /// English: «Bridge», «Medbay») count most, then the station beacon the door stands at; a main airlock beats a
    /// maintenance hatch, a desk windoor or a shutter, and firelocks never count (they are not entrances). A door at
    /// the beacon named by the very word said wins among them.
    /// </summary>
    private int RoomScore(PulsarAutonomousAiComponent comp, LexiconCache lex, RoomCache room, EntityUid grid, PulsarDoorInfo info,
        List<string> spoken)
    {
        if (TerminatingOrDeleted(info.Door) || HasComp<FirelockComponent>(info.Door))
            return 0;

        var score = 0;
        if (room.Proto.Access.Count > 0 && info.Access.Any(set => room.Proto.Access.Any(a => set.Contains(a.Id))))
            score += 2;
        var nameTokens = PulsarSpeech.Tokenize(Name(info.Door));
        if (room.DoorNames.Count > 0 && PulsarSpeech.Any(nameTokens, room.DoorNames))
            score += 4;
        if (room.Beacons.Count > 0)
            score += BeaconScore(comp, lex, room, grid, info.Tile);
        if (score == 0)
            return 0;

        // «Открой бриг»: of the security doors, the ones at the «Бриг» beacon, not the arrivals checkpoint.
        if (spoken.Count > 0 && NearestBeacon(comp, lex, grid, info.Tile) is { } at
            && spoken.Any(w => at.Tokens.Any(b => b.Length >= 3 && PulsarSpeech.SameWord(w, b))))
            score += 2;

        if (info.IsAirlock)
            score += 1;
        var protoId = MetaData(info.Door).EntityPrototype?.ID ?? string.Empty;
        if (protoId.Contains("Maint", StringComparison.OrdinalIgnoreCase)
            || nameTokens.Any(t => t.StartsWith(Maint, StringComparison.Ordinal) || t == Hatch))
            score -= 6; // a back door, even when it carries the room's name
        else if (protoId.Contains("Windoor", StringComparison.OrdinalIgnoreCase)
                 || protoId.Contains("Shutter", StringComparison.OrdinalIgnoreCase))
            score -= 3; // a desk window or a shutter: a way in only when nothing else is
        return Math.Max(score, 1);
    }

    /// <summary>2 when the nearest beacon is the room's, 1 when the room's beacon is about as near.</summary>
    private int BeaconScore(PulsarAutonomousAiComponent comp, LexiconCache lex, RoomCache room, EntityUid grid, Vector2i tile)
    {
        var range = lex.Proto.BeaconRange;
        float? nearest = null;
        float? roomBeacon = null;
        var nearestIsRoom = false;
        foreach (var beacon in comp.Beacons)
        {
            if (beacon.Grid != grid)
                continue;
            var d = (beacon.Tile - tile).Length;
            if (d > range)
                continue;
            var isRoom = PulsarSpeech.Any(beacon.Tokens, room.Beacons);
            if (nearest == null || d < nearest)
            {
                nearest = d;
                nearestIsRoom = isRoom;
            }

            if (isRoom && (roomBeacon == null || d < roomBeacon))
                roomBeacon = d;
        }

        if (nearestIsRoom)
            return 2;
        return roomBeacon is { } r && nearest is { } n && r <= n + 4f ? 1 : 0;
    }

    /// <summary>How a door is called in replies: its name, with the nearest station beacon when it has one.</summary>
    private string DoorLabel(Entity<PulsarAutonomousAiComponent> ent, EntityUid door)
    {
        var name = Name(door);
        var xform = Transform(door);
        if (xform.GridUid is not { } grid || !_gridQuery.TryComp(grid, out var gridComp))
            return name;

        var tile = _maps.LocalToTile(grid, gridComp, xform.Coordinates);
        return NearestBeacon(ent.Comp, GetLexicon(ent.Comp), grid, tile) is { } beacon
            ? Loc.GetString("pulsar-clarify-option-place", ("door", name), ("place", beacon.Name))
            : name;
    }

    private bool DoorNameMatches(LexiconCache lex, string doorName, List<string> words)
    {
        var nameWords = PulsarSpeech.Tokenize(doorName).Where(t => t.Length >= 3 && !PulsarSpeech.IsWord(t, lex.DoorWords)).ToList();
        return nameWords.Count > 0 && words.Any(w => nameWords.Any(n => PulsarSpeech.SameWord(w, n)));
    }

    private DoorCandidate? KnownDoor(PulsarAutonomousAiComponent comp, EntityUid door)
    {
        var xform = Transform(door);
        if (xform.GridUid is not { } grid || !comp.Grids.TryGetValue(grid, out var mem) || !_gridQuery.TryComp(grid, out var gridComp))
            return null;

        var tile = _maps.LocalToTile(grid, gridComp, xform.Coordinates);
        return mem.Doors.TryGetValue(tile, out var info) && info.Door == door
            ? new DoorCandidate(door, info, grid, mem, 1)
            : null;
    }

    private List<PulsarCandidate> MakeOptions(Entity<PulsarAutonomousAiComponent> ent, List<DoorCandidate> doors)
    {
        var names = doors.Select(d => Name(d.Door)).ToList();
        var distinctNames = names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;
        var list = new List<PulsarCandidate>();
        foreach (var d in doors)
        {
            var name = Name(d.Door);
            var beacon = NearestBeacon(ent.Comp, GetLexicon(ent.Comp), d.Grid, d.Info.Tile);
            string label;
            if (distinctNames)
                label = name;
            else if (beacon is { } b)
                label = Loc.GetString("pulsar-clarify-option-place", ("door", name), ("place", b.Name));
            else
                label = Loc.GetString("pulsar-clarify-option-tile", ("door", name), ("x", d.Info.Tile.X), ("y", d.Info.Tile.Y));

            var words = PulsarSpeech.Tokenize(name).Concat(beacon?.Tokens ?? Array.Empty<string>()).Distinct().ToArray();
            list.Add(new PulsarCandidate(d.Door, label, words, d.Info.Tile, d.Grid));
        }

        return list;
    }

    // ---- geometry ----

    private static Vector2 TileCenter(Vector2i tile) => new(tile.X + 0.5f, tile.Y + 0.5f);

    private readonly record struct SpeakerPos(EntityUid Grid, Vector2 Local, Vector2i Tile, Vector2 Facing);

    /// <summary>Where the speaker stands on their grid and where they face (grid space, north = +Y).</summary>
    private SpeakerPos? SpeakerPosition(EntityUid speaker)
    {
        var xform = Transform(speaker);
        if (xform.GridUid is not { } grid || !_gridQuery.TryComp(grid, out var gridComp))
            return null;

        var local = _maps.WorldToLocal(grid, gridComp, _xforms.GetWorldPosition(xform));
        var facing = (_xforms.GetWorldRotation(xform) - _xforms.GetWorldRotation(grid)).ToWorldVec();
        return new SpeakerPos(grid, local, _maps.LocalToTile(grid, gridComp, xform.Coordinates), facing);
    }

    private PulsarDirection ParseDirection(LexiconCache lex, List<string> tokens)
    {
        foreach (var (dir, phrase) in lex.Directions)
        {
            if (phrase.Find(tokens) >= 0)
                return dir;
        }

        return PulsarDirection.None;
    }

    /// <summary>
    /// Keeps what lies that way. Station directions: relative to the speaker when <paramref name="cone"/> (no room
    /// named), else the extreme ones of the group (the room's northern door). Left/right/front/back: relative to the
    /// speaker and where they face.
    /// </summary>
    private List<T> FilterByDirection<T>(List<(T Item, EntityUid Grid, Vector2i Tile)> items, PulsarDirection dir, EntityUid speaker, bool cone = false)
    {
        if (items.Count == 0 || SpeakerPosition(speaker) is not { } pos)
            return new List<T>();

        var f = pos.Facing;
        Vector2 vec;
        var relative = true;
        switch (dir)
        {
            case PulsarDirection.North: vec = new Vector2(0, 1); relative = cone; break;
            case PulsarDirection.South: vec = new Vector2(0, -1); relative = cone; break;
            case PulsarDirection.East: vec = new Vector2(1, 0); relative = cone; break;
            case PulsarDirection.West: vec = new Vector2(-1, 0); relative = cone; break;
            case PulsarDirection.Front: vec = f; break;
            case PulsarDirection.Back: vec = -f; break;
            case PulsarDirection.Left: vec = new Vector2(-f.Y, f.X); break;
            case PulsarDirection.Right: vec = new Vector2(f.Y, -f.X); break;
            default: return items.Select(i => i.Item).ToList();
        }

        if (relative)
        {
            // Within 60° of that way from the speaker, nearest first.
            return items
                .Select(i => (i.Item, D: TileCenter(i.Tile) - pos.Local))
                .Where(i => i.D.Length() > 0.3f && Vector2.Dot(Vector2.Normalize(i.D), vec) >= 0.5f)
                .OrderBy(i => i.D.LengthSquared())
                .Select(i => i.Item)
                .ToList();
        }

        // The extreme ones of the group along that axis (within 1.5 tiles of the furthest).
        var proj = items.Select(i => (i.Item, P: Vector2.Dot(TileCenter(i.Tile), vec))).ToList();
        var max = proj.Max(p => p.P);
        return proj.Where(p => p.P >= max - 1.5f).Select(p => p.Item).ToList();
    }
}

public enum PulsarDirection : byte
{
    None,
    North,
    South,
    West,
    East,
    Left,
    Right,
    Front,
    Back,
}
