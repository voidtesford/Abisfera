using Content.Shared._CorvaxGoob.Pulsar;
using System.Linq;
using Content.Server.Silicons.Laws;
using Content.Shared.Silicons.Laws;
using Content.Shared.Silicons.Laws.Components;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 3: Pulsar follows the laws of its core (the same laws the AI player would have). They are re-read every
/// <see cref="PulsarAutonomousAiComponent.LawCheckInterval"/> and compared by text (catches uploads, ion storms,
/// emag, admin edits without depending on each path raising an event), then classified by
/// <see cref="PulsarLawClassifier"/>.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly SiliconLawSystem _laws = default!;

    private void CheckLaws(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now, bool force = false)
    {
        var comp = ent.Comp;
        if (!force && now < comp.NextLawCheck)
            return;
        comp.NextLawCheck = now + comp.LawCheckInterval;

        // The laws live on the AI brain in the core (what a player AI would follow). An empty core (no AI job
        // taken this round) has no brain: the brain prototype's default lawset then.
        List<string> ids;
        if (_stationAi.TryGetHeld((ent.Owner, (Content.Shared.Silicons.StationAi.StationAiCoreComponent?) null), out var held)
            && TryComp<SiliconLawBoundComponent>(held.Value, out var bound))
        {
            ids = _laws.GetLaws(held.Value, bound).Laws.Select(l => l.LawString).ToList();
        }
        else if (_proto.TryIndex(comp.DefaultLawsFrom, out var brainProto)
                 && brainProto.TryGetComponent<SiliconLawProviderComponent>(out var provider, EntityManager.ComponentFactory))
        {
            ids = _laws.GetLawset(provider.Laws).Laws.Select(l => l.LawString).ToList();
        }
        else
        {
            ids = new List<string>();
        }

        var signature = string.Join("\n", ids);
        if (!force && signature == comp.LawSignature)
            return;

        comp.LawSignature = signature;
        comp.LawTexts = ids.Select(id => Loc.TryGetString(id, out var text) ? text : id).ToList();

        var matched = MatchLawset(ids);
        comp.Policy = PulsarLawClassifier.Classify(comp.LawTexts, matched, _proto.Index(comp.LawPolicy));

        // A policy that no longer serves the crew drops the crew's protective bolts? No: they stay as they are,
        // only the automatic ones are lifted on their own timer.
    }

    /// <summary>The lawset prototype whose laws are exactly these (order ignored), if any.</summary>
    private string? MatchLawset(List<string> lawIds)
    {
        if (lawIds.Count == 0)
            return null;

        var set = lawIds.ToHashSet();
        foreach (var proto in _proto.EnumeratePrototypes<SiliconLawsetPrototype>())
        {
            if (proto.Laws.Count != set.Count)
                continue;

            var all = true;
            foreach (var lawId in proto.Laws)
            {
                if (!_proto.TryIndex<SiliconLawPrototype>(lawId, out var law) || !set.Contains(law.LawString))
                {
                    all = false;
                    break;
                }
            }

            if (all)
                return proto.ID;
        }

        return null;
    }

    /// <summary>Is the speaker the person the laws name (by entity / ID card name)?</summary>
    public bool IsMaster(PulsarLawPolicy policy, EntityUid speaker)
    {
        if (policy.Kind != PulsarLawPolicyKind.ObeyPerson || string.IsNullOrWhiteSpace(policy.Master))
            return false;

        var master = PulsarSpeech.Tokenize(policy.Master);
        if (master.Count == 0)
            return false;

        if (NameHas(Name(speaker), master))
            return true;

        return _idCard.TryFindIdCard(speaker, out var id) && id.Comp.FullName is { } full && NameHas(full, master);
    }

    private static bool NameHas(string name, List<string> master)
    {
        var tokens = PulsarSpeech.Tokenize(name);
        return master.All(m => tokens.Any(t => t == m
            || PulsarSpeech.Stem(m).Length >= 3 && t.StartsWith(PulsarSpeech.Stem(m), StringComparison.Ordinal)
            || PulsarSpeech.Stem(t).Length >= 3 && m.StartsWith(PulsarSpeech.Stem(t), StringComparison.Ordinal)));
    }

    /// <summary>Re-reads the laws now (admin / tests).</summary>
    public void RefreshLaws(Entity<PulsarAutonomousAiComponent> ent)
    {
        CheckLaws(ent, _timing.CurTime, force: true);
    }

    /// <summary>Admin / tests: cooldown of all camera alerts.</summary>
    public void SetAlertCooldown(Entity<PulsarAutonomousAiComponent> ent, TimeSpan cooldown)
    {
        ent.Comp.AlertCooldown = cooldown;
    }

    /// <summary>Admin / tests: how long automatic bolts stay after the last enemy sighting.</summary>
    public void SetAutoUnboltDelay(Entity<PulsarAutonomousAiComponent> ent, TimeSpan delay)
    {
        ent.Comp.AutoUnboltDelay = delay;
    }
}
