using System.Linq;
using Content.Server._CorvaxGoob.Pulsar.Units;
using Content.Shared._CorvaxGoob.Pulsar;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 5: staff requests for the units linked to the AI. «Пришли двух медиботов в медбей», «закрепи
/// инженерного борга в инженерном отделе», «отзови чистобота», «какие юниты подключены». Who outranks whom:
/// <see cref="UnitRank"/>.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly PulsarUnitsSystem _units = default!;

    private enum UnitVerb : byte
    {
        None,
        Send,
        Assign,
        Release,
        Status,
    }

    /// <summary>What a line asks of the units: which kinds, how many, the tokens left for the place.</summary>
    public sealed class UnitRequest
    {
        public HashSet<PulsarUnitKind> Kinds = new();
        /// <summary>Spoken kind for replies («медибот», «инженерный юнит»).</summary>
        public string KindName = string.Empty;
        /// <summary>0 = not said (one); int.MaxValue = all.</summary>
        public int Count;
        /// <summary>Stage 5.3: units called by name, and their kinds.</summary>
        public HashSet<EntityUid> Units = new();
        public HashSet<PulsarUnitKind> NameKinds = new();
        /// <summary>A kind was named too («Келлера и медибота»), not only names.</summary>
        public bool HasNouns;
        public List<string> PlaceTokens = new();
    }

    private sealed class UnitLexicon
    {
        public List<PulsarSpeech.Phrase> Send = default!;
        public List<PulsarSpeech.Phrase> Assign = default!;
        public List<PulsarSpeech.Phrase> Release = default!;
        public List<PulsarSpeech.Phrase> Status = default!;
        public List<PulsarSpeech.Phrase> Here = default!;
        public List<PulsarSpeech.Phrase> Borg = default!;
        public List<PulsarSpeech.Phrase> Bot = default!;
        public List<(PulsarUnitKind Kind, PulsarSpeech.Phrase Phrase)> Minibots = default!;
        public List<(string Role, PulsarSpeech.Phrase Phrase)> Adjectives = default!;
        public List<List<PulsarSpeech.Phrase>> Counts = default!;
    }

    private readonly Dictionary<string, UnitLexicon> _unitLexicons = new();

    private UnitLexicon GetUnitLexicon(PulsarLexiconPrototype proto)
    {
        if (_unitLexicons.TryGetValue(proto.ID, out var cache))
            return cache;

        cache = new UnitLexicon
        {
            Send = PulsarSpeech.Parse(proto.UnitSendWords),
            Assign = PulsarSpeech.Parse(proto.UnitAssignWords),
            Release = PulsarSpeech.Parse(proto.UnitReleaseWords),
            Status = PulsarSpeech.Parse(proto.UnitStatusWords),
            Here = PulsarSpeech.Parse(proto.UnitHereWords),
            Borg = PulsarSpeech.Parse(proto.UnitBorgWords),
            Bot = PulsarSpeech.Parse(proto.UnitBotWords),
            Minibots = proto.UnitMinibotWords
                .Where(kv => Enum.TryParse<PulsarUnitKind>(kv.Key, out _))
                .SelectMany(kv => PulsarSpeech.Parse(kv.Value).Select(p => (Enum.Parse<PulsarUnitKind>(kv.Key), p)))
                .ToList(),
            Adjectives = proto.UnitAdjectives
                .SelectMany(kv => PulsarSpeech.Parse(kv.Value).Select(p => (kv.Key, p)))
                .ToList(),
            Counts = proto.UnitCountWords.Select(c => PulsarSpeech.Parse(c)).ToList(),
        };
        _unitLexicons[proto.ID] = cache;
        return cache;
    }

    /// <summary>The word as written (a prefix, or the exact form for «$» words), no typos.</summary>
    private static bool StrictMatch(PulsarSpeech.Phrase phrase, string token)
    {
        var word = phrase.Words[0];
        return phrase.Exact[0] ? token == word : token.StartsWith(word, StringComparison.Ordinal);
    }

    /// <summary>
    /// A unit noun: as written, or with a typo only when the word is about as long and is not an adjective
    /// («научного» is not «научборг», «медотсек» is not «медбот»).
    /// </summary>
    private static bool NounMatches(UnitLexicon lex, PulsarSpeech.Phrase phrase, string token)
    {
        if (phrase.Words.Length != 1)
            return false;
        if (StrictMatch(phrase, token))
            return true;
        if (Math.Abs(token.Length - phrase.Words[0].Length) > 1)
            return false;
        if (lex.Adjectives.Any(a => a.Phrase.Words.Length == 1 && StrictMatch(a.Phrase, token)))
            return false;
        return phrase.WordMatches(0, token);
    }

    private static bool TokenIs(string token, List<PulsarSpeech.Phrase> phrases)
    {
        foreach (var p in phrases)
        {
            if (p.Words.Length == 1 && p.WordMatches(0, token))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the unit part of a line. Null when no unit is named. Public for the tests.
    /// Stage 5.3: compound and role nouns («медборг», «медика», «инжборг», «пожарного») may stand for several kinds,
    /// and a unit's own name («Келлер», «медибот Синт-8711») picks that unit.
    /// </summary>
    public UnitRequest? ParseUnitRequest(PulsarLexiconPrototype proto, List<string> tokens,
        IReadOnlyList<(EntityUid Unit, string Name, PulsarUnitKind Kind)>? named = null)
    {
        var lex = GetUnitLexicon(proto);
        var used = new bool[tokens.Count];
        var req = new UnitRequest();
        var nouns = new List<(int Index, bool Borg, PulsarUnitKind[]? Fixed)>();

        // Units by their own names first.
        if (named != null)
        {
            // A word in the names of several units («Синт») names none of them.
            var common = named.SelectMany(n => PulsarSpeech.Tokenize(n.Name).Distinct()).GroupBy(t => t)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            foreach (var (unit, name, kind) in named)
            {
                var nameTokens = PulsarSpeech.Tokenize(name).Where(t => t.Length >= 4 && !common.Contains(t)).ToList();
                if (nameTokens.Count == 0)
                    continue;
                for (var i = 0; i < tokens.Count; i++)
                {
                    if (used[i] || tokens[i].Length < 4 || !nameTokens.Any(n => PulsarSpeech.SameWord(tokens[i], n)))
                        continue;
                    // A word of the lexicon is not a name («бот», «борг»).
                    if (TokenIs(tokens[i], lex.Borg) || TokenIs(tokens[i], lex.Bot)
                        || lex.Minibots.Any(m => NounMatches(lex, m.Phrase, tokens[i])))
                        continue;
                    used[i] = true;
                    req.Units.Add(unit);
                    req.NameKinds.Add(kind);
                }
            }
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i])
                continue;
            var t = tokens[i];
            var kinds = lex.Minibots.Where(m => NounMatches(lex, m.Phrase, t)).Select(m => m.Kind).Distinct().ToArray();
            if (kinds.Length > 0)
            {
                nouns.Add((i, false, kinds));
                used[i] = true;
                continue;
            }

            if (TokenIs(t, lex.Borg))
            {
                nouns.Add((i, true, null));
                used[i] = true;
            }
            else if (TokenIs(t, lex.Bot))
            {
                nouns.Add((i, false, null));
                used[i] = true;
            }
        }

        // «Пришли пожарного / медицинского»: an adjective standing for the noun (animate accusative only).
        if (nouns.Count == 0 && req.Units.Count == 0)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (used[i] || !(t.EndsWith("ого", StringComparison.Ordinal) || t.EndsWith("его", StringComparison.Ordinal)))
                    continue;
                var adj = lex.Adjectives.FirstOrDefault(a => a.Phrase.Words.Length == 1 && StrictMatch(a.Phrase, t));
                if (adj.Phrase == null)
                    continue;
                PulsarUnitKind[] kinds = adj.Role switch
                {
                    "Engineering" => [PulsarUnitKind.BorgEngineering, PulsarUnitKind.Weldbot],
                    "Weld" => [PulsarUnitKind.Weldbot],
                    "Medical" => [PulsarUnitKind.BorgMedical, PulsarUnitKind.Medibot],
                    "Science" => [PulsarUnitKind.BorgScience],
                    "Service" => [PulsarUnitKind.BorgService],
                    "Janitor" or "Clean" => [PulsarUnitKind.BorgJanitor, PulsarUnitKind.Cleanbot],
                    "Mining" => [PulsarUnitKind.BorgMining],
                    "Generic" => [PulsarUnitKind.BorgGeneric],
                    "Fire" => [PulsarUnitKind.Firebot],
                    _ => [],
                };
                if (kinds.Length == 0)
                    continue;
                used[i] = true;
                nouns.Add((i, false, kinds));
                break;
            }
        }

        if (nouns.Count == 0 && req.Units.Count == 0)
            return null;
        req.HasNouns = nouns.Count > 0;

        var names = req.Units.Select(u => named?.FirstOrDefault(n => n.Item1 == u).Item2 ?? Name(u)).ToList();
        foreach (var (index, borg, minibot) in nouns)
        {
            // An adjective right before (or after) the noun: «инженерного борга», «борга инженерного».
            string? role = null;
            foreach (var j in new[] { index - 1, index + 1, index - 2 })
            {
                if (j < 0 || j >= tokens.Count || used[j])
                    continue;
                var adj = lex.Adjectives.FirstOrDefault(a => a.Phrase.Words.Length == 1 && a.Phrase.WordMatches(0, tokens[j]));
                if (adj.Phrase == null)
                    continue;
                role = adj.Role;
                used[j] = true;
                break;
            }

            if (minibot is { } fixedKinds)
            {
                req.Kinds.UnionWith(fixedKinds);
                names.AddRange(fixedKinds.Select(k => Loc.GetString(PulsarUnitsSystem.KindLoc(k))));
                continue;
            }

            if (borg)
            {
                PulsarUnitKind[] kinds = role switch
                {
                    "Engineering" or "Weld" => [PulsarUnitKind.BorgEngineering],
                    "Medical" => [PulsarUnitKind.BorgMedical],
                    "Science" => [PulsarUnitKind.BorgScience],
                    "Service" => [PulsarUnitKind.BorgService],
                    "Janitor" or "Clean" => [PulsarUnitKind.BorgJanitor],
                    "Mining" => [PulsarUnitKind.BorgMining],
                    "Generic" => [PulsarUnitKind.BorgGeneric],
                    _ => [PulsarUnitKind.BorgGeneric, PulsarUnitKind.BorgEngineering, PulsarUnitKind.BorgMedical,
                        PulsarUnitKind.BorgService, PulsarUnitKind.BorgJanitor, PulsarUnitKind.BorgMining,
                        PulsarUnitKind.BorgScience, PulsarUnitKind.BorgOther],
                };
                req.Kinds.UnionWith(kinds);
                names.Add(kinds.Length == 1 ? Loc.GetString(PulsarUnitsSystem.KindLoc(kinds[0])) : Loc.GetString("pulsar-unit-borg-any"));
                continue;
            }

            // Plain «бот»: the adjective says which one; none = any minibot.
            PulsarUnitKind[] botKinds = role switch
            {
                "Medical" => [PulsarUnitKind.Medibot],
                "Engineering" or "Weld" => [PulsarUnitKind.Weldbot],
                "Fire" => [PulsarUnitKind.Firebot],
                "Janitor" or "Clean" or "Service" => [PulsarUnitKind.Cleanbot],
                _ => [PulsarUnitKind.Medibot, PulsarUnitKind.Weldbot, PulsarUnitKind.Firebot, PulsarUnitKind.Cleanbot],
            };
            req.Kinds.UnionWith(botKinds);
            names.Add(botKinds.Length == 1 ? Loc.GetString(PulsarUnitsSystem.KindLoc(botKinds[0])) : Loc.GetString("pulsar-unit-bot-any"));
        }

        // Count: a count word anywhere («двух», «пару», «3», «всех»).
        for (var i = 0; i < tokens.Count && req.Count == 0; i++)
        {
            if (used[i])
                continue;
            for (var n = 0; n < lex.Counts.Count; n++)
            {
                if (!TokenIs(tokens[i], lex.Counts[n]))
                    continue;
                req.Count = n == lex.Counts.Count - 1 ? int.MaxValue : n + 1;
                used[i] = true;
                break;
            }
        }

        req.KindName = string.Join(", ", names.Distinct());
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!used[i])
                req.PlaceTokens.Add(tokens[i]);
        }

        return req;
    }

    private UnitVerb ParseUnitVerb(PulsarLexiconPrototype proto, List<string> tokens)
    {
        var lex = GetUnitLexicon(proto);
        if (PulsarSpeech.Any(tokens, lex.Release))
            return UnitVerb.Release;
        if (PulsarSpeech.Any(tokens, lex.Assign))
            return UnitVerb.Assign;
        if (PulsarSpeech.Any(tokens, lex.Send))
            return UnitVerb.Send;
        if (PulsarSpeech.Any(tokens, lex.Status))
            return UnitVerb.Status;
        return UnitVerb.None;
    }

    /// <summary>Handles a unit request. False when the line is not about units.</summary>
    private bool TryUnitRequest(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens, TimeSpan now)
    {
        var verb = ParseUnitVerb(lex.Proto, tokens);
        if (verb == UnitVerb.None)
            return false;

        var named = _units.LinkedUnits(ent).Select(u => (u.Owner, Name(u), u.Comp.Kind)).ToList();
        var req = ParseUnitRequest(lex.Proto, tokens, named);
        if (req == null)
            return false;

        var policy = ent.Comp.Policy;
        if (!policy.ServesCrew && !IsMaster(policy, ctx.Speaker))
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "law-refuse", "pulsar-reply-law-refuse", now);
            return true;
        }

        switch (verb)
        {
            case UnitVerb.Status:
                UnitStatus(ent, ctx, req, now);
                return true;
            case UnitVerb.Release:
                UnitRelease(ent, ctx, lex, req, now);
                return true;
            default:
                UnitSend(ent, ctx, lex, req, verb == UnitVerb.Assign, now);
                return true;
        }
    }

    /// <summary>
    /// Rank of the requester over a unit kind. The captain is highest; on red alert or higher security and the
    /// captain; then any department head; then the unit's own department; then anyone else.
    /// </summary>
    public int UnitRank(PulsarLexiconPrototype proto, string? job, PulsarUnitKind kind, EntityUid? station)
    {
        if (job == null)
            return PulsarOrderRank.Crew;
        if (job == proto.CaptainJob.Id)
            return PulsarOrderRank.Captain;
        if (IsSecurity(proto, job) && IsRedOrHigher(station, proto))
            return PulsarOrderRank.RedAlertSecurity;
        if (proto.HeadJobs.Any(h => h.Id == job))
            return PulsarOrderRank.Head;

        var inDept = PulsarUnitsSystem.Department(kind) switch
        {
            PulsarUnitDepartment.Engineering => InDepartment(proto.UnitEngineeringDepartment.Id, job),
            PulsarUnitDepartment.Medical => InDepartment(proto.UnitMedicalDepartment.Id, job),
            PulsarUnitDepartment.Science => InDepartment(proto.UnitScienceDepartment.Id, job),
            PulsarUnitDepartment.Cargo => InDepartment(proto.UnitCargoDepartment.Id, job),
            PulsarUnitDepartment.Service => proto.UnitServiceJobs.Any(j => j.Id == job),
            _ => false,
        };

        return inDept ? PulsarOrderRank.Department : PulsarOrderRank.Crew;
    }

    private bool InDepartment(string department, string job)
    {
        return _proto.TryIndex<Content.Shared.Roles.DepartmentPrototype>(department, out var dept) && dept.Roles.Any(r => r.Id == job);
    }

    private string RankName(int rank) => rank switch
    {
        >= PulsarOrderRank.Captain => Loc.GetString("pulsar-rank-captain"),
        >= PulsarOrderRank.RedAlertSecurity => Loc.GetString("pulsar-rank-security"),
        >= PulsarOrderRank.Head => Loc.GetString("pulsar-rank-head"),
        >= PulsarOrderRank.Department => Loc.GetString("pulsar-rank-department"),
        _ => Loc.GetString("pulsar-rank-crew"),
    };

    /// <summary>The place of a unit order: a room from the table, a station beacon, or the requester («сюда»).</summary>
    private (EntityCoordinates Coords, string Name)? ResolveUnitPlace(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex,
        List<string> tokens)
    {
        var comp = ent.Comp;
        var ulex = GetUnitLexicon(lex.Proto);

        // 1. A room from the table: its beacon, else the middle of its doors.
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

        if (room != null)
        {
            var name = Loc.GetString(room.Proto.Name);
            foreach (var beacon in comp.Beacons)
            {
                if (room.Beacons.Count > 0 && PulsarSpeech.Any(beacon.Tokens, room.Beacons)
                    && _units.TileCoordinates(beacon.Grid, beacon.Tile) is { } bc)
                    return (bc, name);
            }

            var doors = new List<(EntityUid Grid, Vector2i Tile)>();
            foreach (var (grid, mem) in comp.Grids)
            {
                foreach (var info in mem.Doors.Values)
                {
                    if (room.Proto.Access.Count > 0 && info.Access.Any(set => room.Proto.Access.Any(a => set.Contains(a.Id))))
                        doors.Add((grid, info.Tile));
                }
            }

            if (doors.Count > 0)
            {
                var grid = doors.GroupBy(d => d.Grid).OrderByDescending(g => g.Count()).First();
                var cx = grid.Average(d => d.Tile.X);
                var cy = grid.Average(d => d.Tile.Y);
                var nearest = grid.OrderBy(d => (d.Tile.X - cx) * (d.Tile.X - cx) + (d.Tile.Y - cy) * (d.Tile.Y - cy)).First();
                if (_units.TileCoordinates(nearest.Grid, nearest.Tile) is { } dc)
                    return (dc, name);
            }
        }

        // 2. A station beacon by name.
        var words = ContentWords(lex, tokens);
        foreach (var beacon in comp.Beacons)
        {
            if (words.Any(w => beacon.Tokens.Any(b => b.Length >= 3 && PulsarSpeech.SameWord(w, b)))
                && _units.TileCoordinates(beacon.Grid, beacon.Tile) is { } bc)
                return (bc, beacon.Name);
        }

        // 3. «Сюда / ко мне»: where the requester is (on camera, or the holopad they speak at).
        if (PulsarSpeech.Any(tokens, ulex.Here))
        {
            if (ctx.Line.Holopad is { } pad && !TerminatingOrDeleted(pad))
                return (Transform(pad).Coordinates, PlaceAt(ent, Transform(pad).Coordinates));
            if (SpeakerPosition(ctx.Speaker) is { } pos && comp.Grids.TryGetValue(pos.Grid, out var mem) && mem.IsSeen(pos.Tile))
                return (Transform(ctx.Speaker).Coordinates, PlaceAt(ent, Transform(ctx.Speaker).Coordinates));
        }

        return null;
    }

    private string PlaceAt(Entity<PulsarAutonomousAiComponent> ent, EntityCoordinates coords)
    {
        if (_xforms.GetGrid(coords) is { } grid && TryComp<MapGridComponent>(grid, out var gridComp))
            return PlaceName(ent, grid, _maps.LocalToTile(grid, gridComp, coords));
        return Loc.GetString("pulsar-place-unknown");
    }

    private List<Entity<PulsarLinkedComponent>> MatchingUnits(Entity<PulsarAutonomousAiComponent> ent, UnitRequest req)
    {
        // Called by name: those units first; kinds named too («Келлера и медибота») add their units.
        var all = _units.LinkedUnits(ent);
        var byName = all.Where(u => req.Units.Contains(u.Owner)).ToList();
        return byName.Concat(all.Where(u => !req.Units.Contains(u.Owner) && req.Kinds.Contains(u.Comp.Kind))).ToList();
    }

    private void UnitSend(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, UnitRequest req, bool assign, TimeSpan now)
    {
        var place = ResolveUnitPlace(ent, ctx, lex, req.PlaceTokens);
        if (place == null)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.AskTarget, "unit-no-place", "pulsar-reply-unit-where", now,
                ("unit", req.KindName));
            return;
        }

        var (coords, placeName) = place.Value;
        var units = MatchingUnits(ent, req);
        var station = _station.GetOwningStation(ent);
        var wanted = req.Count == 0 ? Math.Max(1, req.Units.Count + (req.HasNouns ? 1 : 0)) : req.Count;

        // Units the module can drive, nearest first (the ones called by name before all); player borgs only get a radio request.
        var target = _xforms.ToMapCoordinates(coords);
        var drivable = units.Where(u => _units.Controllable(u))
            .OrderBy(u => req.Units.Contains(u.Owner) ? 0 : 1)
            .ThenBy(u => u.Comp.Order != null ? 1 : 0)
            .ThenBy(u =>
            {
                var p = _xforms.GetMapCoordinates(u);
                return p.MapId == target.MapId ? (p.Position - target.Position).Length() : float.MaxValue;
            })
            .ToList();

        var sent = new List<string>();
        PulsarUnitOrder? blocking = null;
        // Stage 5.2: one group (one report when all are there), each unit its own spot.
        var channel = drivable.Count > 0 ? PulsarUnitsSystem.ChannelFor(drivable[0].Comp.Kind) : ReportChannel.Common;
        var group = _units.NewGroup(ent, placeName, channel, report: true);
        var taken = _units.TakenSpots(ent);
        foreach (var unit in drivable)
        {
            if (sent.Count >= wanted)
                break;
            var rank = UnitRank(lex.Proto, ctx.Job, unit.Comp.Kind, station);
            var order = new PulsarUnitOrder
            {
                Group = group,
                Kinds = new HashSet<PulsarUnitKind>(req.Kinds.Concat(req.NameKinds)),
                Target = _units.FreeSpot(ent, coords, taken),
                Place = placeName,
                Assigned = assign,
                Radius = assign ? 8f : 4f,
                Rank = rank,
                By = string.IsNullOrEmpty(ctx.Name) ? RankName(rank) : ctx.Name,
            };
            if (_units.TryOrder(unit, order, out var b))
            {
                sent.Add(Name(unit));
                _units.JoinGroup(group, unit);
            }
            else
                blocking ??= b;
        }

        // Cyborgs with a person inside: ask on the radio instead of driving them.
        var asked = new List<string>();
        if (sent.Count < wanted)
        {
            foreach (var unit in units.Where(u => !PulsarUnitsSystem.IsMinibot(u.Comp.Kind) && _units.HasMind(u)))
            {
                if (sent.Count + asked.Count >= wanted)
                    break;
                asked.Add(Name(unit));
            }

            if (asked.Count > 0)
            {
                var text = Loc.GetString("pulsar-unit-radio-request", ("units", string.Join(", ", asked)), ("place", placeName));
                UnitRadio(ent, text, ent.Comp.CommonChannel);
            }
        }

        var outcome = assign ? PulsarDecisionOutcome.UnitsAssigned : PulsarDecisionOutcome.UnitsSent;
        if (sent.Count == 0 && asked.Count == 0)
        {
            if (blocking != null)
            {
                Decide(ent, ctx, placeName, null, PulsarDecisionOutcome.UnitsDenied, $"unit-rank {blocking.Rank}", "pulsar-reply-unit-busy", now,
                    ("unit", req.KindName), ("by", string.IsNullOrEmpty(blocking.By) ? RankName(blocking.Rank) : blocking.By));
                return;
            }

            var reply = units.Count == 0 ? "pulsar-reply-unit-none-linked" : "pulsar-reply-unit-none-available";
            Decide(ent, ctx, placeName, null, PulsarDecisionOutcome.UnitsDenied, units.Count == 0 ? "unit-none-linked" : "unit-none-available", reply, now,
                ("unit", req.KindName));
            return;
        }

        var all = sent.Concat(asked).ToList();
        string replyLoc;
        if (req.Count != int.MaxValue && all.Count < wanted)
            replyLoc = assign ? "pulsar-reply-unit-assigned-partial" : "pulsar-reply-unit-sent-partial";
        else
            replyLoc = assign ? "pulsar-reply-unit-assigned" : "pulsar-reply-unit-sent";

        Decide(ent, ctx, placeName, null, outcome, $"units {all.Count}/{(req.Count == int.MaxValue ? "all" : wanted.ToString())}", replyLoc, now,
            ("units", string.Join(", ", all)), ("count", all.Count), ("place", placeName));
    }

    private void UnitRelease(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, UnitRequest req, TimeSpan now)
    {
        var units = MatchingUnits(ent, req).Where(u => u.Comp.Order != null).ToList();
        if (units.Count == 0)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.UnitsDenied, "unit-nothing-to-release", "pulsar-reply-unit-none-busy", now,
                ("unit", req.KindName));
            return;
        }

        var station = _station.GetOwningStation(ent);
        var wanted = req.Count == 0 ? int.MaxValue : req.Count;
        var released = new List<string>();
        PulsarUnitOrder? blocking = null;
        foreach (var unit in units)
        {
            if (released.Count >= wanted)
                break;
            if (_units.TryRelease(unit, UnitRank(lex.Proto, ctx.Job, unit.Comp.Kind, station), out var b))
                released.Add(Name(unit));
            else
                blocking ??= b;
        }

        if (released.Count == 0 && blocking != null)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.UnitsDenied, $"unit-rank {blocking.Rank}", "pulsar-reply-unit-busy", now,
                ("unit", req.KindName), ("by", string.IsNullOrEmpty(blocking.By) ? RankName(blocking.Rank) : blocking.By));
            return;
        }

        Decide(ent, ctx, null, null, PulsarDecisionOutcome.UnitsReleased, $"released {released.Count}", "pulsar-reply-unit-released", now,
            ("units", string.Join(", ", released)));
    }

    private void UnitStatus(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, UnitRequest req, TimeSpan now)
    {
        var units = MatchingUnits(ent, req);
        if (units.Count == 0)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Answered, "unit-status-none", "pulsar-reply-unit-none-linked", now,
                ("unit", req.KindName));
            return;
        }

        var lines = units.Take(8).Select(u => Loc.GetString("pulsar-unit-status-line", ("name", Name(u)), ("task", _units.TaskText(u))));
        Decide(ent, ctx, null, null, PulsarDecisionOutcome.Answered, $"unit-status {units.Count}", "pulsar-reply-unit-status", now,
            ("count", units.Count), ("list", string.Join("; ", lines)));
    }

    /// <summary>Says a unit report on a department channel the core has (else Common), at most once per 20 s per text.</summary>
    public void UnitRadio(EntityUid core, string text, string channel)
    {
        if (!TryComp<PulsarAutonomousAiComponent>(core, out var comp) || comp.State != PulsarState.Active)
            return;
        UnitRadio((core, comp), text, channel);
    }

    private void UnitRadio(Entity<PulsarAutonomousAiComponent> ent, string text, string channel)
    {
        var now = _timing.CurTime;
        var key = "unit:" + text;
        if (ent.Comp.AlertCooldowns.TryGetValue(key, out var next) && next > now)
            return;
        ent.Comp.AlertCooldowns[key] = now + TimeSpan.FromSeconds(20);

        var ch = ent.Comp.Channels.Contains(channel) ? channel : ent.Comp.CommonChannel;
        if (ent.Comp.Channels.Contains(ch))
            _radio.SendRadioMessage(ent, text, ch, ent);
    }
}
