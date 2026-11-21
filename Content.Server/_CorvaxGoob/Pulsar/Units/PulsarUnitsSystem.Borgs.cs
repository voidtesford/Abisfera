using System.Linq;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.NPC;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Anomaly.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Medical.Healing;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Repairable;
using Content.Shared.Singularity.Components;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._CorvaxGoob.Pulsar.Units;

/// <summary>
/// Stage 5: cyborgs without a mind, driven by NPC steering and a small task machine. Everything a borg does goes
/// through the normal interaction code with the real items of its modules (do-afters, fuel, charges, access).
/// </summary>
public sealed partial class PulsarUnitsSystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem _solutions = default!;

    private static readonly TimeSpan NoPathPatience = TimeSpan.FromSeconds(4);

    public const PathFlags BorgPathFlags = PathFlags.Interact | PathFlags.Access | PathFlags.Pushing;
    public const float JobRange = 10f;
    public const float HealDamageThreshold = 15f;
    /// <summary>Stage 5.3: damage the chassis can treat worth a visit.</summary>
    public const float TreatableThreshold = 8f;
    /// <summary>Scratches below this are not worth a trip with the welder.</summary>
    public const float RepairDamageThreshold = 10f;
    public static readonly TimeSpan ActWait = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan IgnoreTime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(4);

    // ---- modules ----

    /// <summary>Does any module of the chassis provide an item with this component?</summary>
    public bool HasModuleItem<T>(Entity<BorgChassisComponent> chassis) where T : IComponent, new()
    {
        return FindModuleItem(chassis, uid => HasComp<T>(uid), proto => proto.TryGetComponent<T>(out _, _factory)).Module != null;
    }

    /// <summary>The module with an item that fits, and the item if it already exists (in a hand or stored).</summary>
    private (EntityUid? Module, EntityUid? Item) FindModuleItem(
        Entity<BorgChassisComponent> chassis,
        Func<EntityUid, bool> fits,
        Func<Robust.Shared.Prototypes.EntityPrototype, bool> protoFits)
    {
        if (chassis.Comp.ModuleContainer == null)
            return (null, null);

        foreach (var module in chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<ItemBorgModuleComponent>(module, out var items))
                continue;

            // Selected module: its items are in the hands.
            if (chassis.Comp.SelectedModule == module)
            {
                foreach (var held in _hands.EnumerateHeld(chassis.Owner))
                {
                    if (fits(held))
                        return (module, held);
                }
            }

            foreach (var stored in items.StoredItems.Values)
            {
                if (!TerminatingOrDeleted(stored) && fits(stored))
                    return (module, stored);
            }

            if (!items.Spawned)
            {
                foreach (var hand in items.Hands)
                {
                    if (hand.Item is { } id && _proto.TryIndex(id, out var proto) && protoFits(proto))
                        return (module, null);
                }
            }
        }

        return (null, null);
    }

    /// <summary>Selects the module and puts the item in the active hand. Null when it is not there.</summary>
    private EntityUid? TakeItem(Entity<BorgChassisComponent> chassis, Func<EntityUid, bool> fits,
        Func<Robust.Shared.Prototypes.EntityPrototype, bool> protoFits)
    {
        var (module, _) = FindModuleItem(chassis, fits, protoFits);
        if (module == null)
            return null;

        if (chassis.Comp.SelectedModule != module)
            _borg.SelectModule(chassis.AsNullable(), module.Value);

        foreach (var held in _hands.EnumerateHeld(chassis.Owner))
        {
            if (!fits(held))
                continue;
            _hands.TrySelect(chassis.Owner, held);
            return held;
        }

        return null;
    }

    public bool CanHeal(Entity<BorgChassisComponent> chassis)
    {
        return FindModuleItem(chassis, IsHealItem, p => p.TryGetComponent<HealingComponent>(out _, _factory) || IsHypoProto(p.ID)).Module != null;
    }

    public bool CanRepair(Entity<BorgChassisComponent> chassis)
    {
        return FindModuleItem(chassis, uid => HasComp<WelderComponent>(uid), p => p.TryGetComponent<WelderComponent>(out _, _factory)).Module != null;
    }

    public bool CanWrench(Entity<BorgChassisComponent> chassis)
    {
        return FindModuleItem(chassis, IsWrench, p => ProtoIsWrench(p)).Module != null;
    }

    private static readonly ProtoId<Content.Shared.Tools.ToolQualityPrototype> AnchoringQuality = "Anchoring";

    private bool IsWrench(EntityUid uid) => HasComp<ToolComponent>(uid) && _tools.HasQuality(uid, AnchoringQuality);

    private bool ProtoIsWrench(Robust.Shared.Prototypes.EntityPrototype proto)
    {
        if (!proto.TryGetComponent<ToolComponent>(out var tool, _factory))
            return false;
        var qualities = tool.Qualities;
        return qualities.Contains(AnchoringQuality);
    }

    private static bool IsHypoProto(string id) => id.Contains("Hypo") || id.Contains("JetInjector");

    private bool IsHealItem(EntityUid uid)
    {
        return HasComp<HealingComponent>(uid)
               || HasComp<InjectorComponent>(uid) && MetaData(uid).EntityPrototype is { } p && IsHypoProto(p.ID);
    }

    // ---- driving ----

    private void DriveBorg(Entity<PulsarLinkedComponent> ent, TimeSpan now)
    {
        if (!TryComp<BorgChassisComponent>(ent, out var chassis))
            return;

        // Turn a mindless chassis on (modules, full speed) while it works for the module.
        if (!chassis.Active)
        {
            _borg.SetActive((ent, chassis), true);
            ent.Comp.ActivatedChassis = true;
        }

        if (!ent.Comp.Driving)
        {
            EnsureComp<ActiveNPCComponent>(ent);
            ent.Comp.Driving = true;
        }

        CleanIgnore(ent.Comp, now);

        // Stage 5.3: low on charge first (the order waits).
        CheckCharge(ent, now);

        if (ent.Comp.Task == null)
            PickTask(ent, (ent, chassis), now);

        if (ent.Comp.Task is { } task)
        {
            RunTask(ent, (ent, chassis), task, now);
            return;
        }

        // No task: walk to the ordered place / back into the assigned area.
        var order = ent.Comp.Order;
        if (order == null)
        {
            _steering.Unregister(ent);
            return;
        }

        if (DistanceTo(ent, order.Target) is not { } dist)
        {
            FailOrder(ent, now);
            return;
        }

        if (!order.Arrived && dist <= ArriveRange)
            Arrive(ent, order, now);

        if (order.Arrived && dist <= order.Radius)
        {
            _steering.Unregister(ent);
            ent.Comp.BestDistance = float.MaxValue;
            ent.Comp.BestDistanceAt = now;
            return;
        }

        if (!Progressing(ent, dist, now))
        {
            _steering.Unregister(ent);
            FailOrder(ent, now);
            return;
        }

        // A single "no path" is often transient (the nav grid around a door or a shoved crate is being rebuilt):
        // ask again for a few seconds before giving up.
        if (SteerTo(ent, order.Target, ArriveRange) != SteeringStatus.NoPath)
        {
            ent.Comp.NoPathSince = null;
            return;
        }

        ent.Comp.NoPathSince ??= now;
        if (now - ent.Comp.NoPathSince.Value < NoPathPatience)
        {
            if (TryComp<NPCSteeringComponent>(ent, out var steering))
            {
                steering.Status = SteeringStatus.Moving;
                steering.FailedPathCount = 0;
                ForceRepath(steering);
            }
            return;
        }

        ent.Comp.NoPathSince = null;
        _steering.Unregister(ent);
        FailOrder(ent, now);
    }

    private SteeringStatus SteerTo(EntityUid uid, EntityCoordinates target, float range)
    {
        if (TryComp<NPCSteeringComponent>(uid, out var existing) && existing.Status == SteeringStatus.NoPath
            && !existing.Coordinates.Equals(target))
        {
            existing.Status = SteeringStatus.Moving;
            existing.FailedPathCount = 0;
        }

        var steering = _steering.Register(uid, target, existing);
        steering.Flags = BorgPathFlags;
        steering.Range = range;
        return steering.Status;
    }

    private void CleanIgnore(PulsarLinkedComponent comp, TimeSpan now)
    {
        if (comp.Ignore.Count == 0)
            return;
        foreach (var (uid, until) in comp.Ignore.ToArray())
        {
            if (until <= now || TerminatingOrDeleted(uid))
                comp.Ignore.Remove(uid);
        }
    }

    /// <summary>Where it looks for work: its area when assigned, else around itself.</summary>
    private (EntityCoordinates Center, float Range) WorkArea(Entity<PulsarLinkedComponent> ent)
    {
        if (ent.Comp.Order is { Arrived: true } order)
            return (order.Target, order.Radius + 2f);
        if (ent.Comp.Order is { } walking)
            return (Transform(ent).Coordinates, 3f); // on its way: only what is right next to it
        return (Transform(ent).Coordinates, JobRange);
    }

    private void PickTask(Entity<PulsarLinkedComponent> ent, Entity<BorgChassisComponent> chassis, TimeSpan now)
    {
        // The thing it was sent for first.
        if (ent.Comp.Order?.Focus is { } focus && !TerminatingOrDeleted(focus) && !ent.Comp.Ignore.ContainsKey(focus))
        {
            if (HasComp<AnomalyComponent>(focus) && ent.Comp.Kind == PulsarUnitKind.BorgScience && !LeftToScience(focus))
            {
                StartTask(ent, PulsarBorgTaskKind.Anomaly, focus, now, PulsarBorgTaskStage.Scan);
                return;
            }

            if (ent.Comp.Kind == PulsarUnitKind.BorgMedical && BodyNeedsCare(focus) && CanCarry(chassis))
            {
                StartCorpse(ent, focus, now);
                return;
            }

            if (NeedsHealingBy(chassis, focus))
            {
                StartTask(ent, PulsarBorgTaskKind.Heal, focus, now);
                return;
            }

            if (NeedsRepair(focus) && CanRepair(chassis))
            {
                StartTask(ent, PulsarBorgTaskKind.Repair, focus, now);
                return;
            }
        }

        // Stage 5.2: on its way somewhere it does not stop for odd jobs (only for what it was sent for).
        if (ent.Comp.Order is { Arrived: false })
            return;

        var (center, range) = WorkArea(ent);

        if (CanHeal(chassis))
        {
            foreach (var mob in _lookup.GetEntitiesInRange<HumanoidAppearanceComponent>(center, range))
            {
                if (mob.Owner == ent.Owner || ent.Comp.Ignore.ContainsKey(mob) || !NeedsHealingBy(chassis, mob) || IsEnemyOf(ent, mob)
                    || Claimed(ent, mob))
                    continue;
                StartTask(ent, PulsarBorgTaskKind.Heal, mob, now);
                return;
            }
        }

        // Stage 5.3: bodies nearby (medical units).
        if (ent.Comp.Kind == PulsarUnitKind.BorgMedical && CanCarry(chassis))
        {
            foreach (var mob in _lookup.GetEntitiesInRange<HumanoidAppearanceComponent>(center, range))
            {
                if (ent.Comp.Ignore.ContainsKey(mob) || !BodyNeedsCare(mob) || Claimed(ent, mob))
                    continue;
                StartCorpse(ent, mob, now);
                return;
            }
        }

        if (CanRepair(chassis))
        {
            foreach (var thing in _lookup.GetEntitiesInRange<RepairableComponent>(center, range))
            {
                if (thing.Owner == ent.Owner || ent.Comp.Ignore.ContainsKey(thing) || !NeedsRepair(thing))
                    continue;
                StartTask(ent, PulsarBorgTaskKind.Repair, thing, now);
                return;
            }
        }
    }

    private bool IsEnemyOf(Entity<PulsarLinkedComponent> ent, EntityUid mob)
    {
        return ent.Comp.Core is { } core && TryComp<PulsarAutonomousAiComponent>(core, out var comp)
               && comp.Mobs.TryGetValue(mob, out var info) && info.Kind == PulsarMobKind.Enemy;
    }

    public bool NeedsHealing(EntityUid mob)
    {
        return HasComp<HumanoidAppearanceComponent>(mob) && !_mobState.IsDead(mob)
               && TryComp<DamageableComponent>(mob, out var dmg) && dmg.TotalDamage >= HealDamageThreshold;
    }

    public bool NeedsRepair(EntityUid uid)
    {
        return HasComp<RepairableComponent>(uid) && TryComp<DamageableComponent>(uid, out var dmg) && dmg.TotalDamage >= RepairDamageThreshold;
    }

    private void StartTask(Entity<PulsarLinkedComponent> ent, PulsarBorgTaskKind kind, EntityUid target, TimeSpan now,
        PulsarBorgTaskStage stage = PulsarBorgTaskStage.Approach)
    {
        ent.Comp.Task = new PulsarBorgTask
        {
            Kind = kind,
            Stage = stage,
            Target = target,
            StageStarted = now,
            Deadline = now + TaskTimeout,
        };
        ent.Comp.BestDistance = float.MaxValue;
        ent.Comp.BestDistanceAt = now;
    }

    private void EndTask(Entity<PulsarLinkedComponent> ent, TimeSpan now, bool failed)
    {
        if (ent.Comp.Task is { } task)
        {
            AbortTask(ent, task);
            if (failed)
                ent.Comp.Ignore[task.Target] = now + IgnoreTime;
            if (ent.Comp.Order?.Focus == task.Target)
                ent.Comp.Order.Focus = null;
        }

        ent.Comp.Task = null;
        _steering.Unregister(ent);
        ent.Comp.BestDistance = float.MaxValue;
        ent.Comp.BestDistanceAt = now;
    }

    /// <summary>Lets go of anything the task holds (a pulled A.P.E.).</summary>
    private void AbortTask(EntityUid uid, PulsarBorgTask task)
    {
        if (task.Ape is { } ape && TryComp<PullableComponent>(ape, out var pullable) && pullable.Puller == uid)
            _pulling.TryStopPull(ape, pullable, uid);
    }

    private void SetStage(PulsarBorgTask task, PulsarBorgTaskStage stage, TimeSpan now)
    {
        task.Stage = stage;
        task.StageStarted = now;
        task.Attempts = 0;
    }

    private void RunTask(Entity<PulsarLinkedComponent> ent, Entity<BorgChassisComponent> chassis, PulsarBorgTask task, TimeSpan now)
    {
        if (TerminatingOrDeleted(task.Target) || now > task.Deadline)
        {
            var anomalyGone = task.Kind == PulsarBorgTaskKind.Anomaly && TerminatingOrDeleted(task.Target);
            if (anomalyGone)
                FinishAnomaly(ent, task);
            EndTask(ent, now, failed: !anomalyGone && !TerminatingOrDeleted(task.Target));
            return;
        }

        // Science asked to keep the anomaly: the A.P.E. goes off and the unit goes back to work.
        if (task.Kind == PulsarBorgTaskKind.Anomaly && LeftToScience(task.Target))
        {
            if (task.Ape is { } ape && TryComp<EmitterComponent>(ape, out var emitter) && emitter.IsOn)
                Signal(ape, emitter.OffPort);
            if (ent.Comp.Core is { } core)
                UnitReport(core, Loc.GetString("pulsar-unit-anomaly-left", ("unit", Name(ent))), ReportChannel.Science);
            if (ent.Comp.Order?.Focus == task.Target)
                ent.Comp.Order.Focus = null;
            EndTask(ent, now, failed: false);
            return;
        }

        switch (task.Kind)
        {
            case PulsarBorgTaskKind.Heal:
                RunUseTask(ent, chassis, task, now, m => NeedsHealingBy(chassis, m), PickHealItem);
                break;
            case PulsarBorgTaskKind.Repair:
                RunUseTask(ent, chassis, task, now, NeedsRepair, PickWelder);
                break;
            case PulsarBorgTaskKind.Anomaly:
                RunAnomaly(ent, chassis, task, now);
                break;
            case PulsarBorgTaskKind.Pry:
                RunUseTask(ent, chassis, task, now, DoorClosed, PickPryTool);
                break;
            case PulsarBorgTaskKind.Corpse:
                RunCorpse(ent, chassis, task, now);
                break;
            case PulsarBorgTaskKind.Recharge:
                RunRecharge(ent, task, now);
                break;
            default:
                EndTask(ent, now, failed: true);
                break;
        }
    }

    /// <summary>Walk up to the target and use an item on it until it no longer needs it.</summary>
    private void RunUseTask(Entity<PulsarLinkedComponent> ent, Entity<BorgChassisComponent> chassis, PulsarBorgTask task, TimeSpan now,
        Func<EntityUid, bool> stillNeeded, Func<Entity<BorgChassisComponent>, EntityUid, EntityUid?> pickItem)
    {
        if (!stillNeeded(task.Target))
        {
            EndTask(ent, now, failed: false);
            return;
        }

        if (!_interaction.InRangeUnobstructed(ent.Owner, task.Target))
        {
            task.Stage = PulsarBorgTaskStage.Approach;
            var dist = DistanceTo(ent, Transform(task.Target).Coordinates) ?? float.MaxValue;
            if (!Progressing(ent, dist, now)
                || SteerTo(ent, Transform(task.Target).Coordinates, 1f) == SteeringStatus.NoPath)
                EndTask(ent, now, failed: true);
            return;
        }

        _steering.Unregister(ent);

        // Using an item is a do-after: give it time before using it again.
        if (task.Stage == PulsarBorgTaskStage.Act && now - task.StageStarted < ActWait)
            return;

        if (task.Attempts >= 8)
        {
            EndTask(ent, now, failed: true);
            return;
        }

        if (pickItem(chassis, task.Target) is not { } item)
        {
            EndTask(ent, now, failed: true);
            return;
        }

        task.Stage = PulsarBorgTaskStage.Act;
        task.StageStarted = now;
        task.Attempts++;
        _interaction.InteractUsing(ent, item, task.Target, Transform(task.Target).Coordinates);
    }

    /// <summary>
    /// Stage 5.3: the first aid item that treats most of what the patient has (bleeding too), else a hypospray
    /// that actually holds medicine. Each use is checked: an item that twice did nothing is put aside for this
    /// patient and the next one is tried.
    /// </summary>
    private EntityUid? PickHealItem(Entity<BorgChassisComponent> chassis, EntityUid patient)
    {
        if (!TryComp<DamageableComponent>(patient, out var dmg))
            return null;

        if (TryComp<PulsarLinkedComponent>(chassis, out var link) && link.Task is { Kind: PulsarBorgTaskKind.Heal } task)
        {
            var left = TreatableDamage(chassis, patient) + BleedScore(patient);
            if (task.UsedItem != null)
            {
                if (left > task.LastDamage - 0.5f)
                {
                    if (++task.NoEffect >= 2)
                    {
                        task.Excluded.Add(task.UsedItem);
                        task.NoEffect = 0;
                    }
                }
                else
                {
                    task.NoEffect = 0;
                }
            }

            task.LastDamage = left;
            var picked = PickHealItemInner(chassis, patient, dmg, task.Excluded);
            task.UsedItem = picked is { } p && MetaData(p).EntityPrototype is { } proto ? proto.ID : null;
            return picked;
        }

        return PickHealItemInner(chassis, patient, dmg, new HashSet<string>());
    }

    private EntityUid? PickHealItemInner(Entity<BorgChassisComponent> chassis, EntityUid patient, DamageableComponent dmg, HashSet<string> excluded)
    {
        string? bestProto = null;
        var bestScore = 0f;
        var bleeding = BleedScore(patient);
        foreach (var item in ModuleItems(chassis))
        {
            if (!TryComp<HealingComponent>(item, out var heal) || MetaData(item).EntityPrototype is not { } p || excluded.Contains(p.ID))
                continue;
            if (TryComp<Content.Shared.Stacks.StackComponent>(item, out var stack) && stack.Count <= 0)
                continue;
            var score = 0f;
            foreach (var (type, amount) in heal.Damage.DamageDict)
            {
                if (amount < 0 && dmg.Damage.DamageDict.TryGetValue(type, out var has))
                    score += Math.Min(has.Float(), -amount.Float());
            }

            if (heal.BloodlossModifier < 0 && bleeding > 0)
                score += Math.Min(bleeding, -heal.BloodlossModifier);

            if (score > bestScore)
            {
                bestScore = score;
                bestProto = p.ID;
            }
        }

        // Modules not switched to yet: their items do not exist, judge them by the prototypes.
        if (chassis.Comp.ModuleContainer != null)
        {
            foreach (var module in chassis.Comp.ModuleContainer.ContainedEntities)
            {
                if (!TryComp<ItemBorgModuleComponent>(module, out var items) || items.Spawned)
                    continue;
                foreach (var hand in items.Hands)
                {
                    if (hand.Item is not { } id || excluded.Contains(id) || !_proto.TryIndex(id, out var proto)
                        || !proto.TryGetComponent<HealingComponent>(out var heal, _factory))
                        continue;
                    var score = 0f;
                    foreach (var (type, amount) in heal.Damage.DamageDict)
                    {
                        if (amount < 0 && dmg.Damage.DamageDict.TryGetValue(type, out var has))
                            score += Math.Min(has.Float(), -amount.Float());
                    }

                    if (heal.BloodlossModifier < 0 && bleeding > 0)
                        score += Math.Min(bleeding, -heal.BloodlossModifier);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestProto = id;
                    }
                }
            }
        }

        if (bestProto != null)
        {
            var proto = bestProto;
            var kit = TakeItem(chassis, uid => MetaData(uid).EntityPrototype?.ID == proto && HasComp<HealingComponent>(uid), p => p.ID == proto);
            if (kit != null)
                return kit;
        }

        // A hypospray only when it holds something.
        return TakeItem(chassis,
            uid => HasComp<InjectorComponent>(uid) && IsHealItem(uid) && HoldsMedicine(uid)
                   && !(MetaData(uid).EntityPrototype is { } ip && excluded.Contains(ip.ID)),
            p => IsHypoProto(p.ID) && !excluded.Contains(p.ID));
    }

    /// <summary>Every item of every module of the chassis that exists now (in the hands or stored).</summary>
    private IEnumerable<EntityUid> ModuleItems(Entity<BorgChassisComponent> chassis)
    {
        if (chassis.Comp.ModuleContainer == null)
            yield break;
        foreach (var module in chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<ItemBorgModuleComponent>(module, out var items))
                continue;
            foreach (var stored in items.StoredItems.Values)
            {
                if (!TerminatingOrDeleted(stored))
                    yield return stored;
            }

            if (chassis.Comp.SelectedModule == module)
            {
                foreach (var held in _hands.EnumerateHeld(chassis.Owner))
                    yield return held;
            }
        }
    }

    private bool HoldsMedicine(EntityUid injector)
    {
        if (!TryComp<Content.Shared.Chemistry.Components.SolutionManager.SolutionContainerManagerComponent>(injector, out var manager))
            return false;
        foreach (var (_, soln) in _solutions.EnumerateSolutions((injector, manager)))
        {
            if (soln.Comp.Solution.Volume > 0)
                return true;
        }

        return false;
    }

    /// <summary>Damage types the first aid items of the chassis treat (items that exist and the ones a module will make).</summary>
    private HashSet<string> TreatedTypes(Entity<BorgChassisComponent> chassis, out bool stopsBleeding)
    {
        var types = new HashSet<string>();
        stopsBleeding = false;
        void Add(HealingComponent heal, ref bool bleed)
        {
            foreach (var (type, amount) in heal.Damage.DamageDict)
            {
                if (amount < 0)
                    types.Add(type);
            }

            if (heal.BloodlossModifier < 0)
                bleed = true;
        }

        foreach (var item in ModuleItems(chassis))
        {
            if (TryComp<HealingComponent>(item, out var heal))
                Add(heal, ref stopsBleeding);
        }

        if (chassis.Comp.ModuleContainer != null)
        {
            foreach (var module in chassis.Comp.ModuleContainer.ContainedEntities)
            {
                if (!TryComp<ItemBorgModuleComponent>(module, out var items) || items.Spawned)
                    continue;
                foreach (var hand in items.Hands)
                {
                    if (hand.Item is { } id && _proto.TryIndex(id, out var proto)
                        && proto.TryGetComponent<HealingComponent>(out var heal, _factory))
                        Add(heal, ref stopsBleeding);
                }
            }
        }

        return types;
    }

    /// <summary>How much of the patient's damage this chassis can treat (all of it with a filled hypospray).</summary>
    public float TreatableDamage(Entity<BorgChassisComponent> chassis, EntityUid mob)
    {
        if (!TryComp<DamageableComponent>(mob, out var dmg))
            return 0f;
        foreach (var item in ModuleItems(chassis))
        {
            if (HasComp<InjectorComponent>(item) && IsHealItem(item) && HoldsMedicine(item))
                return dmg.TotalDamage.Float();
        }

        var types = TreatedTypes(chassis, out _);
        var sum = 0f;
        foreach (var (type, amount) in dmg.Damage.DamageDict)
        {
            if (types.Contains(type))
                sum += amount.Float();
        }

        return sum;
    }

    private float BleedScore(EntityUid mob)
    {
        return TryComp<Content.Shared.Body.Components.BloodstreamComponent>(mob, out var blood) ? blood.BleedAmount : 0f;
    }

    /// <summary>Hurt in a way this chassis can actually treat (or bleeding and it has bandages).</summary>
    public bool NeedsHealingBy(Entity<BorgChassisComponent> chassis, EntityUid mob)
    {
        if (!HasComp<HumanoidAppearanceComponent>(mob) || _mobState.IsDead(mob) || !CanHeal(chassis))
            return false;
        if (TreatableDamage(chassis, mob) >= TreatableThreshold)
            return true;
        TreatedTypes(chassis, out var bandages);
        return bandages && BleedScore(mob) > 0.5f;
    }

    /// <summary>Another linked unit already works on this (no two units on one patient or one job).</summary>
    private bool Claimed(Entity<PulsarLinkedComponent> ent, EntityUid target)
    {
        var query = EntityQueryEnumerator<PulsarLinkedComponent>();
        while (query.MoveNext(out var uid, out var other))
        {
            if (uid == ent.Owner || other.Core != ent.Comp.Core)
                continue;
            if (other.Task?.Target == target || other.Order is { Arrived: false } o && o.Focus == target)
                return true;
        }

        return false;
    }

    private EntityUid? PickWelder(Entity<BorgChassisComponent> chassis, EntityUid target)
    {
        var welder = TakeItem(chassis, uid => HasComp<WelderComponent>(uid), p => p.TryGetComponent<WelderComponent>(out _, _factory));
        if (welder is { } w && !_toggle.IsActivated(w))
            _toggle.TryActivate(w, chassis.Owner, predicted: false, showPopup: false);
        return welder;
    }

    // ---- anomalies ----

    /// <summary>
    /// Science unit vs. an anomaly: scan it with the anomaly scanner (the scan tells which particles weaken it),
    /// then use an A.P.E.: one already aimed at it, or the nearest one: unwrench it, pull it next to the anomaly,
    /// wrench it down facing it, switch it to the weakening particles and turn it on until the anomaly is gone.
    /// </summary>
    private void RunAnomaly(Entity<PulsarLinkedComponent> ent, Entity<BorgChassisComponent> chassis, PulsarBorgTask task, TimeSpan now)
    {
        var anomaly = task.Target;
        var anomalyXform = Transform(anomaly);

        switch (task.Stage)
        {
            case PulsarBorgTaskStage.Scan:
            {
                if (!_interaction.InRangeUnobstructed(ent.Owner, anomaly, 2f))
                {
                    var dist = DistanceTo(ent, anomalyXform.Coordinates) ?? float.MaxValue;
                    if (!Progressing(ent, dist, now) || SteerTo(ent, anomalyXform.Coordinates, 1.8f) == SteeringStatus.NoPath)
                        EndTask(ent, now, failed: true);
                    return;
                }

                _steering.Unregister(ent);
                var scanner = TakeItem(chassis, uid => HasComp<AnomalyScannerComponent>(uid), p => p.TryGetComponent<AnomalyScannerComponent>(out _, _factory));
                if (scanner == null)
                {
                    EndTask(ent, now, failed: true);
                    return;
                }

                if (TryComp<AnomalyScannerComponent>(scanner, out var sc) && sc.ScannedAnomaly == anomaly)
                {
                    ReportAnomalyScan(ent, anomaly);
                    PlanApe(ent, chassis, task, now);
                    return;
                }

                if (task.Attempts == 0 || now - task.StageStarted > TimeSpan.FromSeconds(7))
                {
                    if (task.Attempts >= 3)
                    {
                        EndTask(ent, now, failed: true);
                        return;
                    }

                    task.Attempts++;
                    task.StageStarted = now;
                    _interaction.InteractUsing(ent, scanner.Value, anomaly, anomalyXform.Coordinates);
                }

                return;
            }
            case PulsarBorgTaskStage.ToApe:
            {
                if (task.Ape is not { } ape || TerminatingOrDeleted(ape))
                {
                    EndTask(ent, now, failed: true);
                    return;
                }

                if (!_interaction.InRangeUnobstructed(ent.Owner, ape))
                {
                    var dist = DistanceTo(ent, Transform(ape).Coordinates) ?? float.MaxValue;
                    if (!Progressing(ent, dist, now) || SteerTo(ent, Transform(ape).Coordinates, 1f) == SteeringStatus.NoPath)
                        EndTask(ent, now, failed: true);
                    return;
                }

                _steering.Unregister(ent);
                SetStage(task, Transform(ape).Anchored ? PulsarBorgTaskStage.UnanchorApe : PulsarBorgTaskStage.PullApe, now);
                return;
            }
            case PulsarBorgTaskStage.UnanchorApe:
            case PulsarBorgTaskStage.AnchorApe:
            {
                var ape = task.Ape!.Value;
                var wantAnchored = task.Stage == PulsarBorgTaskStage.AnchorApe;
                if (Transform(ape).Anchored == wantAnchored)
                {
                    if (wantAnchored)
                    {
                        StartApe(ent, task, now);
                    }
                    else
                    {
                        ent.Comp.BestDistance = float.MaxValue;
                        ent.Comp.BestDistanceAt = now;
                        SetStage(task, PulsarBorgTaskStage.PullApe, now);
                    }

                    return;
                }

                if (task.Attempts > 0 && now - task.StageStarted < ActWait)
                    return;
                if (task.Attempts >= 3)
                {
                    EndTask(ent, now, failed: true);
                    return;
                }

                var wrench = TakeItem(chassis, IsWrench, p => ProtoIsWrench(p));
                if (wrench == null)
                {
                    EndTask(ent, now, failed: true);
                    return;
                }

                if (wantAnchored)
                    _transform.SetWorldRotation(ape, task.ApeFacing);

                task.Attempts++;
                task.StageStarted = now;
                _interaction.InteractUsing(ent, wrench.Value, ape, Transform(ape).Coordinates);
                return;
            }
            case PulsarBorgTaskStage.PullApe:
            {
                var ape = task.Ape!.Value;
                var spot = task.ApeSpot!.Value;
                var apeDist = DistanceTo(ape, spot) ?? float.MaxValue;
                if (apeDist <= 0.4f)
                {
                    if (TryComp<PullableComponent>(ape, out var p) && p.Puller == ent.Owner)
                        _pulling.TryStopPull(ape, p, ent);
                    _steering.Unregister(ent);
                    // Centre it on the tile before wrenching it down.
                    _transform.SetCoordinates(ape, spot);
                    SetStage(task, PulsarBorgTaskStage.AnchorApe, now);
                    return;
                }

                if (!TryComp<PullableComponent>(ape, out var pullable) || pullable.Puller != ent.Owner)
                {
                    if (!_interaction.InRangeUnobstructed(ent.Owner, ape))
                    {
                        SetStage(task, PulsarBorgTaskStage.ToApe, now);
                        return;
                    }

                    if (!_pulling.TryStartPull(ent, ape) && ++task.Attempts > 3)
                    {
                        EndTask(ent, now, failed: true);
                        return;
                    }
                }

                // Walk onto the spot: the A.P.E. trails behind; then step past so it lands on it.
                if (!Progressing(ent, apeDist, now) || SteerTo(ent, spot, 0.3f) == SteeringStatus.NoPath)
                {
                    EndTask(ent, now, failed: true);
                    return;
                }

                // Close enough with the borg on the spot: drop it here and nudge it on.
                if (DistanceTo(ent, spot) is < 0.6f && apeDist < 1.6f)
                {
                    if (TryComp<PullableComponent>(ape, out var p2) && p2.Puller == ent.Owner)
                        _pulling.TryStopPull(ape, p2, ent);
                    _steering.Unregister(ent);
                    // Step off the spot towards the anomaly side so the tile is free for the machine.
                    _transform.SetCoordinates(ape, spot);
                    SetStage(task, PulsarBorgTaskStage.AnchorApe, now);
                }

                return;
            }
            case PulsarBorgTaskStage.Fire:
            {
                // Runs until the anomaly is gone (handled at the top of RunTask) or the deadline.
                if (task.Ape is { } ape && TryComp<EmitterComponent>(ape, out var emitter) && !emitter.IsOn)
                    Signal(ape, emitter.OnPort);
                return;
            }
        }
    }

    /// <summary>Chooses the A.P.E. and the spot next to the anomaly, or uses one already aimed at it.</summary>
    private void PlanApe(Entity<PulsarLinkedComponent> ent, Entity<BorgChassisComponent> chassis, PulsarBorgTask task, TimeSpan now)
    {
        var anomaly = task.Target;
        var anomalyXform = Transform(anomaly);
        if (anomalyXform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            EndTask(ent, now, failed: true);
            return;
        }

        var anomalyTile = _maps.LocalToTile(grid, gridComp, anomalyXform.Coordinates);

        // An A.P.E. already anchored in line with it and facing it.
        EntityUid? best = null;
        var bestDist = float.MaxValue;
        foreach (var e in _lookup.GetEntitiesInRange<EmitterComponent>(anomalyXform.Coordinates, 40f))
        {
            if (!IsApe(e.Comp) || Transform(e).GridUid != grid)
                continue;

            var xform = Transform(e);
            if (xform.Anchored && AimedAt(e, grid, gridComp, anomalyTile))
            {
                task.Ape = e;
                StartApe(ent, task, now);
                return;
            }

            if (TryComp<PullableComponent>(e, out var pullable) && pullable.Puller != null)
                continue;
            var d = (DistanceTo(e, anomalyXform.Coordinates) ?? float.MaxValue);
            if (d < bestDist)
            {
                bestDist = d;
                best = e;
            }
        }

        if (best == null || !CanWrench(chassis) || FindApeSpot(grid, gridComp, anomalyTile, Transform(best.Value).Coordinates) is not { } spot)
        {
            // Nothing it can do itself: the scan report already went to science.
            EndTask(ent, now, failed: true);
            return;
        }

        task.Ape = best;
        task.ApeSpot = spot.Coords;
        task.ApeFacing = spot.Facing;
        if (TryComp<EmitterComponent>(best, out var em) && em.IsOn)
            Signal(best.Value, em.OffPort);
        ent.Comp.BestDistance = float.MaxValue;
        ent.Comp.BestDistanceAt = now;
        SetStage(task, PulsarBorgTaskStage.ToApe, now);
    }

    private static bool IsApe(EmitterComponent emitter)
    {
        return emitter.SelectableTypes.Any(t => t.Id.Contains("AnomalousParticle"));
    }

    /// <summary>Anchored in a straight line within 6 tiles and its barrel points at the anomaly.</summary>
    private bool AimedAt(EntityUid ape, EntityUid grid, MapGridComponent gridComp, Vector2i anomalyTile)
    {
        var xform = Transform(ape);
        var tile = _maps.LocalToTile(grid, gridComp, xform.Coordinates);
        var delta = anomalyTile - tile;
        if (delta == Vector2i.Zero || delta.X != 0 && delta.Y != 0 || Math.Abs(delta.X + delta.Y) > 6)
            return false;
        var shoot = _transform.GetWorldRotation(ape).ToWorldVec();
        var want = new Vector2(Math.Sign(delta.X), Math.Sign(delta.Y));
        // Grid may be rotated: compare in grid space.
        var gridRot = _transform.GetWorldRotation(grid);
        var local = (-gridRot).RotateVec(shoot);
        return Vector2.Dot(Vector2.Normalize(local), want) > 0.9f;
    }

    /// <summary>A free floor tile 2-3 tiles from the anomaly in a straight line with nothing solid in between.</summary>
    private (EntityCoordinates Coords, Angle Facing)? FindApeSpot(EntityUid grid, MapGridComponent gridComp, Vector2i anomalyTile, EntityCoordinates from)
    {
        (EntityCoordinates, Angle)? best = null;
        var bestDist = float.MaxValue;
        var fromPos = _transform.ToMapCoordinates(from).Position;
        var gridRot = _transform.GetWorldRotation(grid);
        foreach (var dir in new[] { new Vector2i(0, -1), new Vector2i(0, 1), new Vector2i(1, 0), new Vector2i(-1, 0) })
        {
            for (var d = 2; d <= 3; d++)
            {
                var ok = true;
                for (var i = 1; i <= d && ok; i++)
                    ok = TileFree(grid, gridComp, anomalyTile + dir * i);
                if (!ok)
                    break;

                var tile = anomalyTile + dir * d;
                var coords = new EntityCoordinates(grid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * gridComp.TileSize);
                var dist = (_transform.ToMapCoordinates(coords).Position - fromPos).Length();
                if (dist >= bestDist)
                    continue;
                bestDist = dist;
                // Barrel towards the anomaly: -dir in grid space, then to world.
                var facing = gridRot + new Vector2(-dir.X, -dir.Y).ToWorldAngle();
                best = (coords, facing);
            }
        }

        return best;
    }

    private bool TileFree(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        if (!_maps.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<PhysicsComponent>(uid.Value, out var body) && body.CanCollide && body.Hard)
                return false;
        }

        return true;
    }

    private void StartApe(Entity<PulsarLinkedComponent> ent, PulsarBorgTask task, TimeSpan now)
    {
        if (task.Ape is not { } ape || !TryComp<AnomalyComponent>(task.Target, out var anomaly)
            || !TryComp<EmitterComponent>(ape, out var emitter))
        {
            EndTask(ent, now, failed: true);
            return;
        }

        var particle = anomaly.WeakeningParticleType;
        var port = new ProtoId<SinkPortPrototype>("SetParticle" + particle);
        if (emitter.SetTypePorts.ContainsKey(port))
            Signal(ape, port);
        Signal(ape, emitter.OnPort);

        task.Deadline = now + TaskTimeout;
        SetStage(task, PulsarBorgTaskStage.Fire, now);
        _steering.Unregister(ent);
    }

    /// <summary>Same as a signal from a device link (the A.P.E. ports), no UI needed.</summary>
    private void Signal(EntityUid uid, ProtoId<SinkPortPrototype> port)
    {
        var ev = new SignalReceivedEvent(port.Id);
        RaiseLocalEvent(uid, ref ev);
    }

    /// <summary>Made by the science generator or kept by a scientist: not for units to neutralize.</summary>
    public bool LeftToScience(EntityUid anomaly)
    {
        return TryComp<Content.Server._CorvaxGoob.Pulsar.PulsarScienceAnomalyComponent>(anomaly, out var mark)
               && (mark.Generated || mark.Protected);
    }

    private void FinishAnomaly(Entity<PulsarLinkedComponent> ent, PulsarBorgTask task)
    {
        if (task.Ape is { } ape && TryComp<EmitterComponent>(ape, out var emitter) && emitter.IsOn)
            Signal(ape, emitter.OffPort);

        if (ent.Comp.Core is { } core)
            UnitReport(core, Loc.GetString("pulsar-unit-anomaly-done", ("unit", Name(ent))), ReportChannel.Science);
    }

    private string ParticleName(AnomalyComponent comp)
    {
        var particle = comp.WeakeningParticleType;
        return Loc.GetString("pulsar-particle-" + particle.ToString().ToLowerInvariant());
    }

    private void ReportAnomalyScan(Entity<PulsarLinkedComponent> ent, EntityUid anomaly)
    {
        if (ent.Comp.Core is not { } core || !TryComp<AnomalyComponent>(anomaly, out var comp))
            return;
        UnitReport(core, Loc.GetString("pulsar-unit-anomaly-scanned",
            ("unit", Name(ent)),
            ("particle", ParticleName(comp))),
            ReportChannel.Science);
    }
}
