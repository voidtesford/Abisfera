using System.Linq;
using System.Numerics;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Doors.Components;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 3: protective bolting.
/// <list type="bullet">
/// <item>Automatic, only on red and higher and only while the laws make it serve the crew: closed doors within
/// <see cref="PulsarAutonomousAiComponent.AutoBoltRadius"/> tiles of an enemy on camera get bolted; open ones are
/// closed first. Lifted again when no enemy was seen near the door for
/// <see cref="PulsarAutonomousAiComponent.AutoUnboltDelay"/>.</item>
/// <item>On request of Security staff or heads: «заблокируй шлюзы вокруг врага / в оружейной», «разблокируй …».
/// Bolts made on request stay until someone asks to lift them.</item>
/// </list>
/// Only doors on camera that the AI controls (AI wire, power, not welded). Doors bolted by someone else are left
/// alone by the automatic part.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    private static readonly TimeSpan PendingBoltTime = TimeSpan.FromSeconds(10);

    private IEnumerable<PulsarMobInfo> EnemiesInView(PulsarAutonomousAiComponent comp)
    {
        return comp.Mobs.Values.Where(m => m.InView && m.Kind == PulsarMobKind.Enemy && !m.Dead);
    }

    private List<EntityUid> DoorsNear(PulsarAutonomousAiComponent comp, PulsarMobInfo mob, float radius)
    {
        var list = new List<EntityUid>();
        if (!comp.Grids.TryGetValue(mob.Grid, out var mem))
            return list;

        var r2 = radius * radius;
        foreach (var info in mem.Doors.Values)
        {
            if ((info.Tile - mob.Tile).LengthSquared <= r2 && mem.IsSeen(info.Tile))
                list.Add(info.Door);
        }

        return list;
    }

    private bool CanControl(EntityUid door, out DoorComponent? doorComp)
    {
        doorComp = null;
        if (TerminatingOrDeleted(door) || !_doorQuery.TryComp(door, out doorComp))
            return false;

        return doorComp.State != DoorState.Welded
               && TryComp<StationAiWhitelistComponent>(door, out var wl) && wl.Enabled
               && _power.IsPowered(door)
               && _boltQuery.HasComp(door);
    }

    /// <summary>Bolts (or closes, then bolts) a door. False if it cannot / it was bolted by someone else.</summary>
    private bool BoltDoor(Entity<PulsarAutonomousAiComponent> ent, EntityUid door, bool auto, string reason, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!CanControl(door, out var doorComp) || !_boltQuery.TryComp(door, out var bolts))
            return false;

        if (comp.Bolts.TryGetValue(door, out var existing))
        {
            existing.LastThreat = now;
            if (!auto)
                existing.Auto = false;
            return true;
        }

        if (bolts.BoltsDown)
            return false;

        var record = new PulsarBolt { Auto = auto, Reason = reason, Since = now, LastThreat = now };
        if (doorComp!.State == DoorState.Closed)
        {
            _doors.SetBoltsDown((door, bolts), true);
        }
        else
        {
            _doors.TryClose(door, doorComp);
            record.Pending = true;
        }

        comp.Bolts[door] = record;
        return true;
    }

    private bool UnboltDoor(Entity<PulsarAutonomousAiComponent> ent, EntityUid door)
    {
        var known = ent.Comp.Bolts.Remove(door);
        if (TerminatingOrDeleted(door) || !_boltQuery.TryComp(door, out var bolts) || !bolts.BoltsDown)
            return known;

        if (!TryComp<StationAiWhitelistComponent>(door, out var wl) || !wl.Enabled || !_power.IsPowered(door))
            return false;

        _doors.SetBoltsDown((door, bolts), false);
        return true;
    }

    /// <summary>Every mob refresh: automatic bolts on red, pending bolts, lifting old automatic bolts.</summary>
    private void UpdateBolts(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var enemies = EnemiesInView(comp).ToList();

        // Keep the bolts near enemies "fresh".
        foreach (var enemy in enemies)
        {
            foreach (var door in DoorsNear(comp, enemy, Math.Max(comp.AutoBoltRadius, comp.BoltRadius)))
            {
                if (comp.Bolts.TryGetValue(door, out var b))
                    b.LastThreat = now;
            }
        }

        if (comp.AutoBolt && enemies.Count > 0 && comp.Policy.ServesCrew
            && IsRedOrHigher(_station.GetOwningStation(ent), GetLexicon(comp).Proto))
        {
            foreach (var enemy in enemies)
            {
                var fresh = 0;
                foreach (var door in DoorsNear(comp, enemy, comp.AutoBoltRadius))
                {
                    var known = comp.Bolts.ContainsKey(door);
                    if (BoltDoor(ent, door, auto: true, "auto-red-enemy", now) && !known)
                        fresh++;
                }

                // Stage 4: tell the station once per place.
                if (fresh > 0)
                    AnnounceBolting(ent, enemy, now);
            }
        }

        if (comp.Bolts.Count == 0)
            return;

        foreach (var (door, b) in comp.Bolts.ToArray())
        {
            if (TerminatingOrDeleted(door) || !_doorQuery.TryComp(door, out var doorComp))
            {
                comp.Bolts.Remove(door);
                continue;
            }

            if (b.Pending)
            {
                if (doorComp.State == DoorState.Closed && _boltQuery.TryComp(door, out var bolts))
                {
                    _doors.SetBoltsDown((door, bolts), true);
                    b.Pending = false;
                }
                else if (now - b.Since > PendingBoltTime)
                {
                    comp.Bolts.Remove(door);
                }

                continue;
            }

            if (b.Auto && now - b.LastThreat >= comp.AutoUnboltDelay)
                UnboltDoor(ent, door);
        }
    }

    // ---- requests ----

    private bool MayBolt(PulsarLexiconPrototype proto, string? job)
    {
        if (job == null)
            return false;
        if (job == proto.CaptainJob.Id || proto.HeadJobs.Contains(job))
            return true;
        foreach (var dept in proto.BoltDepartments)
        {
            if (_proto.TryIndex(dept, out var d) && d.Roles.Any(r => r.Id == job))
                return true;
        }

        return false;
    }

    private void HandleBoltRequest(Entity<PulsarAutonomousAiComponent> ent, RequestContext ctx, LexiconCache lex, List<string> tokens, bool bolt, TimeSpan now)
    {
        var comp = ent.Comp;
        var master = IsMaster(comp.Policy, ctx.Speaker);
        if (!master)
        {
            if (!comp.Policy.ServesCrew)
            {
                Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "law-refuse", "pulsar-reply-law-refuse", now);
                return;
            }

            if (!MayBolt(lex.Proto, ctx.Job))
            {
                Decide(ent, ctx, null, null, PulsarDecisionOutcome.Denied, "bolt-not-security", "pulsar-reply-bolt-no-access", now);
                return;
            }
        }

        var doors = new List<EntityUid>();
        string target;
        if (PulsarSpeech.Any(tokens, lex.EnemyWords))
        {
            target = Loc.GetString("pulsar-target-enemies");
            foreach (var enemy in EnemiesInView(comp))
                doors.AddRange(DoorsNear(comp, enemy, comp.BoltRadius));
            if (doors.Count == 0 && bolt)
            {
                Decide(ent, ctx, target, null, PulsarDecisionOutcome.NoTarget, "no-enemy-in-view", "pulsar-reply-no-enemy", now);
                return;
            }
        }
        else if (TryResolveTarget(ent, lex, tokens, ctx.Speaker, out _, out target, out var failReason, out var failReply, doors))
        {
        }
        else if (!bolt && failReason == "no-target-named")
        {
            target = Loc.GetString("pulsar-target-my-bolts");
            doors.AddRange(comp.Bolts.Keys);
        }
        else
        {
            Decide(ent, ctx, target, null,
                failReason == "no-target-named" ? PulsarDecisionOutcome.AskTarget : PulsarDecisionOutcome.NoTarget,
                failReason, failReply, now);
            return;
        }

        var count = 0;
        foreach (var door in doors.Distinct())
        {
            if (bolt ? BoltDoor(ent, door, auto: false, "request", now) : UnboltDoor(ent, door))
                count++;
        }

        var outcome = bolt ? PulsarDecisionOutcome.Bolted : PulsarDecisionOutcome.Unbolted;
        if (count == 0)
        {
            Decide(ent, ctx, target, null, PulsarDecisionOutcome.NotControllable, bolt ? "bolt-none" : "unbolt-none",
                bolt ? "pulsar-reply-bolt-none" : "pulsar-reply-unbolt-none", now, ("target", target));
            return;
        }

        Decide(ent, ctx, target, doors[0], outcome, master ? "law-master" : "request",
            bolt ? "pulsar-reply-bolting" : "pulsar-reply-unbolting", now, ("target", target), ("count", count));
    }
}
