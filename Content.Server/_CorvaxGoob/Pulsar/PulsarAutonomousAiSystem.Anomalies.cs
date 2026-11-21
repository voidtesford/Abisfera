using System.Linq;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Anomaly.Components;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 5.3: science and its anomalies. Pulsar sends a science unit to neutralize anomalies that appear on the
/// station by themselves; one made by the science anomaly generator is left alone
/// (<see cref="PulsarScienceAnomalyComponent.Generated"/>), and a scientist (the science department, the research
/// director or the captain) can ask to leave one alone («не трогай аномалию», «оставьте все аномалии») or give it
/// back («аномалию можно нейтрализовать»). Anyone else gets a polite refusal.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    /// <summary>How far from the speaker «эта аномалия» is looked for, tiles.</summary>
    private const float AnomalyNearRange = 40f;

    private bool TryAnomalyRequest(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens,
        TimeSpan now)
    {
        var proto = lex.Proto;
        if (!PulsarSpeech.Any(tokens, PulsarSpeech.Parse(proto.AnomalyWords)))
            return false;

        var keep = PulsarSpeech.Any(tokens, PulsarSpeech.Parse(proto.AnomalyKeepWords));
        var release = !keep && PulsarSpeech.Any(tokens, PulsarSpeech.Parse(proto.AnomalyReleaseWords));
        if (!keep && !release)
            return false;

        if (!MayDecideAnomalies(proto, ctx.Job))
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "anomaly-not-science", "pulsar-reply-anomaly-not-yours", now);
            return true;
        }

        // Exactly as written: «аномалию» is one, «аномалии» are all of them.
        var allWords = proto.AnomalyAllWords.Select(w => PulsarSpeech.Normalize(w.TrimEnd('$'))).ToHashSet();
        var all = tokens.Any(allWords.Contains);
        var targets = StationAnomalies(ent, ctx.Speaker, all);
        if (targets.Count == 0)
        {
            Decide(ent, ctx, null, null, PulsarDecisionOutcome.NoTarget, "anomaly-none", "pulsar-reply-anomaly-none", now);
            return true;
        }

        foreach (var anomaly in targets)
        {
            var mark = EnsureComp<PulsarScienceAnomalyComponent>(anomaly);
            mark.Protected = keep;
            mark.ProtectedBy = keep ? ctx.Name : null;
            if (release)
                mark.Generated = false;
        }

        var reply = (keep, targets.Count > 1) switch
        {
            (true, false) => "pulsar-reply-anomaly-kept",
            (true, true) => "pulsar-reply-anomaly-kept-all",
            (false, false) => "pulsar-reply-anomaly-released",
            _ => "pulsar-reply-anomaly-released-all",
        };
        Decide(ent, ctx, null, null, keep ? PulsarDecisionOutcome.AnomalyKept : PulsarDecisionOutcome.AnomalyReleased,
            keep ? "anomaly-kept" : "anomaly-released", reply, now);
        return true;
    }

    /// <summary>The science department, the research director and the captain decide on anomalies.</summary>
    private bool MayDecideAnomalies(PulsarLexiconPrototype proto, string? job)
    {
        if (job == null)
            return false;
        return job == proto.CaptainJob.Id || job == "ResearchDirector" || InDepartment(proto.UnitScienceDepartment.Id, job);
    }

    /// <summary>Anomalies on the station grids: all of them, or the one nearest to the speaker.</summary>
    private List<EntityUid> StationAnomalies(Entity<PulsarAutonomousAiComponent> ent, EntityUid speaker, bool all)
    {
        var list = new List<(EntityUid Uid, float Dist)>();
        var speakerPos = _xforms.GetMapCoordinates(speaker);
        var query = EntityQueryEnumerator<AnomalyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || !ent.Comp.StationGrids.Contains(grid))
                continue;
            var pos = _xforms.GetMapCoordinates(uid, xform);
            var dist = pos.MapId == speakerPos.MapId ? (pos.Position - speakerPos.Position).Length() : float.MaxValue;
            list.Add((uid, dist));
        }

        if (all || list.Count <= 1)
            return list.Select(a => a.Uid).ToList();

        // «Эту аномалию»: the nearest one; when the speaker is far from all of them, the only sensible one is none.
        var nearest = list.OrderBy(a => a.Dist).First();
        return nearest.Dist <= AnomalyNearRange ? new List<EntityUid> { nearest.Uid } : new List<EntityUid>();
    }
}
