using System.Linq;
using System.Text.RegularExpressions;
using Content.Server.Administration.Logs;
using Content.Server.AlertLevel;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Database;
using Content.Shared.Station.Components;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 4: station announcements, like the Central Command ones but light blue and signed «А.М.Ц.И.И Пульсар».
/// Made for: the activation / reactivation greetings, its reaction to the alert level changing, automatic
/// protective bolting, a large depressurisation, and on request of the captain / a head of staff
/// («Пульсар, объяви: …», <see cref="PulsarLexiconPrototype.AnnounceCooldown"/> between two, at most
/// <see cref="PulsarLexiconPrototype.AnnounceMaxLength"/> characters). One-to-one answers stay on the radio.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    private static readonly Regex WordRegex = new(@"[\p{L}\d]+", RegexOptions.Compiled);
    private static readonly char[] AnnounceTrimChars = { ' ', ',', ':', ';', '-', '—', '–', '.', '!', '«', '»', '"', '\'' };

    private void InitializeAnnouncements()
    {
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
    }

    /// <summary>A station announcement from the module (the station of the core, or everyone without one).</summary>
    public void PulsarAnnounce(Entity<PulsarAutonomousAiComponent> ent, string text, string reason)
    {
        var comp = ent.Comp;
        var sender = Loc.GetString(comp.AnnouncementSender);
        if (_station.GetOwningStation(ent) is { } station && HasComp<StationDataComponent>(station))
            _chat.DispatchStationAnnouncement(ent, text, sender, true, comp.AnnouncementSound, comp.AnnouncementColor);
        else
            _chat.DispatchGlobalAnnouncement(text, sender, true, comp.AnnouncementSound, comp.AnnouncementColor);

        comp.Announcements.Add(new PulsarAnnouncement(_timing.CurTime, reason, sender, text, comp.AnnouncementColor));
        var excess = comp.Announcements.Count - Math.Max(1, comp.AnnouncementLimit);
        if (excess > 0)
            comp.Announcements.RemoveRange(0, excess);
    }

    // ---- alert level ----

    private string? CurrentAlertLevel(EntityUid core)
    {
        var station = _station.GetOwningStation(core);
        return station != null && TryComp<AlertLevelComponent>(station, out var alert) ? alert.CurrentLevel : null;
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent args)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PulsarAutonomousAiComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.State != PulsarState.Active || _station.GetOwningStation(uid) != args.Station)
                continue;

            var level = args.AlertLevel;
            var before = comp.LastAlertLevel;
            comp.LastAlertLevel = level;
            if (before == null || string.Equals(before, level, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!comp.Policy.ServesCrew || now < comp.NextAlertLevelAnnouncement)
                continue;

            comp.NextAlertLevelAnnouncement = now + comp.AlertLevelAnnounceCooldown;
            var key = $"pulsar-announce-alert-{level.ToLowerInvariant()}";
            var name = Loc.TryGetString($"alert-level-{level}", out var localized) ? localized : level;
            var text = Loc.TryGetString(key, out var specific, ("level", name))
                ? specific
                : Loc.GetString("pulsar-announce-alert-other", ("level", name));
            PulsarAnnounce((uid, comp), text, $"alert-level:{level}");
        }
    }

    // ---- on request ----

    private void HandleAnnounceRequest(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens, TimeSpan now)
    {
        if (!MayAnnounce(ent, ctx, lex, now))
            return;

        var text = ExtractAnnouncement(lex, ctx.Line.Message);
        if (text.Length == 0)
        {
            Remember(ent, ctx, lex, PulsarClarifyKind.Announce, false, new List<PulsarCandidate>(), now);
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.AskTarget, "announce-what", "pulsar-reply-announce-what", now);
            return;
        }

        HandleAnnounceText(ent, ctx, lex, text, now);
    }

    private bool MayAnnounce(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, TimeSpan now)
    {
        var policy = ent.Comp.Policy;
        if (IsMaster(policy, ctx.Speaker))
            return true;

        if (!policy.ServesCrew)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "law-refuse", "pulsar-reply-law-refuse", now);
            return false;
        }

        var proto = lex.Proto;
        if (ctx.Job != null && (ctx.Job == proto.CaptainJob.Id || proto.HeadJobs.Contains(ctx.Job)))
            return true;

        Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "announce-not-head", "pulsar-reply-announce-no-access", now);
        return false;
    }

    private void HandleAnnounceText(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, string text, TimeSpan now)
    {
        if (!MayAnnounce(ent, ctx, lex, now))
            return;

        var comp = ent.Comp;
        var proto = lex.Proto;
        text = Regex.Replace(text, @"\s+", " ").Trim(AnnounceTrimChars);
        if (text.Length == 0)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.AskTarget, "announce-empty", "pulsar-reply-announce-what", now);
            return;
        }

        if (text.Length > proto.AnnounceMaxLength)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "announce-too-long", "pulsar-reply-announce-too-long", now,
                ("max", proto.AnnounceMaxLength));
            return;
        }

        if (now < comp.NextRequestedAnnouncement)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "announce-cooldown", "pulsar-reply-announce-cooldown", now,
                ("seconds", (int) Math.Ceiling((comp.NextRequestedAnnouncement - now).TotalSeconds)));
            return;
        }

        text = char.ToUpperInvariant(text[0]) + text[1..];
        comp.NextRequestedAnnouncement = now + proto.AnnounceCooldown;
        PulsarAnnounce(ent, text, $"request:{ctx.Name}");
        _adminLog.Add(LogType.Chat, LogImpact.Medium, $"{ToPrettyString(ctx.Speaker):player} had {ToPrettyString(ent.Owner):entity} announce: {text}");
        Decide(ent, ctx, null, null, PulsarDecisionOutcome.Announced, "announce-request", "pulsar-reply-announced", now);
    }

    /// <summary>
    /// The text after the announce word in the line as it was said («Пульсар, объяви всем: эвакуация» →
    /// «эвакуация»), lead-in words («всем», «что», «экипажу») dropped.
    /// </summary>
    private static string ExtractAnnouncement(LexiconCache lex, string message)
    {
        var matches = WordRegex.Matches(message);
        var tokens = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var t = PulsarSpeech.Tokenize(m.Value);
            tokens.Add(t.Count == 0 ? string.Empty : string.Concat(t));
        }

        var cut = -1;
        var next = 0;
        foreach (var phrase in lex.Announce)
        {
            var at = phrase.Find(tokens);
            if (at < 0)
                continue;
            var last = Math.Min(tokens.Count - 1, at + phrase.Words.Length - 1);
            var end = matches[last].Index + matches[last].Length;
            if (cut < 0 || end < cut)
            {
                cut = end;
                next = last + 1;
            }
        }

        if (cut < 0)
            return string.Empty;

        // Lead-in words right after the announce word.
        while (next < tokens.Count && PulsarSpeech.IsWord(tokens[next], lex.AnnounceTrim)
               && message[cut..matches[next].Index].Trim(AnnounceTrimChars).Length == 0)
        {
            cut = matches[next].Index + matches[next].Length;
            next++;
        }

        return message[cut..].Trim(AnnounceTrimChars);
    }

    /// <summary>The line without the module's names at its start («Пульсар, эвакуация» → «эвакуация»).</summary>
    private static string StripNames(LexiconCache lex, string message)
    {
        var matches = WordRegex.Matches(message);
        var cut = 0;
        foreach (Match m in matches)
        {
            var t = PulsarSpeech.Tokenize(m.Value);
            if (t.Count == 0 || !PulsarSpeech.IsWord(string.Concat(t), lex.Names) || message[cut..m.Index].Trim(AnnounceTrimChars).Length > 0)
                break;
            cut = m.Index + m.Length;
        }

        return message[cut..].Trim(AnnounceTrimChars);
    }

    // ---- automatic ----

    /// <summary>Automatic protective bolting started near an enemy: one announcement per place per alert cooldown.</summary>
    private void AnnounceBolting(Entity<PulsarAutonomousAiComponent> ent, PulsarMobInfo enemy, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!comp.AnnounceBolting)
            return;

        var key = $"autobolt:{PlaceKey(ent, enemy.Grid, enemy.Tile)}";
        if (comp.AlertCooldowns.TryGetValue(key, out var next) && next > now)
            return;

        comp.AlertCooldowns[key] = now + comp.AlertCooldown;
        PulsarAnnounce(ent, Loc.GetString("pulsar-announce-bolting", ("place", PlaceName(ent, enemy.Grid, enemy.Tile))), "auto-bolt");
    }
}
