using Content.Shared.Medical.SuitSensors;
using Content.Shared._CorvaxGoob.Pulsar;
using System.Linq;
using Content.Server.AlertLevel;
using Content.Server.GameTicking;
using Content.Server.Medical.SuitSensors;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Roles;
using Content.Shared.StationRecords;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 3: questions. «Где капитан / Иван?» (last seen on camera, else suit sensors set to coordinates, like the
/// crew monitor), «какой код?», «кто на смене / кто из глав / из СБ?», «сколько идёт смена?».
/// Keywords are in the lexicon YAML.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly GameTicker _ticker = default!;

    private const int MaxAnswerLength = 220;

    private bool TryAnswer(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens, TimeSpan now)
    {
        var kind = PulsarSpeech.Any(tokens, lex.AlertQuestion) ? 1
            : PulsarSpeech.Any(tokens, lex.ShiftQuestion) ? 2
            : PulsarSpeech.Any(tokens, lex.TimeQuestion) ? 3
            : PulsarSpeech.Any(tokens, lex.Where) ? 4
            : 0;
        if (kind == 0)
            return false;

        // «Где ты?» / «ты где?»: no one to look for; small talk takes it when it knows the phrase.
        if (kind == 4 && !HasWhereSubject(lex, tokens) && lex.Chat.Any(c => PulsarSpeech.Any(tokens, c.Phrases)))
            return false;

        var policy = ent.Comp.Policy;
        if (!policy.ServesCrew && !IsMaster(policy, ctx.Speaker)
            && !(policy.Kind is PulsarLawPolicyKind.Hostile or PulsarLawPolicyKind.ObeySyndicate && IsWearingEnemySuit(ent.Comp, ctx.Speaker)))
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "law-refuse", "pulsar-reply-law-refuse", now);
            return true;
        }

        switch (kind)
        {
            case 1:
                AnswerAlert(ent, ctx, now);
                break;
            case 2:
                AnswerShift(ent, ctx, lex, tokens, now);
                break;
            case 3:
                Answer(ent, ctx, "time", Loc.GetString("pulsar-answer-time", ("time", FormatDuration(_ticker.RoundDuration()))), now);
                break;
            default:
                AnswerWhere(ent, ctx, lex, tokens, now);
                break;
        }

        return true;
    }

    private void Answer(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, string reason, string text, TimeSpan now, string? target = null)
    {
        if (text.Length > MaxAnswerLength)
            text = text[..(MaxAnswerLength - 1)] + "…";
        Decide(ent, ctx, target, null, PulsarDecisionOutcome.Answered, reason, "pulsar-answer", now, ("text", text));
    }

    private static string FormatDuration(TimeSpan t)
    {
        return $"{(int) t.TotalHours}:{t.Minutes:00}";
    }

    private string FormatAgo(TimeSpan ago)
    {
        return ago.TotalSeconds < 60
            ? Loc.GetString("pulsar-ago-seconds", ("n", (int) ago.TotalSeconds))
            : Loc.GetString("pulsar-ago-minutes", ("n", (int) ago.TotalMinutes));
    }

    private void AnswerAlert(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, TimeSpan now)
    {
        var station = _station.GetOwningStation(ent);
        if (station == null || !TryComp<AlertLevelComponent>(station, out var alert) || string.IsNullOrEmpty(alert.CurrentLevel))
        {
            Answer(ent, ctx, "alert-unknown", Loc.GetString("pulsar-answer-alert-unknown"), now);
            return;
        }

        var key = $"alert-level-{alert.CurrentLevel}";
        var name = Loc.TryGetString(key, out var localized) ? localized : alert.CurrentLevel;
        Answer(ent, ctx, "alert", Loc.GetString("pulsar-answer-alert", ("level", name)), now);
    }

    private void AnswerShift(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens, TimeSpan now)
    {
        var station = _station.GetOwningStation(ent);
        if (station == null || !HasComp<Content.Shared.StationRecords.StationRecordsComponent>(station))
        {
            Answer(ent, ctx, "shift-no-records", Loc.GetString("pulsar-answer-shift-none"), now);
            return;
        }

        var records = _records.GetRecordsOfType<GeneralStationRecord>(station.Value).Select(r => r.Item2).ToList();
        var proto = lex.Proto;

        if (PulsarSpeech.Any(tokens, lex.Heads))
        {
            var heads = records.Where(r => r.JobPrototype == proto.CaptainJob.Id || proto.HeadJobs.Any(h => h.Id == r.JobPrototype))
                .Select(r => $"{JobName(r)} {r.Name}").ToList();
            Answer(ent, ctx, "shift-heads", heads.Count == 0
                ? Loc.GetString("pulsar-answer-heads-none")
                : Loc.GetString("pulsar-answer-heads", ("list", string.Join(", ", heads))), now);
            return;
        }

        if (PulsarSpeech.Any(tokens, lex.Security))
        {
            var sec = records.Where(r => IsSecurity(proto, r.JobPrototype)).Select(r => $"{JobName(r)} {r.Name}").ToList();
            Answer(ent, ctx, "shift-security", sec.Count == 0
                ? Loc.GetString("pulsar-answer-security-none")
                : Loc.GetString("pulsar-answer-security", ("list", string.Join(", ", sec))), now);
            return;
        }

        var byDept = new Dictionary<string, int>();
        foreach (var r in records)
        {
            var dept = PrimaryDepartment(r.JobPrototype);
            var name = dept != null ? Loc.GetString(dept.Name) : Loc.GetString("pulsar-department-other");
            byDept[name] = byDept.GetValueOrDefault(name) + 1;
        }

        var list = string.Join(", ", byDept.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
        Answer(ent, ctx, "shift", Loc.GetString("pulsar-answer-shift", ("count", records.Count), ("list", list)), now);
    }

    private string JobName(GeneralStationRecord record)
    {
        return _proto.TryIndex<JobPrototype>(record.JobPrototype, out var job) ? job.LocalizedName : record.JobTitle;
    }

    private DepartmentPrototype? PrimaryDepartment(string job)
    {
        DepartmentPrototype? fallback = null;
        foreach (var dept in _proto.EnumeratePrototypes<DepartmentPrototype>().OrderByDescending(d => d.Weight))
        {
            if (!dept.Roles.Any(r => r.Id == job))
                continue;
            if (dept.Primary)
                return dept;
            fallback ??= dept;
        }

        return fallback;
    }

    private void AnswerWhere(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens, TimeSpan now)
    {
        var comp = ent.Comp;

        // Who: a job (longest phrase) or a name.
        string? job = null;
        var best = 0;
        foreach (var (j, phrase) in lex.JobWords)
        {
            if (phrase.Weight > best && phrase.Find(tokens) >= 0)
            {
                best = phrase.Weight;
                job = j;
            }
        }

        var nameWords = tokens.Where(t => t.Length >= 3
                                          && !PulsarSpeech.IsWord(t, lex.Names)
                                          && !PulsarSpeech.IsWord(t, lex.Where)
                                          && !PulsarSpeech.IsWord(t, lex.Stop)).ToList();
        if (job == null && nameWords.Count == 0)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.AskTarget, "where-who", "pulsar-reply-who", now);
            return;
        }

        var who = job != null && _proto.TryIndex<JobPrototype>(job, out var jobProto)
            ? jobProto.LocalizedName
            : string.Join(' ', nameWords);

        // 1. Cameras (memory of the last minutes).
        PulsarMobInfo? found = null;
        foreach (var info in comp.Mobs.Values)
        {
            if (info.Kind == PulsarMobKind.Enemy && job != null)
                continue;
            var match = job != null ? info.JobId == job : NameMatches(info.Name, nameWords);
            if (!match)
                continue;
            if (found == null || info.InView && !found.InView || info.InView == found.InView && info.LastSeen > found.LastSeen)
                found = info;
        }

        if (found != null)
        {
            var place = PlaceName(ent, found.Grid, found.Tile);
            var name = VisualNameWithJob(found);
            var text = found.InView
                ? Loc.GetString("pulsar-answer-where-now", ("name", name), ("place", place))
                : Loc.GetString("pulsar-answer-where-seen", ("name", name), ("place", place), ("ago", FormatAgo(now - found.LastSeen)));
            Answer(ent, ctx, found.InView ? "where-camera" : "where-memory", text, now, who);
            return;
        }

        // 2. Suit sensors, only those that show coordinates (what the crew monitor shows).
        var query = EntityQueryEnumerator<SuitSensorComponent>();
        while (query.MoveNext(out var uid, out var sensor))
        {
            if (sensor.User == null || sensor.Mode != SuitSensorMode.SensorCords)
                continue;
            if (_sensors.GetSensorState((uid, sensor, null)) is not { } status || status.Coordinates is not { } netCoords)
                continue;

            var match = job != null ? JobByIcon(status.JobIcon) == job : NameMatches(status.Name, nameWords);
            if (!match)
                continue;

            var coords = GetCoordinates(netCoords);
            var place = Loc.GetString("pulsar-place-unknown");
            if (_xforms.GetGrid(coords) is { } grid && _gridQuery.TryComp(grid, out var gridComp))
                place = PlaceName(ent, grid, _maps.LocalToTile(grid, gridComp, coords));

            Answer(ent, ctx, "where-sensors", Loc.GetString("pulsar-answer-where-sensors",
                ("name", $"{status.Job} {status.Name}"), ("place", place)), now, who);
            return;
        }

        Answer(ent, ctx, "where-unknown", Loc.GetString("pulsar-answer-where-unknown", ("name", who)), now, who);
    }

    private static bool HasWhereSubject(LexiconCache lex, List<string> tokens)
    {
        if (lex.JobWords.Any(j => j.Phrase.Find(tokens) >= 0))
            return true;

        return tokens.Any(t => t.Length >= 3
                               && !PulsarSpeech.IsWord(t, lex.Names)
                               && !PulsarSpeech.IsWord(t, lex.Where)
                               && !PulsarSpeech.IsWord(t, lex.Stop));
    }

    private static bool NameMatches(string name, List<string> words)
    {
        if (words.Count == 0)
            return false;

        var nameTokens = PulsarSpeech.Tokenize(name);
        foreach (var word in words)
        {
            var stem = PulsarSpeech.Stem(word);
            if (stem.Length < 3)
                continue;
            foreach (var n in nameTokens)
            {
                if (n.StartsWith(stem, StringComparison.Ordinal) || word.StartsWith(PulsarSpeech.Stem(n), StringComparison.Ordinal) && n.Length >= 3)
                    return true;
            }
        }

        return false;
    }
}
