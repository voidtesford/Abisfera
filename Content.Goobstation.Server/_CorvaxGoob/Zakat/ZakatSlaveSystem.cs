using System.Linq;
using System.Numerics;
using Content.Goobstation.Common.Atmos;
using Content.Goobstation.Common.Body.Components;
using Content.Goobstation.Common.Temperature.Components;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Server.Antag;
using Content.Server.Beam;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Objectives.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// «порабощение» and the «раб улья» mutations (the host's circular map).
/// </summary>
public sealed partial class ZakatSlaveSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ZakatSystem _zakat = default!;
    [Dependency] private ZakatSymptomSystem _symptoms = default!;
    [Dependency] private ZakatContagionSystem _contagion = default!;
    [Dependency] private CollectiveMindUpdateSystem _collective = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private AntagSelectionSystem _antag = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private BeamSystem _beam = default!;
    [Dependency] private Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private Content.Shared.DoAfter.SharedDoAfterSystem _doAfter = default!;

    public static readonly EntProtoId SlaveRole = "MindRoleZakatSlave";
    public static readonly EntProtoId SlaveObjective = "ZakatSlaveObjective";
    public static readonly EntProtoId HarpoonAction = "ActionZakatHarpoon";
    public static readonly EntProtoId DevourAction = "ActionZakatDevour";
    public static readonly EntProtoId Bioshmat = "MobZakatBioshmat";
    public static readonly EntProtoId HarpoonBeam = "ZakatHarpoonTentacle";
    public static readonly EntProtoId HarpoonAnchor = "ZakatHarpoonAnchor";
    public static readonly EntProtoId MeatChunk = "FoodMeat";

    public const float RegenPerSecond = 2f;
    public const float ArmorCoefficient = 0.7f;
    public const float GrowthsHealthBonus = 50f;
    public const float GrowthsSprintPenalty = 0.5f;
    public const float GrowthsSplashVolume = 10f;
    public static readonly TimeSpan GrowthsSplashCooldown = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan PuddleLifetime = TimeSpan.FromMinutes(3);
    public static readonly EntProtoId ClawsAction = "ActionZakatSlaveClaws";
    public static readonly EntProtoId ClawsItem = "ZakatSlaveClaws";
    public const float HarpoonRange = 10f;
    public static readonly TimeSpan HarpoonKnockdown = TimeSpan.FromSeconds(2);
    public const float DevourHeal = 70f;
    public const float DevourBiomass = 10f;
    public const int DevourMeat = 3;
    public const float DevourBlood = 60f;

    /// <summary>upd2: how long tearing a corpse apart takes (interrupted by moving or being hit).</summary>
    public static readonly TimeSpan DevourTime = TimeSpan.FromSeconds(6);

    public static readonly EntProtoId DevourEffect = "ZakatSlaveDevourGore";

    private readonly List<EntityUid> _due = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatSlaveComponent, ComponentShutdown>(OnSlaveShutdown);
        SubscribeLocalEvent<ZakatSlaveComponent, UpdateMobStateEvent>(OnUpdateMobState, after: new[] { typeof(MobThresholdSystem) });
        SubscribeLocalEvent<ZakatSlaveComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<ZakatSlaveComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<ZakatSlaveComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ZakatSlaveComponent, ZakatSlaveClawsActionEvent>(OnClaws);
        SubscribeLocalEvent<ZakatSlaveComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ZakatSlaveComponent, ZakatHarpoonActionEvent>(OnHarpoon);
        SubscribeLocalEvent<ZakatSlaveComponent, ZakatDevourActionEvent>(OnDevour);
        SubscribeLocalEvent<ZakatSlaveComponent, ZakatSlaveDevourDoAfterEvent>(OnDevourDone);

        SubscribeLocalEvent<ZakatInfectiousPuddleComponent, InteractHandEvent>(OnPuddleTouched);
        SubscribeLocalEvent<ZakatSlaveConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private static bool Has(EntityUid uid, ZakatMutation mutation, IEntityManager entMan)
    {
        return entMan.TryGetComponent<ZakatHostComponent>(uid, out var host) && host.Mutations.Contains(mutation);
    }

    private bool Has(EntityUid uid, ZakatMutation mutation) => Has(uid, mutation, EntityManager);

    #region hive mind and enslaving

    /// <summary>
    /// «разум улья» for the disease itself or one of its slaves.
    /// </summary>
    public void JoinHiveMind(EntityUid uid, ZakatComponent zakat)
    {
        var collective = EnsureComp<CollectiveMindComponent>(uid);
        collective.Channels.Add(zakat.HiveMind);
        collective.DefaultChannel ??= zakat.HiveMind;
        _collective.UpdateCollectiveMind(uid, collective);
    }

    public void LeaveHiveMind(EntityUid uid, ZakatComponent zakat)
    {
        if (!TryComp<CollectiveMindComponent>(uid, out var collective))
            return;

        collective.Channels.Remove(zakat.HiveMind);
        if (collective.DefaultChannel == zakat.HiveMind)
            collective.DefaultChannel = null;
        _collective.UpdateCollectiveMind(uid, collective);
    }

    /// <summary>
    /// Only living humanoids can serve the hive.
    /// </summary>
    public bool CanEnslave(EntityUid uid)
    {
        return HasComp<HumanoidAppearanceComponent>(uid) && _mobState.IsAlive(uid);
    }

    public void Enslave(Entity<ZakatComponent> zakat, Entity<ZakatHostComponent> host)
    {
        host.Comp.Enslaved = true;
        var slave = EnsureComp<ZakatSlaveComponent>(host);
        slave.Zakat = zakat;

        _symptoms.RemoveDebuffs(host);
        JoinHiveMind(host, zakat.Comp);
        GiveRole(host);
    }

    /// <summary>
    /// Mind role, objective and briefing. Called again when a slave moves into a new body.
    /// </summary>
    public void GiveRole(EntityUid uid)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out var mind))
            return;

        if (_roles.MindHasRole<ZakatSlaveRoleComponent>(mindId))
            return;

        _roles.MindAddRole(mindId, SlaveRole, mind, silent: true);
        _mind.TryAddObjective(mindId, mind, SlaveObjective);
        _antag.SendBriefing(uid, Loc.GetString("zakat-slave-briefing"), Color.FromHex("#c0392b"), null);
    }

    /// <summary>
    /// The host got cured: the hive lets go.
    /// </summary>
    public void Release(Entity<ZakatHostComponent> host)
    {
        RemComp<ZakatSlaveComponent>(host);
    }

    private void OnSlaveShutdown(Entity<ZakatSlaveComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakat))
            LeaveHiveMind(ent, zakat);

        _actions.RemoveAction(ent.Owner, ent.Comp.HarpoonAction);
        _actions.RemoveAction(ent.Owner, ent.Comp.DevourAction);
        _actions.RemoveAction(ent.Owner, ent.Comp.ClawsAction);
        if (ent.Comp.ClawsItem is { } claws && !TerminatingOrDeleted(claws))
            QueueDel(claws);
        RemComp<SpecialBreathingImmunityComponent>(ent);
        RemComp<SpecialPressureImmunityComponent>(ent);
        RemComp<SpecialLowTempImmunityComponent>(ent);
        RemComp<ZakatRotGrowthsComponent>(ent);
        RemComp<ZakatPraetorianComponent>(ent);

        if (ent.Comp.ThresholdsRaised)
            ShiftThresholds(ent, -GrowthsHealthBonus);

        if (TryComp<ZakatHostComponent>(ent, out var host))
        {
            host.Enslaved = false;
            host.Mutations.Clear();
        }

        if (_mind.TryGetMind(ent, out var mindId, out _))
            _roles.MindRemoveRole<ZakatSlaveRoleComponent>(mindId);

        _speed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnGetProgress(Entity<ZakatSlaveConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        args.Progress = 0f;
        if (args.Mind.OwnedEntity is not { } body)
            return;

        var zakatUid = TryComp<ZakatSlaveComponent>(body, out var slave) ? slave.Zakat
            : TryComp<ZakatBioshmatComponent>(body, out var shmat) ? shmat.Zakat
            : TryComp<ZakatCreatureComponent>(body, out var creature) && creature.Zakat is { } owner ? owner
            : EntityUid.Invalid;
        if (!TryComp<ZakatComponent>(zakatUid, out var zakat))
            return;

        args.Progress = ZakatSystem.CrewShare(zakat);
    }

    #endregion

    #region mutations

    public void AddMutation(Entity<ZakatHostComponent> host, ZakatMutation mutation)
    {
        if (!host.Comp.Mutations.Add(mutation) || !TryComp<ZakatSlaveComponent>(host, out var slave))
            return;

        var uid = host.Owner;
        switch (mutation)
        {
            case ZakatMutation.SpaceResistance:
                EnsureComp<SpecialBreathingImmunityComponent>(uid);
                EnsureComp<SpecialPressureImmunityComponent>(uid);
                EnsureComp<SpecialLowTempImmunityComponent>(uid);
                break;
            case ZakatMutation.NoPain:
                _mobState.UpdateMobState(uid);
                break;
            case ZakatMutation.Regeneration:
                slave.NextRegen = _timing.CurTime + TimeSpan.FromSeconds(1);
                break;
            case ZakatMutation.RotGrowths:
                EnsureComp<ZakatRotGrowthsComponent>(uid);
                if (!slave.ThresholdsRaised)
                {
                    slave.ThresholdsRaised = true;
                    ShiftThresholds(uid, GrowthsHealthBonus);
                }

                _speed.RefreshMovementSpeedModifiers(uid);
                break;
            case ZakatMutation.MeatHarpoon:
                _actions.AddAction(uid, ref slave.HarpoonAction, HarpoonAction);
                break;
            case ZakatMutation.Devourer:
                _actions.AddAction(uid, ref slave.DevourAction, DevourAction);
                break;
            case ZakatMutation.Claws:
                _actions.AddAction(uid, ref slave.ClawsAction, ClawsAction);
                break;
        }

        _popup.PopupEntity(Loc.GetString($"zakat-mutation-grown-{mutation.ToString().ToLowerInvariant()}"), uid, uid, PopupType.MediumCaution);
    }

    private void ShiftThresholds(EntityUid uid, float delta)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        // dead first when growing, crit first when shrinking: the two never swap places
        var order = delta > 0 ? new[] { MobState.Dead, MobState.Critical } : new[] { MobState.Critical, MobState.Dead };
        foreach (var state in order)
        {
            if (_thresholds.TryGetThresholdForState(uid, state, out var value, thresholds))
                _thresholds.SetMobStateThreshold(uid, value.Value + delta, state, thresholds);
        }
    }

    /// <summary>
    /// «боли нет»: the slave never lies in crit, it fights on until it dies.
    /// </summary>
    private void OnUpdateMobState(Entity<ZakatSlaveComponent> ent, ref UpdateMobStateEvent args)
    {
        if (args.State == MobState.Critical && Has(ent, ZakatMutation.NoPain))
            args.State = MobState.Alive;
    }

    /// <summary>
    /// «живая броня»: hidden resistance to everything (no armor examine line).
    /// </summary>
    private void OnDamageModify(Entity<ZakatSlaveComponent> ent, ref DamageModifyEvent args)
    {
        if (!Has(ent, ZakatMutation.LivingArmor))
            return;

        var reduced = new DamageSpecifier(args.Damage);
        foreach (var (type, value) in args.Damage.DamageDict)
        {
            if (value > 0)
                reduced.DamageDict[type] = value * ArmorCoefficient;
        }

        args.Damage = reduced;
    }

    /// <summary>
    /// «гнилые наросты»: every wound splashes infectious blood.
    /// </summary>
    private void OnDamageChanged(Entity<ZakatSlaveComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !Has(ent, ZakatMutation.RotGrowths))
            return;

        var now = _timing.CurTime;
        if (now < ent.Comp.NextSplash)
            return;

        ent.Comp.NextSplash = now + GrowthsSplashCooldown;
        SplashInfectiousBlood(ent, ent.Comp.Zakat);
    }

    public EntityUid? SplashInfectiousBlood(EntityUid uid, EntityUid zakat)
    {
        var solution = new Solution("Blood", FixedPoint2.New(GrowthsSplashVolume));
        if (!_puddle.TrySpillAt(Transform(uid).Coordinates, solution, out var puddle, sound: false))
            return null;

        var infectious = EnsureComp<ZakatInfectiousPuddleComponent>(puddle);
        infectious.Zakat = zakat;
        infectious.Until = _timing.CurTime + PuddleLifetime;
        return puddle;
    }

    private void OnPuddleTouched(Entity<ZakatInfectiousPuddleComponent> ent, ref InteractHandEvent args)
    {
        _contagion.TryPuddleInfect(ent.Comp.Zakat, args.User);
    }

    private void OnRefreshSpeed(Entity<ZakatSlaveComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!Has(ent, ZakatMutation.RotGrowths) || !TryComp<MovementSpeedModifierComponent>(ent, out var move))
            return;

        if (move.BaseSprintSpeed <= GrowthsSprintPenalty)
            return;

        args.ModifySpeed(1f, (move.BaseSprintSpeed - GrowthsSprintPenalty) / move.BaseSprintSpeed);
    }

    /// <summary>
    /// «когти»: bone claws grow out of the hand (an item that can't be dropped) or go back in.
    /// </summary>
    private void OnClaws(Entity<ZakatSlaveComponent> ent, ref ZakatSlaveClawsActionEvent args)
    {
        if (args.Handled || !Has(ent, ZakatMutation.Claws))
            return;

        args.Handled = ToggleClaws(ent);
    }

    public bool ToggleClaws(Entity<ZakatSlaveComponent> ent)
    {
        if (ent.Comp.ClawsItem is { } old && !TerminatingOrDeleted(old))
        {
            QueueDel(old);
            ent.Comp.ClawsItem = null;
            _popup.PopupEntity(Loc.GetString("zakat-slave-claws-in"), ent, ent);
            return true;
        }

        var claws = Spawn(ClawsItem, Transform(ent).Coordinates);
        if (!_hands.TryForcePickupAnyHand(ent, claws))
        {
            _popup.PopupEntity(Loc.GetString("zakat-slave-claws-no-hand"), ent, ent);
            QueueDel(claws);
            return false;
        }

        ent.Comp.ClawsItem = claws;
        _popup.PopupEntity(Loc.GetString("zakat-slave-claws-out"), ent, PopupType.MediumCaution);
        return true;
    }

    /// <summary>
    /// «сердце паразит»: a «биошмат» bursts out of the dead slave and takes its mind along.
    /// </summary>
    private void OnMobStateChanged(Entity<ZakatSlaveComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !Has(ent, ZakatMutation.ParasiteHeart))
            return;

        // one heart, one burst
        if (TryComp<ZakatHostComponent>(ent, out var host))
            host.Mutations.Remove(ZakatMutation.ParasiteHeart);

        var shmat = Spawn(Bioshmat, Transform(ent).Coordinates);
        EnsureComp<ZakatBioshmatComponent>(shmat).Zakat = ent.Comp.Zakat;
        // a creature of the hive like any nest-born one: counted as infected, faction, hive mind
        if (TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakatComp))
            EntityManager.System<ZakatFleshSystem>().SetupCreature(shmat, (ent.Comp.Zakat, zakatComp));

        if (_mind.TryGetMind(ent, out var mindId, out var mind))
            _mind.TransferTo(mindId, shmat, mind: mind);

        _popup.PopupEntity(Loc.GetString("zakat-bioshmat-burst", ("target", ent.Owner)), shmat, PopupType.LargeCaution);
    }

    #endregion

    #region abilities

    private void OnHarpoon(Entity<ZakatSlaveComponent> ent, ref ZakatHarpoonActionEvent args)
    {
        if (args.Handled || !_mobState.IsAlive(ent))
            return;

        args.Handled = true;
        FireHarpoon(ent, _transform.ToMapCoordinates(args.Target));
    }

    /// <summary>
    /// «мясной гарпун»: a bone-tipped tentacle. A mob is torn off its feet and dragged in, a wall drags the slave to it.
    /// Returns what was hit (null for a miss).
    /// </summary>
    public EntityUid? FireHarpoon(EntityUid user, MapCoordinates target)
    {
        var userMap = _transform.GetMapCoordinates(user);
        if (userMap.MapId != target.MapId)
            return null;

        var delta = target.Position - userMap.Position;
        if (delta.LengthSquared() < 0.01f)
            return null;

        var dir = Vector2.Normalize(delta);
        var ray = new CollisionRay(userMap.Position, dir, (int) (CollisionGroup.MobLayer | CollisionGroup.Impassable));
        var hit = _physics.IntersectRay(userMap.MapId, ray, HarpoonRange, user, returnOnFirstHit: false)
            .OrderBy(r => r.Distance)
            .Cast<RayCastResults?>()
            .FirstOrDefault();

        var end = hit?.HitPos ?? userMap.Position + dir * HarpoonRange;
        var anchor = Spawn(HarpoonAnchor, new MapCoordinates(end, userMap.MapId));
        DrawTentacle(user, anchor);

        if (hit is not { } result)
            return null;

        var victim = result.HitEntity;
        if (HasComp<MobStateComponent>(victim))
        {
            var pull = userMap.Position + dir * 0.8f - _transform.GetMapCoordinates(victim).Position;
            _stun.TryKnockdown(victim, HarpoonKnockdown, true);
            _throwing.TryThrow(victim, pull, 12f, user, recoil: false, compensateFriction: true, doSpin: false);
            _popup.PopupEntity(Loc.GetString("zakat-harpoon-hit", ("target", victim)), victim, PopupType.MediumCaution);
        }
        else
        {
            var jump = dir * MathF.Max(0f, result.Distance - 0.6f);
            _throwing.TryThrow(user, jump, 12f, user, recoil: false, compensateFriction: true, doSpin: false);
        }

        return victim;
    }

    private void DrawTentacle(EntityUid user, EntityUid anchor)
    {
        // visual only: the beam segments don't collide with anything
        _beam.TryCreateBeam(user, anchor, HarpoonBeam);
    }

    private void OnDevour(Entity<ZakatSlaveComponent> ent, ref ZakatDevourActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryDevour(ent, args.Target);
    }

    public bool CanDevour(EntityUid uid, EntityUid target)
    {
        return target != uid && _mobState.IsAlive(uid) && _mobState.IsDead(target) && HasComp<BodyComponent>(target);
    }

    /// <summary>
    /// «пожиратель»: starts tearing a corpse apart (<see cref="DevourTime"/>, interruptible); see <see cref="Devour"/>.
    /// </summary>
    public bool TryDevour(Entity<ZakatSlaveComponent> ent, EntityUid target)
    {
        if (!CanDevour(ent, target))
        {
            _popup.PopupEntity(Loc.GetString("zakat-devour-not-corpse"), ent, ent);
            return false;
        }

        var args = new Content.Shared.DoAfter.DoAfterArgs(EntityManager, ent, DevourTime, new ZakatSlaveDevourDoAfterEvent(), ent, target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
        };
        if (!_doAfter.TryStartDoAfter(args))
            return false;

        ClearDevourEffect(ent);
        var gore = Spawn(DevourEffect, Transform(target).Coordinates);
        _transform.SetParent(gore, target);
        ent.Comp.DevourEffect = gore;
        _popup.PopupEntity(Loc.GetString("zakat-slave-devour-start-self", ("target", target)), ent, ent, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("zakat-slave-devour-start", ("user", ent.Owner), ("target", target)), ent,
            Robust.Shared.Player.Filter.PvsExcept(ent), true, PopupType.MediumCaution);
        return true;
    }

    private void ClearDevourEffect(Entity<ZakatSlaveComponent> ent)
    {
        if (ent.Comp.DevourEffect is { } gore && !TerminatingOrDeleted(gore))
            QueueDel(gore);
        ent.Comp.DevourEffect = null;
    }

    private void OnDevourDone(Entity<ZakatSlaveComponent> ent, ref ZakatSlaveDevourDoAfterEvent args)
    {
        ClearDevourEffect(ent);
        if (args.Cancelled)
        {
            _popup.PopupEntity(Loc.GetString("zakat-slave-devour-interrupted"), ent, ent);
            return;
        }

        if (args.Handled || args.Target is not { } target || !CanDevour(ent, target))
            return;

        args.Handled = true;
        Devour(ent, target);
    }

    /// <summary>
    /// The corpse is torn into organs, meat and blood, the slave heals, the disease feeds.
    /// </summary>
    public void Devour(Entity<ZakatSlaveComponent> ent, EntityUid target)
    {
        var coords = Transform(target).Coordinates;
        _puddle.TrySpillAt(coords, new Solution("Blood", FixedPoint2.New(DevourBlood)), out _, sound: false);
        for (var i = 0; i < DevourMeat; i++)
        {
            var meat = Spawn(MeatChunk, coords);
            _throwing.TryThrow(meat, _random.NextVector2(1.5f), 4f, recoil: false);
        }

        _body.GibBody(target, gibOrgans: false);
        _damageable.HealEvenly(ent.Owner, -DevourHeal, origin: ent);

        if (TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakat))
            _zakat.AddBiomass((ent.Comp.Zakat, zakat), DevourBiomass);

        _popup.PopupEntity(Loc.GetString("zakat-devour-success"), ent, PopupType.LargeCaution);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // «регенерация»
        var slaves = EntityQueryEnumerator<ZakatSlaveComponent, ZakatHostComponent>();
        while (slaves.MoveNext(out var uid, out var slave, out var host))
        {
            if (!host.Mutations.Contains(ZakatMutation.Regeneration) || now < slave.NextRegen)
                continue;

            slave.NextRegen = now + TimeSpan.FromSeconds(1);
            if (_mobState.IsDead(uid))
                continue;

            _damageable.HealEvenly(uid, -RegenPerSecond, origin: uid);
        }

        // infectious blood: stepping in it
        _due.Clear();
        var puddles = EntityQueryEnumerator<ZakatInfectiousPuddleComponent>();
        while (puddles.MoveNext(out var uid, out var puddle))
        {
            if (now >= puddle.Until)
            {
                _due.Add(uid);
                continue;
            }

            if (now < puddle.NextCheck)
                continue;

            puddle.NextCheck = now + TimeSpan.FromSeconds(0.5);
            foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(uid).Coordinates, 0.45f))
                _contagion.TryPuddleInfect(puddle.Zakat, mob);
        }

        foreach (var uid in _due)
            RemComp<ZakatInfectiousPuddleComponent>(uid);
    }
}
