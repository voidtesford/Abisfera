using System.Linq;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Server.Atmos.Components;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared.Actions;
using Content.Shared.Armor;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Clothing;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Explosion.Components;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// «преторианец» (an evolved slave) and the hunter's devouring.
/// </summary>
public sealed partial class ZakatPraetorianSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public static readonly EntProtoId AbsorbAction = "ActionZakatAbsorb";
    public static readonly EntProtoId ShiftAction = "ActionZakatShift";
    public static readonly EntProtoId ClawsAction = "ActionZakatClaws";
    public static readonly EntProtoId TransformAction = "ActionZakatTransform";
    public static readonly EntProtoId AbsorbEffect = "ZakatAbsorbTentacles";
    public static readonly EntProtoId AbsorbBurst = "ZakatAbsorbBurst";
    public static readonly EntProtoId AbsorbEngulf = "ZakatAbsorbEngulf";
    public static readonly EntProtoId AbsorbReachShort = "ZakatAbsorbReachShort";
    public static readonly EntProtoId AbsorbReachMid = "ZakatAbsorbReach";
    public static readonly EntProtoId AbsorbReachLong = "ZakatAbsorbReachLong";
    public const string AbsorbedContainer = "zakat-absorbed";

    public const float Health = 400f;
    public const float SprintBonus = 2f;
    public const float RegenPerSecond = 2f;
    public const float ClawSlash = 25f;
    public const float DevourBlood = 60f;

    /// <summary>
    /// Slots a swallowed look is worn in. Only what shows: no ID, belt, back, pockets or headset.
    /// </summary>
    public static readonly string[] LookSlots = { "jumpsuit", "outerClothing", "head", "shoes", "gloves", "mask", "neck" };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatPraetorianComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ZakatPraetorianComponent, UpdateMobStateEvent>(OnUpdateMobState, after: new[] { typeof(MobThresholdSystem) });
        SubscribeLocalEvent<ZakatPraetorianComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ZakatPraetorianComponent, GetMeleeDamageEvent>(OnGetMeleeDamage, after: new[] { typeof(ZakatSlaveSystem) });
        SubscribeLocalEvent<ZakatPraetorianComponent, ZakatAbsorbActionEvent>(OnAbsorb);
        SubscribeLocalEvent<ZakatPraetorianComponent, ZakatAbsorbDoAfterEvent>(OnAbsorbDone);
        SubscribeLocalEvent<ZakatPraetorianComponent, ZakatShiftActionEvent>(OnShift);
        SubscribeLocalEvent<ZakatPraetorianComponent, ZakatClawsActionEvent>(OnClaws);
        SubscribeLocalEvent<ZakatPraetorianComponent, ZakatTransformActionEvent>(OnTransform);

        SubscribeLocalEvent<ZakatHunterComponent, MapInitEvent>(OnHunterInit);
        SubscribeLocalEvent<ZakatHunterComponent, ZakatHunterDevourActionEvent>(OnHunterDevour);
        SubscribeLocalEvent<ZakatHunterComponent, ZakatHunterDevourDoAfterEvent>(OnHunterDevourDone);
    }

    #region praetorian

    public void Make(EntityUid uid, Entity<ZakatComponent> zakat)
    {
        var comp = EnsureComp<ZakatPraetorianComponent>(uid);
        comp.Zakat = zakat;
        comp.NextRegen = _timing.CurTime + TimeSpan.FromSeconds(1);

        if (_thresholds.TryGetThresholdForState(uid, MobState.Dead, out var dead))
            comp.OriginalDead = dead.Value.Float();
        _thresholds.SetMobStateThreshold(uid, Health, MobState.Dead);
        _mobState.UpdateMobState(uid);
        _speed.RefreshMovementSpeedModifiers(uid);
        EntityManager.System<ZakatFleshSystem>().MakeSpaceproof(uid);

        _actions.AddAction(uid, ref comp.AbsorbAction, AbsorbAction);
        _actions.AddAction(uid, ref comp.ShiftAction, ShiftAction);
        _actions.AddAction(uid, ref comp.ClawsAction, ClawsAction);
        _actions.AddAction(uid, ref comp.TransformAction, TransformAction);
        _popup.PopupEntity(Loc.GetString("zakat-praetorian-born"), uid, uid, PopupType.LargeCaution);
    }

    private void OnShutdown(Entity<ZakatPraetorianComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        _actions.RemoveAction(ent.Owner, ent.Comp.AbsorbAction);
        _actions.RemoveAction(ent.Owner, ent.Comp.ShiftAction);
        _actions.RemoveAction(ent.Owner, ent.Comp.ClawsAction);
        _actions.RemoveAction(ent.Owner, ent.Comp.TransformAction);
        ClearAbsorbEffects(ent);
        RemComp<ZakatPraetorianClawsComponent>(ent);
        if (ent.Comp.OriginalDead is { } dead)
            _thresholds.SetMobStateThreshold(ent, dead, MobState.Dead);
        _mobState.UpdateMobState(ent);
        _speed.RefreshMovementSpeedModifiers(ent);
    }

    // no crit: it fights on until it dies
    private void OnUpdateMobState(Entity<ZakatPraetorianComponent> ent, ref UpdateMobStateEvent args)
    {
        if (args.State == MobState.Critical)
            args.State = MobState.Alive;
    }

    private void OnRefreshSpeed(Entity<ZakatPraetorianComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<MovementSpeedModifierComponent>(ent, out var move) || move.BaseSprintSpeed <= 0f)
            return;

        args.ModifySpeed(1f, (move.BaseSprintSpeed + SprintBonus) / move.BaseSprintSpeed);
    }

    private void OnGetMeleeDamage(Entity<ZakatPraetorianComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.Weapon != ent.Owner || !HasComp<ZakatPraetorianClawsComponent>(ent))
            return;

        args.Damage = new DamageSpecifier { DamageDict = new() { ["Slash"] = ClawSlash } };
    }

    private void OnClaws(Entity<ZakatPraetorianComponent> ent, ref ZakatClawsActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleClaws(ent);
    }

    public void ToggleClaws(EntityUid uid)
    {
        if (RemComp<ZakatPraetorianClawsComponent>(uid))
        {
            _popup.PopupEntity(Loc.GetString("zakat-praetorian-claws-in"), uid, uid);
            return;
        }

        EnsureComp<ZakatPraetorianClawsComponent>(uid);
        _popup.PopupEntity(Loc.GetString("zakat-praetorian-claws-out"), uid, PopupType.MediumCaution);
    }

    private void OnAbsorb(Entity<ZakatPraetorianComponent> ent, ref ZakatAbsorbActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartAbsorb(ent, args.Target);
    }

    public bool CanAbsorb(EntityUid uid, EntityUid target)
    {
        return target != uid && _mobState.IsDead(target) && HasComp<HumanoidAppearanceComponent>(target) && HasComp<BodyComponent>(target);
    }

    /// <summary>Durations of the three absorption stages: tentacles burst out, seize the body, draw it in.</summary>
    public static readonly TimeSpan[] AbsorbStages =
    {
        TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2),
    };

    public bool TryStartAbsorb(Entity<ZakatPraetorianComponent> ent, EntityUid target)
    {
        if (!CanAbsorb(ent, target))
        {
            _popup.PopupEntity(Loc.GetString("zakat-absorb-bad-target"), ent, ent);
            return false;
        }

        if (!StartAbsorbStage(ent, target, 0))
            return false;

        ClearAbsorbEffects(ent);
        // tentacles tear out of the praetorian's body
        AddAbsorbEffect(ent, AbsorbBurst, ent);
        _popup.PopupEntity(Loc.GetString("zakat-absorb-burst", ("user", ent.Owner)), ent, PopupType.LargeCaution);
        return true;
    }

    private bool StartAbsorbStage(Entity<ZakatPraetorianComponent> ent, EntityUid target, int stage)
    {
        var args = new DoAfterArgs(EntityManager, ent, AbsorbStages[stage], new ZakatAbsorbDoAfterEvent(stage), ent, target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
        };
        return _doAfter.TryStartDoAfter(args);
    }

    private void AddAbsorbEffect(Entity<ZakatPraetorianComponent> ent, EntProtoId proto, EntityUid parent)
    {
        var effect = Spawn(proto, Transform(parent).Coordinates);
        _transform.SetParent(effect, parent);
        ent.Comp.AbsorbEffects.Add(effect);
    }

    private void ClearAbsorbEffects(Entity<ZakatPraetorianComponent> ent)
    {
        foreach (var effect in ent.Comp.AbsorbEffects)
        {
            if (!TerminatingOrDeleted(effect))
                QueueDel(effect);
        }
        ent.Comp.AbsorbEffects.Clear();
    }

    /// <summary>
    /// The tentacles stretch from the praetorian to the body: the sprite points east, turned towards the body.
    /// </summary>
    private void Reach(Entity<ZakatPraetorianComponent> ent, EntityUid target)
    {
        var from = _transform.GetWorldPosition(ent);
        var dir = _transform.GetWorldPosition(target) - from;
        // three lengths of the bundle (0.7, 1.05, 1.45 tiles): the one that fits the distance
        var length = dir.Length();
        var proto = length < 0.85f ? AbsorbReachShort : length < 1.25f ? AbsorbReachMid : AbsorbReachLong;
        var effect = Spawn(proto, Transform(ent).Coordinates);
        _transform.SetParent(effect, ent);
        if (dir.LengthSquared() > 0.0001f)
            _transform.SetLocalRotation(effect, dir.ToAngle() - _transform.GetWorldRotation(ent));
        ent.Comp.AbsorbEffects.Add(effect);
    }

    private void ClearReach(Entity<ZakatPraetorianComponent> ent)
    {
        foreach (var effect in ent.Comp.AbsorbEffects.ToArray())
        {
            var id = Prototype(effect)?.ID;
            if (id == AbsorbReachShort.Id || id == AbsorbReachMid.Id || id == AbsorbReachLong.Id)
            {
                QueueDel(effect);
                ent.Comp.AbsorbEffects.Remove(effect);
            }
        }
    }

    /// <summary>Drags the body <paramref name="fraction"/> of the way to the praetorian.</summary>
    private void Drag(EntityUid uid, EntityUid target, float fraction)
    {
        var from = _transform.GetMapCoordinates(target);
        var to = _transform.GetMapCoordinates(uid);
        if (from.MapId != to.MapId)
            return;

        var pos = from.Position + (to.Position - from.Position) * fraction;
        _transform.SetMapCoordinates(target, new MapCoordinates(pos, from.MapId));
    }

    private void OnAbsorbDone(Entity<ZakatPraetorianComponent> ent, ref ZakatAbsorbDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (args.Cancelled || args.Target is not { } target || !CanAbsorb(ent, target))
        {
            ClearAbsorbEffects(ent);
            return;
        }

        args.Handled = true;
        switch (args.Stage)
        {
            case 0:
                // they seize the body
                Reach(ent, target);
                AddAbsorbEffect(ent, AbsorbEffect, target);
                _popup.PopupEntity(Loc.GetString("zakat-absorb-start", ("target", target)), target, PopupType.LargeCaution);
                if (!StartAbsorbStage(ent, target, 1))
                    ClearAbsorbEffects(ent);
                break;
            case 1:
                // and pull it closer
                Drag(ent, target, 0.5f);
                ClearReach(ent);
                Reach(ent, target);
                _popup.PopupEntity(Loc.GetString("zakat-absorb-pull", ("target", target)), target, PopupType.LargeCaution);
                if (!StartAbsorbStage(ent, target, 2))
                    ClearAbsorbEffects(ent);
                break;
            default:
                ClearAbsorbEffects(ent);
                Drag(ent, target, 1f);
                Spawn(AbsorbEngulf, Transform(ent).Coordinates);
                Absorb(ent, target);
                break;
        }
    }

    /// <summary>
    /// The corpse is swallowed whole. Its look can be taken later with the transformation action.
    /// </summary>
    public void Absorb(Entity<ZakatPraetorianComponent> ent, EntityUid target)
    {
        var name = Name(target);
        var container = _container.EnsureContainer<Container>(ent, AbsorbedContainer);
        if (!_container.Insert(target, container))
            return;

        ent.Comp.Absorbed.Add(target);
        ent.Comp.SelectedLook = ent.Comp.Absorbed.Count - 1;
        _popup.PopupEntity(Loc.GetString("zakat-absorb-done", ("name", name)), ent, ent, PopupType.Medium);
    }

    private void OnShift(Entity<ZakatPraetorianComponent> ent, ref ZakatShiftActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!PruneLooks(ent))
            return;

        ent.Comp.SelectedLook = (ent.Comp.SelectedLook + 1) % ent.Comp.Absorbed.Count;
        _popup.PopupEntity(Loc.GetString("zakat-shift-selected", ("name", Name(ent.Comp.Absorbed[ent.Comp.SelectedLook]))), ent, ent);
    }

    private void OnTransform(Entity<ZakatPraetorianComponent> ent, ref ZakatTransformActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!PruneLooks(ent))
            return;

        if (ent.Comp.SelectedLook < 0 || ent.Comp.SelectedLook >= ent.Comp.Absorbed.Count)
            ent.Comp.SelectedLook = 0;
        WearLook(ent, ent.Comp.SelectedLook);
    }

    /// <summary>Forgets deleted bodies; false (with a popup) when no look is left.</summary>
    private bool PruneLooks(Entity<ZakatPraetorianComponent> ent)
    {
        var selected = ent.Comp.SelectedLook >= 0 && ent.Comp.SelectedLook < ent.Comp.Absorbed.Count
            ? ent.Comp.Absorbed[ent.Comp.SelectedLook]
            : (EntityUid?) null;
        ent.Comp.Absorbed.RemoveAll(e => TerminatingOrDeleted(e));
        ent.Comp.SelectedLook = selected is { } sel ? ent.Comp.Absorbed.IndexOf(sel) : -1;
        if (ent.Comp.Absorbed.Count > 0)
            return true;

        _popup.PopupEntity(Loc.GetString("zakat-shift-nobody"), ent, ent);
        return false;
    }

    /// <summary>
    /// Body, face, name and clothes of a swallowed victim. The clothes are copies without any stats.
    /// </summary>
    public void WearLook(Entity<ZakatPraetorianComponent> ent, int index)
    {
        if (index < 0 || index >= ent.Comp.Absorbed.Count)
            return;

        var victim = ent.Comp.Absorbed[index];
        if (TerminatingOrDeleted(victim))
            return;

        ent.Comp.CurrentLook = index;
        _humanoid.CloneAppearance(victim, ent);
        _meta.SetEntityName(ent, Name(victim));

        foreach (var old in ent.Comp.LookClothes)
        {
            if (!TerminatingOrDeleted(old))
                Del(old);
        }
        ent.Comp.LookClothes.Clear();

        foreach (var slot in LookSlots)
        {
            // its own clothes fall off where the copies go
            if (_inventory.TryGetSlotEntity(ent, slot, out var worn) && !HasComp<ZakatLookClothingComponent>(worn))
                _inventory.TryUnequip(ent, slot, silent: true, force: true);

            if (!_inventory.TryGetSlotEntity(victim, slot, out var item) || Prototype(item.Value) is not { } proto)
                continue;

            var copy = Spawn(proto.ID, Transform(ent).Coordinates);
            Strip(copy);
            if (_inventory.TryGetSlotEntity(ent, slot, out var stale))
                Del(stale);
            if (!_inventory.TryEquip(ent, copy, slot, silent: true, force: true))
            {
                QueueDel(copy);
                continue;
            }

            ent.Comp.LookClothes.Add(copy);
        }

        _popup.PopupEntity(Loc.GetString("zakat-shift"), ent, ent, PopupType.Medium);
    }

    /// <summary>
    /// Only the look is copied: no protection, no speed changes, no pockets.
    /// </summary>
    private void Strip(EntityUid copy)
    {
        RemComp<ArmorComponent>(copy);
        RemComp<ClothingSpeedModifierComponent>(copy);
        RemComp<PressureProtectionComponent>(copy);
        RemComp<TemperatureProtectionComponent>(copy);
        RemComp<ExplosionResistanceComponent>(copy);

        if (TryComp<ContainerManagerComponent>(copy, out var containers))
        {
            foreach (var container in _container.GetAllContainers(copy, containers))
            {
                foreach (var inside in container.ContainedEntities.ToArray())
                    QueueDel(inside);
            }
        }

        RemComp<StorageComponent>(copy);
        EnsureComp<ZakatLookClothingComponent>(copy);
    }

    #endregion

    #region hunter

    private void OnHunterInit(Entity<ZakatHunterComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.DevourAction, ent.Comp.DevourActionProto);
    }

    private void OnHunterDevour(Entity<ZakatHunterComponent> ent, ref ZakatHunterDevourActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartDevour(ent, args.Target);
    }

    public bool CanDevour(EntityUid uid, EntityUid target)
    {
        return target != uid && HasComp<MobStateComponent>(target) && !_mobState.IsDead(target)
            && !HasComp<ZakatCreatureComponent>(target) && !HasComp<ZakatFleshFriendComponent>(target) && _mobState.IsAlive(uid);
    }

    public bool TryStartDevour(Entity<ZakatHunterComponent> ent, EntityUid target)
    {
        if (!CanDevour(ent, target))
        {
            _popup.PopupEntity(Loc.GetString("zakat-hunter-devour-bad-target"), ent, ent);
            return false;
        }

        var args = new DoAfterArgs(EntityManager, ent, ent.Comp.DevourTime, new ZakatHunterDevourDoAfterEvent(), ent, target)
        {
            BreakOnMove = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
        };
        if (!_doAfter.TryStartDoAfter(args))
            return false;

        _popup.PopupEntity(Loc.GetString("zakat-hunter-devour-start", ("target", target)), target, PopupType.LargeCaution);
        return true;
    }

    private void OnHunterDevourDone(Entity<ZakatHunterComponent> ent, ref ZakatHunterDevourDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || !CanDevour(ent, target))
            return;

        args.Handled = true;
        Devour(ent, target);
    }

    /// <summary>
    /// The prey is torn apart, the hunter heals.
    /// </summary>
    public void Devour(Entity<ZakatHunterComponent> ent, EntityUid target)
    {
        _puddle.TrySpillAt(Transform(target).Coordinates, new Solution("Blood", FixedPoint2.New(DevourBlood)), out _, sound: false);
        if (HasComp<BodyComponent>(target))
            _body.GibBody(target, gibOrgans: true);
        else
            QueueDel(target);

        _damageable.HealEvenly(ent.Owner, -ent.Comp.DevourHeal, origin: ent);
        _popup.PopupEntity(Loc.GetString("zakat-devour-success"), ent, PopupType.LargeCaution);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ZakatPraetorianComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextRegen)
                continue;

            comp.NextRegen = now + TimeSpan.FromSeconds(1);
            if (!_mobState.IsDead(uid))
                _damageable.HealEvenly(uid, -RegenPerSecond, origin: uid);
        }
    }
}
