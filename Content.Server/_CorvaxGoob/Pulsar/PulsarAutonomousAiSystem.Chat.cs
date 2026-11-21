using System.Linq;
using Content.Server.AlertLevel;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Doors.Components;
using Content.Shared.StationRecords;
using Robust.Shared.Random;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 4: light communication. Small talk topics (greetings, thanks, «ты тут?», «кто ты», «что умеешь»,
/// «как дела / статус», insults…) come from the lexicon YAML, every topic has several replies in the locale and
/// one is picked at random. A per-person cooldown keeps it from being spammed. No memory of people, no
/// emotions: the same line gets the same kind of answer from anyone.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly TimeSpan BreachMemory = TimeSpan.FromMinutes(5);

    private bool TryChat(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens, TimeSpan now)
    {
        PulsarChatTopic? topic = null;
        foreach (var (t, phrases) in lex.Chat)
        {
            if (!PulsarSpeech.Any(tokens, phrases))
                continue;
            topic = t;
            break;
        }

        if (topic == null)
            return false;

        if (!ChatAllowed(ent, ctx, lex, now))
            return true;

        if (topic.Kind == PulsarChatKind.Status)
        {
            var policy = ent.Comp.Policy;
            if (!policy.ServesCrew && !IsMaster(policy, ctx.Speaker))
            {
                Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "law-refuse", "pulsar-reply-law-refuse", now);
                return true;
            }

            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Chat, $"chat:{topic.Id}", "pulsar-answer", now,
                ("text", StatusSummary(ent, now)));
            return true;
        }

        Decide(ent, ctx, null, null, PulsarDecisionOutcome.Chat, $"chat:{topic.Id}", PickReply(topic.Replies, "pulsar-reply-ack"), now,
            ("name", Loc.GetString(ent.Comp.NameLoc)), ("speaker", ctx.Name));
        return true;
    }

    /// <summary>
    /// Small talk and "not understood" to the same person at most every <see cref="PulsarLexiconPrototype.ChatCooldown"/>.
    /// A line inside the cooldown is logged without a reply.
    /// </summary>
    private bool ChatAllowed(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.NextChat.TryGetValue(ctx.Speaker, out var next) && next > now)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Chat, "chat-cooldown", string.Empty, now);
            return false;
        }

        comp.NextChat[ctx.Speaker] = now + lex.Proto.ChatCooldown;
        return true;
    }

    private string PickReply(List<LocId> replies, string fallback)
    {
        return replies.Count == 0 ? fallback : _random.Pick(replies).Id;
    }

    /// <summary>Only names and filler words: the person just called it.</summary>
    private static bool OnlyAddress(LexiconCache lex, List<string> tokens)
    {
        return tokens.All(t => PulsarSpeech.IsWord(t, lex.Names) || PulsarSpeech.IsWord(t, lex.Stop) || t.Length <= 2);
    }

    /// <summary>«Как дела / статус»: code, crew on shift, recent breaches, enemies on camera, bolted doors.</summary>
    private string StatusSummary(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var station = _station.GetOwningStation(ent);

        var level = Loc.GetString("pulsar-status-level-unknown");
        if (station != null && TryComp<AlertLevelComponent>(station, out var alert) && !string.IsNullOrEmpty(alert.CurrentLevel))
            level = Loc.TryGetString($"alert-level-{alert.CurrentLevel}", out var localized) ? localized : alert.CurrentLevel;

        var crew = station != null && HasComp<StationRecordsComponent>(station)
            ? _records.GetRecordsOfType<GeneralStationRecord>(station.Value).Count()
            : 0;

        var breaches = comp.Alerts
            .Where(a => a.Kind == PulsarAlertKind.Depressurisation && now - a.Time <= BreachMemory)
            .Select(a => a.Key)
            .Distinct()
            .Count();
        var enemies = EnemiesInView(comp).Count();

        var parts = new List<string>
        {
            Loc.GetString("pulsar-status-level", ("level", level)),
            Loc.GetString("pulsar-status-crew", ("count", crew)),
            breaches == 0
                ? Loc.GetString("pulsar-status-no-breaches")
                : Loc.GetString("pulsar-status-breaches", ("count", breaches)),
        };

        if (enemies > 0)
            parts.Add(Loc.GetString("pulsar-status-enemies", ("count", enemies)));
        if (comp.Bolts.Count > 0)
            parts.Add(Loc.GetString("pulsar-status-bolts", ("count", comp.Bolts.Count)));

        return string.Join(" ", parts);
    }

    // ---- closing doors (stage 4) ----

    /// <summary>Close: master, own access on the ID, or Security / heads (who may bolt anyway).</summary>
    private (bool Allowed, string Reason, string Reply) AuthorizeClose(
        Entity<PulsarAutonomousAiComponent> ent,
        LexiconCache lex,
        EntityUid speaker,
        string? job,
        EntityUid door)
    {
        var policy = ent.Comp.Policy;
        if (IsMaster(policy, speaker))
            return (true, "law-master", string.Empty);

        if (policy.Kind is PulsarLawPolicyKind.Hostile or PulsarLawPolicyKind.ObeySyndicate)
        {
            return IsWearingEnemySuit(ent.Comp, speaker)
                ? (true, "law-syndicate", string.Empty)
                : (false, "law-refuse", "pulsar-reply-law-refuse");
        }

        if (!policy.ServesCrew)
            return (false, "law-not-crew", "pulsar-reply-law-refuse");

        if (_accessReader.IsAllowed(speaker, door))
            return (true, "own-access", string.Empty);

        if (MayBolt(lex.Proto, job))
            return (true, "close-security-or-head", string.Empty);

        return (false, "no-access", "pulsar-reply-no-access");
    }

    private void CloseDoor(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, EntityUid door, string target, string reason, TimeSpan now)
    {
        if (TerminatingOrDeleted(door) || !_doorQuery.TryComp(door, out var doorComp))
        {
            Decide(ent, ctx, target, door, PulsarDecisionOutcome.NoTarget, "door-gone", "pulsar-reply-no-target", now);
            return;
        }

        if (doorComp.State is DoorState.Closed or DoorState.Closing or DoorState.Welded)
        {
            Decide(ent, ctx, target, door, PulsarDecisionOutcome.AlreadyOpen, $"{reason}; already-closed", "pulsar-reply-already-closed", now);
            return;
        }

        string? problem = null;
        string? problemReply = null;
        if (!TryComp<Content.Shared.Silicons.StationAi.StationAiWhitelistComponent>(door, out var whitelist) || !whitelist.Enabled)
            (problem, problemReply) = ("ai-control-cut", "pulsar-reply-no-control");
        else if (!_power.IsPowered(door))
            (problem, problemReply) = ("no-power", "pulsar-reply-no-power");
        else if (_boltQuery.TryComp(door, out var bolts) && bolts.BoltsDown)
            (problem, problemReply) = ("bolted", "pulsar-reply-bolted");
        else if (!_doors.TryClose(door, doorComp, user: null))
            (problem, problemReply) = ("close-failed", "pulsar-reply-close-failed");

        if (problem != null)
        {
            Decide(ent, ctx, target, door, PulsarDecisionOutcome.NotControllable, $"{reason}; {problem}", problemReply!, now, ("target", target));
            return;
        }

        Decide(ent, ctx, target, door, PulsarDecisionOutcome.Closed, reason, "pulsar-reply-closing", now, ("target", target));
    }
}
