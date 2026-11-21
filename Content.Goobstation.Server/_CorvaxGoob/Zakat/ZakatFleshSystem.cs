using Content.Goobstation.Common.Atmos;
using Content.Goobstation.Common.Body.Components;
using Content.Goobstation.Common.Temperature.Components;
using System.Linq;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Doors.Components;
using Robust.Shared.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// Рост 3 «рождение» and everything after it: the core of flesh, captured tiles, buildings,
/// «освободить от оков», the loss of the core. Works like a blob made of meat.
/// </summary>
public sealed partial class ZakatFleshSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ZakatSystem _zakat = default!;
    [Dependency] private ZakatSlaveSystem _slaves = default!;
    [Dependency] private ZakatPossessionSystem _possession = default!;
    [Dependency] private ZakatPraetorianSystem _praetorian = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private ZakatVineSystem _vines = default!;
    [Dependency] private Content.Server.Atmos.EntitySystems.AtmosphereSystem _atmos = default!;
    [Dependency] private Content.Shared.Damage.Systems.DamageableSystem _damageable = default!;
    [Dependency] private Robust.Shared.Audio.Systems.SharedAudioSystem _audio = default!;

    public static readonly string Faction = "ZakatFlesh";
    public const string HandName = "ZakatFlesh";
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    public static readonly TimeSpan WallCheckInterval = TimeSpan.FromSeconds(5);
    public const float BurstBlood = 80f;

    private TimeSpan _nextWallCheck;
    private readonly List<EntityUid> _due = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatComponent, ZakatBirthActionEvent>(OnBirth);
        SubscribeLocalEvent<ZakatComponent, ZakatBuildActionEvent>(OnBuild);
        SubscribeLocalEvent<ZakatComponent, ZakatLiberateActionEvent>(OnLiberate);
        SubscribeLocalEvent<ZakatComponent, ZakatPraetorianActionEvent>(OnPraetorian);
        SubscribeLocalEvent<ZakatComponent, ZakatToCoreActionEvent>(OnToCore);

        SubscribeLocalEvent<ZakatFleshControllerComponent, AfterInteractEvent>(OnControllerInteract);
        SubscribeLocalEvent<ZakatCoreComponent, ComponentShutdown>(OnCoreShutdown);
        SubscribeLocalEvent<ZakatStructureComponent, MapInitEvent>(OnStructureInit);
        SubscribeLocalEvent<ZakatCreatureComponent, MindAddedMessage>(OnCreatureMind);

        SubscribeLocalEvent<ZakatHostComponent, ComponentStartup>(OnHostStartup);
        SubscribeLocalEvent<ZakatHostComponent, ComponentRemove>(OnHostRemove);
    }

    #region faction

    // the flesh and its creatures don't attack the infected
    private void OnHostStartup(Entity<ZakatHostComponent> ent, ref ComponentStartup args)
    {
        _faction.AddFaction(ent.Owner, Faction);
        EnsureComp<ZakatFleshFriendComponent>(ent);
    }

    private void OnHostRemove(Entity<ZakatHostComponent> ent, ref ComponentRemove args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        _faction.RemoveFaction(ent.Owner, Faction);
        if (!HasComp<ZakatCreatureComponent>(ent))
            RemComp<ZakatFleshFriendComponent>(ent);
    }

    #endregion

    #region birth

    private void OnBirth(Entity<ZakatComponent> ent, ref ZakatBirthActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryBirth(ent, args.Target) != null;
    }

    /// <summary>
    /// «рождение»: the infected bursts, the core of flesh takes its place. Returns the core.
    /// </summary>
    public EntityUid? TryBirth(Entity<ZakatComponent> ent, EntityUid target)
    {
        if (ent.Comp.Born || ent.Comp.Growth < ZakatGrowth.BirthLevel)
            return null;

        if (!IsOwnHost(ent, target) || !TryGetTile(Transform(target).Coordinates, out var grid, out var indices))
        {
            _popup.PopupEntity(Loc.GetString("zakat-host-not-infected"), ent, ent);
            return null;
        }

        if (ent.Comp.PossessedHost == target)
            _possession.EndPossession(ent);

        var center = _map.GridTileToLocal(grid, grid.Comp, indices);
        Burst(target);

        var core = Spawn(ent.Comp.CorePrototype, center);
        EnsureComp<ZakatCoreComponent>(core).Zakat = ent;
        ent.Comp.Core = core;
        ent.Comp.Born = true;
        ent.Comp.NextPassiveSpread = _timing.CurTime + ent.Comp.PassiveSpreadInterval;
        Dirty(ent);

        PlaceFlesh(ent, grid, indices, free: true);

        _actions.RemoveAction(ent.Owner, ent.Comp.BirthActionEntity);
        ent.Comp.BirthActionEntity = null;
        foreach (var proto in ent.Comp.FleshActions)
        {
            EntityUid? action = null;
            _actions.AddAction(ent, ref action, proto);
            if (action != null)
                ent.Comp.FleshActionEntities.Add(action.Value);
        }

        GiveController(ent);
        _transform.SetCoordinates(ent, center);
        _popup.PopupEntity(Loc.GetString("zakat-birth"), core, PopupType.LargeCaution);
        _zakat.UpdateUis(ent);
        return core;
    }

    private void GiveController(Entity<ZakatComponent> ent)
    {
        EnsureComp<HandsComponent>(ent);
        _hands.AddHand(ent.Owner, HandName, HandLocation.Middle);

        var controller = Spawn(ent.Comp.CaptureController, Transform(ent).Coordinates);
        EnsureComp<ZakatFleshControllerComponent>(controller).Zakat = ent;
        ent.Comp.ControllerEntity = controller;
        if (!_hands.TryPickup(ent, controller, HandName, checkActionBlocker: false, animate: false))
            QueueDel(controller);
    }

    /// <summary>
    /// An infected tears apart: blood, meat, organs.
    /// </summary>
    private void Burst(EntityUid target)
    {
        var coords = Transform(target).Coordinates;
        _puddle.TrySpillAt(coords, new Solution("Blood", FixedPoint2.New(BurstBlood)), out _, sound: false);
        if (HasComp<BodyComponent>(target))
            _body.GibBody(target, gibOrgans: true);
        else
            QueueDel(target);
    }

    #endregion

    #region capture

    private void OnControllerInteract(Entity<ZakatFleshControllerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakat))
            return;

        args.Handled = true;
        var zakatEnt = (ent.Comp.Zakat.Value, zakat);
        var now = _timing.CurTime;
        if (now < zakat.NextCapture)
            return;

        zakat.NextCapture = now + ZakatGrowth.CaptureCooldown;
        TryCapture(zakatEnt, args.ClickLocation);
    }

    /// <summary>
    /// Covers the tile at <paramref name="coords"/> with flesh: it must touch the disease's active flesh (side by side).
    /// A wall, window or airlock there is struck instead (blob-style, upd1). Returns the new flesh.
    /// </summary>
    public EntityUid? TryCapture(Entity<ZakatComponent> ent, EntityCoordinates coords)
    {
        if (!ent.Comp.Born || ent.Comp.Defeated || !TryGetTile(coords, out var grid, out var indices))
            return null;

        if (FleshAt(grid, indices) != null)
            return null;

        if (!TouchesFlesh(ent, grid, indices))
        {
            _popup.PopupCoordinates(Loc.GetString("zakat-capture-too-far"), coords, ent);
            return null;
        }

        if (Blocker(grid, indices, out var armored) is { } blocker)
        {
            TryAttack(ent, blocker);
            return null;
        }

        if (armored)
        {
            _popup.PopupCoordinates(Loc.GetString("zakat-attack-armored"), coords, ent);
            return null;
        }

        return PlaceFlesh(ent, grid, indices, free: false);
    }

    private bool TouchesFlesh(Entity<ZakatComponent> ent, Entity<MapGridComponent> grid, Vector2i indices)
    {
        foreach (var dir in Sides)
        {
            if (FleshAt(grid, indices + dir) is { } near && IsSpreading(ent, near))
                return true;
        }

        return false;
    }

    private static readonly Vector2i[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>
    /// upd1: the flesh strikes a wall, window or airlock next to it, the way the blob does: biomass for every blow,
    /// structural damage until it breaks. Armored doors hold.
    /// </summary>
    public bool TryAttack(Entity<ZakatComponent> ent, EntityUid target)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextAttack)
            return false;

        if (!_zakat.TryPay(ent, ZakatGrowth.AttackCost))
            return false;

        ent.Comp.NextAttack = now + ZakatGrowth.AttackCooldown;
        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                ["Structural"] = FixedPoint2.New(ZakatGrowth.AttackStructural),
                ["Blunt"] = FixedPoint2.New(ZakatGrowth.AttackBlunt),
            },
        };
        _damageable.TryChangeDamage(target, damage, origin: ent, canMiss: false);
        _audio.PlayPvs(AttackSound, target);
        Spawn(AttackEffect, Transform(target).Coordinates);
        _zakat.UpdateUis(ent);
        return true;
    }

    public static readonly SoundSpecifier AttackSound = new SoundPathSpecifier("/Audio/Animals/Blob/blobattack.ogg");
    public static readonly EntProtoId AttackEffect = "ZakatFleshStrike";

    /// <summary>
    /// upd1: what the flesh can't grow over: a hard wall, window or closed door. Null with
    /// <paramref name="armored"/> = true when only an armored door (or another unbreakable thing) stands there.
    /// The disease's own buildings don't count (they stand on its flesh).
    /// </summary>
    public EntityUid? Blocker(Entity<MapGridComponent> grid, Vector2i indices, out bool armored)
    {
        armored = false;
        foreach (var uid in _map.GetAnchoredEntities(grid, grid.Comp, indices))
        {
            if (!TryComp<PhysicsComponent>(uid, out var physics) || !physics.Hard)
                continue;

            if (HasComp<ZakatStructureComponent>(uid) || HasComp<ZakatCoreComponent>(uid) || HasComp<ZakatFleshTileComponent>(uid))
                continue;

            var door = CompOrNull<DoorComponent>(uid);
            if (door == null && !_tag.HasTag(uid, WallTag) && !_tag.HasTag(uid, WindowTag))
                continue;

            if (door != null && _vines.IsArmoredDoor(uid, door) || !HasComp<DamageableComponent>(uid))
            {
                armored = true;
                continue;
            }

            return uid;
        }

        return null;
    }

    private bool IsSpreading(Entity<ZakatComponent> ent, Entity<ZakatFleshTileComponent> flesh)
    {
        return flesh.Comp.Zakat == ent.Owner && flesh.Comp.Active && !flesh.Comp.Wall;
    }

    /// <summary>
    /// Flesh over a floor. Paid unless <paramref name="free"/>. Never over walls, windows or closed doors (upd1).
    /// </summary>
    public EntityUid? PlaceFlesh(Entity<ZakatComponent> ent, Entity<MapGridComponent> grid, Vector2i indices, bool free)
    {
        var tile = _map.GetTileRef(grid, grid.Comp, indices);
        if (tile.Tile.IsEmpty || FleshAt(grid, indices) != null)
            return null;

        if (Blocker(grid, indices, out var armored) != null || armored)
            return null;

        if (!free && !_zakat.TryPay(ent, ZakatGrowth.CaptureCost))
            return null;

        // flesh joins its neighbours by itself (corner smoothing), so it is never turned
        var flesh = Spawn(ent.Comp.FleshFloorPrototype, _map.GridTileToLocal(grid, grid.Comp, indices));

        var comp = EnsureComp<ZakatFleshTileComponent>(flesh);
        comp.Zakat = ent;
        comp.Wall = false;
        comp.Active = true;
        _appearance.SetData(flesh, ZakatFleshVisuals.Active, true);
        if (!free)
            _zakat.UpdateUis(ent);
        return flesh;
    }

    /// <summary>
    /// upd1: the passive spread (like the blob's): a random edge of the active flesh takes one free floor tile next
    /// to it. Free tile = floor (not space) without flesh and without anything hard anchored on it: the passive spread
    /// never breaks or covers walls, doors, windows, machines or furniture.
    /// </summary>
    public EntityUid? PassiveSpread(Entity<ZakatComponent> ent)
    {
        _spreadScratch.Clear();
        var query = EntityQueryEnumerator<ZakatFleshTileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flesh, out var xform))
        {
            if (flesh.Zakat == ent.Owner && flesh.Active && !flesh.Wall && xform.GridUid != null && xform.Anchored)
                _spreadScratch.Add(uid);
        }

        // a few tries: most flesh is inside the patch, with no free neighbour
        for (var attempt = 0; attempt < 8 && _spreadScratch.Count > 0; attempt++)
        {
            var from = _random.Pick(_spreadScratch);
            var xform = Transform(from);
            if (!TryComp<MapGridComponent>(xform.GridUid, out var gridComp))
                continue;

            var grid = (xform.GridUid.Value, gridComp);
            var indices = _map.TileIndicesFor(grid.Item1, gridComp, xform.Coordinates) + _random.Pick(Sides);
            if (!IsFreeTile(grid, indices))
                continue;

            return PlaceFlesh(ent, grid, indices, free: true);
        }

        return null;
    }

    private readonly List<EntityUid> _spreadScratch = new();

    public bool IsFreeTile(Entity<MapGridComponent> grid, Vector2i indices)
    {
        var tile = _map.GetTileRef(grid, grid.Comp, indices);
        if (tile.Tile.IsEmpty || FleshAt(grid, indices) != null)
            return false;

        foreach (var uid in _map.GetAnchoredEntities(grid, grid.Comp, indices))
        {
            if (TryComp<PhysicsComponent>(uid, out var physics) && physics.Hard)
                return false;
        }

        return true;
    }

    /// <summary>
    /// A hard wall or window on the tile (for the old wall growths, see <see cref="CheckWalls"/>).
    /// </summary>
    private (bool Wall, bool Window) WallKind(Entity<MapGridComponent> grid, Vector2i indices)
    {
        foreach (var uid in _map.GetAnchoredEntities(grid, grid.Comp, indices))
        {
            if (!TryComp<PhysicsComponent>(uid, out var physics) || !physics.Hard)
                continue;

            if (_tag.HasTag(uid, WallTag))
                return (true, false);

            if (_tag.HasTag(uid, WindowTag))
                return (true, true);
        }

        return (false, false);
    }

    public Entity<ZakatFleshTileComponent>? FleshAt(Entity<MapGridComponent> grid, Vector2i indices)
    {
        foreach (var uid in _map.GetAnchoredEntities(grid, grid.Comp, indices))
        {
            if (TryComp<ZakatFleshTileComponent>(uid, out var flesh) && !TerminatingOrDeleted(uid))
                return (uid, flesh);
        }

        return null;
    }

    private bool HasBuilding(Entity<MapGridComponent> grid, Vector2i indices)
    {
        foreach (var uid in _map.GetAnchoredEntities(grid, grid.Comp, indices))
        {
            if (HasComp<ZakatStructureComponent>(uid) || HasComp<ZakatCoreComponent>(uid))
                return true;
        }

        return false;
    }

    public bool TryGetTile(EntityCoordinates coords, out Entity<MapGridComponent> grid, out Vector2i indices)
    {
        grid = default;
        indices = default;
        if (!coords.IsValid(EntityManager))
            return false;

        // clicks come relative to the map or to the grid: find the grid under the point either way
        var mapCoords = _transform.ToMapCoordinates(coords);
        if (!_map.TryFindGridAt(mapCoords, out var gridUid, out var gridComp))
            return false;

        grid = (gridUid, gridComp);
        indices = _map.WorldToTile(gridUid, gridComp, mapCoords.Position);
        return true;
    }

    #endregion

    #region buildings

    private void OnBuild(Entity<ZakatComponent> ent, ref ZakatBuildActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryBuild(ent, args.Structure, args.Target) != null;
    }

    /// <summary>
    /// A building on the disease's own active flesh, one per tile.
    /// </summary>
    public EntityUid? TryBuild(Entity<ZakatComponent> ent, ZakatStructure structure, EntityCoordinates coords)
    {
        if (!ent.Comp.Born || ent.Comp.Defeated || !TryGetTile(coords, out var grid, out var indices))
            return null;

        if (FleshAt(grid, indices) is not { } flesh || !IsSpreading(ent, flesh) || HasBuilding(grid, indices))
        {
            _popup.PopupCoordinates(Loc.GetString("zakat-build-no-flesh"), coords, ent);
            return null;
        }

        if (!_zakat.TryPay(ent, ZakatGrowth.Cost(structure)))
            return null;

        var building = Spawn(ZakatGrowth.Prototype(structure), _map.GridTileToLocal(grid, grid.Comp, indices));
        var comp = EnsureComp<ZakatStructureComponent>(building);
        comp.Zakat = ent;
        comp.NextAt = _timing.CurTime + comp.Interval;
        _zakat.UpdateUis(ent);
        return building;
    }

    private void OnStructureInit(Entity<ZakatStructureComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextAt = _timing.CurTime + ent.Comp.Interval;
    }

    /// <summary>
    /// A creature of the flesh: hive mind, the disease's faction and, once someone takes it, the slave role.
    /// </summary>
    public void SetupCreature(EntityUid uid, Entity<ZakatComponent> zakat)
    {
        EnsureComp<ZakatCreatureComponent>(uid).Zakat = zakat;
        EnsureComp<ZakatFleshFriendComponent>(uid);
        if (TryComp<ZakatBioshmatComponent>(uid, out var shmat))
            shmat.Zakat = zakat;

        MakeSpaceproof(uid);
        _faction.AddFaction(uid, Faction);
        _slaves.JoinHiveMind(uid, zakat.Comp);
        _slaves.GiveRole(uid);
    }

    /// <summary>
    /// The flesh's monsters don't care for vacuum or cold.
    /// </summary>
    public void MakeSpaceproof(EntityUid uid)
    {
        EnsureComp<SpecialBreathingImmunityComponent>(uid);
        EnsureComp<SpecialPressureImmunityComponent>(uid);
        EnsureComp<SpecialLowTempImmunityComponent>(uid);
    }

    private void OnCreatureMind(Entity<ZakatCreatureComponent> ent, ref MindAddedMessage args)
    {
        _slaves.GiveRole(ent);
    }

    private void TickStructure(Entity<ZakatStructureComponent> ent, Entity<ZakatComponent> zakat)
    {
        if (ent.Comp.Income > 0)
            _zakat.AddBiomass(zakat, ent.Comp.Income);

        // upd1 «изрыгатель спор»: a puff of «вирусный газ заката»
        if (ent.Comp.GasMoles > 0 && _atmos.GetContainingMixture(ent.Owner, excite: true) is { } air)
        {
            air.AdjustMoles(ent.Comp.Gas, ent.Comp.GasMoles);
            if (ent.Comp.EmitEffect is { } puff)
                Spawn(puff, Transform(ent).Coordinates);
        }

        if (ent.Comp.Spawns.Count == 0)
            return;

        ent.Comp.Spawned.RemoveAll(uid => TerminatingOrDeleted(uid) || _mobState.IsDead(uid));
        if (!ent.Comp.Hatch && ent.Comp.Spawned.Count >= ent.Comp.MaxAlive)
            return;

        var creature = Spawn(_random.Pick(ent.Comp.Spawns), Transform(ent).Coordinates);
        SetupCreature(creature, zakat);
        ent.Comp.Spawned.Add(creature);

        if (ent.Comp.Hatch)
        {
            // the cocoon bursts; the flesh under it stays
            _popup.PopupEntity(Loc.GetString("zakat-cocoon-hatch"), creature, PopupType.LargeCaution);
            QueueDel(ent);
        }
    }

    #endregion

    #region abilities

    private void OnLiberate(Entity<ZakatComponent> ent, ref ZakatLiberateActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryLiberate(ent, args.Target);
    }

    /// <summary>
    /// «освободить от оков»: the infected bursts, the floor under it (3x2) turns to flesh, the disease feeds.
    /// </summary>
    public bool TryLiberate(Entity<ZakatComponent> ent, EntityUid target)
    {
        if (!ent.Comp.Born || ent.Comp.Defeated)
            return false;

        if (!IsOwnHost(ent, target) || !TryGetTile(Transform(target).Coordinates, out var grid, out var indices))
        {
            _popup.PopupEntity(Loc.GetString("zakat-host-not-infected"), ent, ent);
            return false;
        }

        // upd2: only the hive's slaves wear the shackles
        if (!TryComp<ZakatSlaveComponent>(target, out var slave) || slave.Zakat != ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString("zakat-liberate-not-slave"), ent, ent);
            return false;
        }

        if (ent.Comp.PossessedHost == target)
            _possession.EndPossession(ent);

        Burst(target);
        for (var dx = -(ZakatGrowth.LiberateWidth / 2); dx <= ZakatGrowth.LiberateWidth / 2; dx++)
        {
            for (var dy = 0; dy > -ZakatGrowth.LiberateHeight; dy--)
                PlaceFlesh(ent, grid, indices + new Vector2i(dx, dy), free: true);
        }

        _zakat.AddBiomass(ent, ZakatGrowth.LiberateBiomass);
        return true;
    }

    private void OnPraetorian(Entity<ZakatComponent> ent, ref ZakatPraetorianActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryMakePraetorian(ent, args.Target);
    }

    /// <summary>
    /// «создать преторианца» from a living humanoid infected.
    /// </summary>
    public bool TryMakePraetorian(Entity<ZakatComponent> ent, EntityUid target)
    {
        if (!ent.Comp.Born || ent.Comp.Defeated)
            return false;

        if (!TryComp<ZakatHostComponent>(target, out var host) || host.Zakat != ent.Owner
            || !_slaves.CanEnslave(target) || HasComp<ZakatPraetorianComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("zakat-praetorian-bad-target"), ent, ent);
            return false;
        }

        if (!_zakat.TryPay(ent, ZakatGrowth.PraetorianCost))
            return false;

        if (!host.Enslaved)
            _slaves.Enslave(ent, (target, host));

        _praetorian.Make(target, ent);
        _zakat.UpdateUis(ent);
        return true;
    }

    private void OnToCore(Entity<ZakatComponent> ent, ref ZakatToCoreActionEvent args)
    {
        if (args.Handled || ent.Comp.Core is not { } core || TerminatingOrDeleted(core))
            return;

        _transform.SetCoordinates(ent, Transform(core).Coordinates);
        args.Handled = true;
    }

    private bool IsOwnHost(Entity<ZakatComponent> ent, EntityUid target)
    {
        return TryComp<ZakatHostComponent>(target, out var host) && host.Zakat == ent.Owner;
    }

    #endregion

    #region loss

    private void OnCoreShutdown(Entity<ZakatCoreComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Zakat is not { } zakatUid || TerminatingOrDeleted(zakatUid)
            || !TryComp<ZakatComponent>(zakatUid, out var zakat) || zakat.Core != ent.Owner)
            return;

        Defeat((zakatUid, zakat));
    }

    /// <summary>
    /// The core is gone: «Закат» has lost, all its flesh dies down and its buildings stop.
    /// </summary>
    public void Defeat(Entity<ZakatComponent> ent)
    {
        if (ent.Comp.Defeated)
            return;

        ent.Comp.Defeated = true;
        ent.Comp.Core = null;
        Dirty(ent);

        var query = EntityQueryEnumerator<ZakatFleshTileComponent>();
        while (query.MoveNext(out var uid, out var flesh))
        {
            if (flesh.Zakat != ent.Owner)
                continue;

            flesh.Active = false;
            _appearance.SetData(uid, ZakatFleshVisuals.Active, false);
            RemComp<SpeedModifierContactsComponent>(uid);
        }

        foreach (var action in ent.Comp.FleshActionEntities)
            _actions.RemoveAction(ent.Owner, action);
        ent.Comp.FleshActionEntities.Clear();

        if (ent.Comp.ControllerEntity is { } controller)
            QueueDel(controller);
        ent.Comp.ControllerEntity = null;
        _hands.RemoveHand(ent.Owner, HandName);

        _popup.PopupEntity(Loc.GetString("zakat-defeat"), ent, ent, PopupType.LargeCaution);
        _zakat.UpdateUis(ent);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        _due.Clear();
        var structures = EntityQueryEnumerator<ZakatStructureComponent>();
        while (structures.MoveNext(out var uid, out var structure))
        {
            if (now >= structure.NextAt)
                _due.Add(uid);
        }

        foreach (var uid in _due)
        {
            if (!TryComp<ZakatStructureComponent>(uid, out var structure) || TerminatingOrDeleted(uid))
                continue;

            structure.NextAt = now + structure.Interval;
            // buildings live on the core: no core, no work
            if (!TryComp<ZakatComponent>(structure.Zakat, out var zakat) || zakat.Defeated || zakat.Core == null)
                continue;

            TickStructure((uid, structure), (structure.Zakat.Value, zakat));
        }

        // upd1: passive spread onto free floor
        var zakats = EntityQueryEnumerator<ZakatComponent>();
        while (zakats.MoveNext(out var zakatUid, out var zakatComp))
        {
            if (!zakatComp.Born || zakatComp.Defeated || zakatComp.Core == null || now < zakatComp.NextPassiveSpread)
                continue;

            zakatComp.NextPassiveSpread = now + zakatComp.PassiveSpreadInterval;
            for (var i = 0; i < zakatComp.PassiveSpreadTiles; i++)
                PassiveSpread((zakatUid, zakatComp));
        }

        if (now < _nextWallCheck)
            return;

        _nextWallCheck = now + WallCheckInterval;
        CheckWalls();
    }

    /// <summary>
    /// Growths over a wall that is gone fall onto the floor under it.
    /// </summary>
    private void CheckWalls()
    {
        _due.Clear();
        var query = EntityQueryEnumerator<ZakatFleshTileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flesh, out var xform))
        {
            if (!flesh.Wall || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            if (!WallKind((gridUid, grid), _map.TileIndicesFor(gridUid, grid, xform.Coordinates)).Wall)
                _due.Add(uid);
        }

        foreach (var uid in _due)
        {
            var flesh = Comp<ZakatFleshTileComponent>(uid);
            var xform = Transform(uid);
            var active = flesh.Active;
            var zakatUid = flesh.Zakat;
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            Del(uid);
            if (active && TryComp<ZakatComponent>(zakatUid, out var zakat))
                PlaceFlesh((zakatUid.Value, zakat), (gridUid, grid), indices, free: true);
        }
    }
}
