using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared.Actions;
using Content.Shared.Administration.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// «биошмат»: crawls into corpses and brings them back as slaves, or grows into a flesh humanoid on its own.
/// </summary>
public sealed partial class ZakatBioshmatSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ZakatSystem _zakat = default!;
    [Dependency] private ZakatSlaveSystem _slaves = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    private readonly List<EntityUid> _grow = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatBioshmatComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ZakatBioshmatComponent, ZakatBioshmatInfestActionEvent>(OnInfest);
        SubscribeLocalEvent<ZakatBioshmatComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMapInit(Entity<ZakatBioshmatComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.GrowAt = _timing.CurTime + ent.Comp.GrowTime;
        _actions.AddAction(ent, ref ent.Comp.InfestAction, ent.Comp.InfestActionProto);
    }

    private void OnMindAdded(Entity<ZakatBioshmatComponent> ent, ref MindAddedMessage args)
    {
        _slaves.GiveRole(ent);
    }

    private void OnInfest(Entity<ZakatBioshmatComponent> ent, ref ZakatBioshmatInfestActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryInfest(ent, args.Target);
    }

    /// <summary>
    /// Crawls into a humanoid corpse, revives it and makes it a slave of the hive.
    /// </summary>
    public bool TryInfest(Entity<ZakatBioshmatComponent> ent, EntityUid corpse)
    {
        if (!_mobState.IsAlive(ent) || !_mobState.IsDead(corpse)
            || !HasComp<HumanoidAppearanceComponent>(corpse) || !HasComp<BodyComponent>(corpse))
        {
            _popup.PopupEntity(Loc.GetString("zakat-bioshmat-not-corpse"), ent, ent);
            return false;
        }

        _rejuvenate.PerformRejuvenate(corpse);
        _popup.PopupEntity(Loc.GetString("zakat-bioshmat-infest", ("target", corpse)), corpse, PopupType.LargeCaution);
        BecomeSlave(ent, corpse);
        return true;
    }

    /// <summary>
    /// The shmat's mind moves into <paramref name="body"/>, which turns into an infected slave with no mutations yet.
    /// </summary>
    private void BecomeSlave(Entity<ZakatBioshmatComponent> ent, EntityUid body)
    {
        if (TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakat))
        {
            var zakatEnt = (ent.Comp.Zakat, zakat);
            _zakat.InfectForced(zakatEnt, body);
            if (TryComp<ZakatHostComponent>(body, out var host) && !host.Enslaved)
                _slaves.Enslave(zakatEnt, (body, host));
        }

        if (_mind.TryGetMind(ent, out var mindId, out var mind))
        {
            _mind.TransferTo(mindId, body, mind: mind);
            _slaves.GiveRole(body);
        }

        QueueDel(ent);
    }

    /// <summary>
    /// Five minutes alive: the shmat grows into a flesh humanoid.
    /// </summary>
    public EntityUid Grow(Entity<ZakatBioshmatComponent> ent)
    {
        var body = Spawn(ent.Comp.GrownPrototype, Transform(ent).Coordinates);
        if (HasComp<HumanoidAppearanceComponent>(body))
            _humanoid.SetSkinColor(body, ent.Comp.GrownSkin, verify: false);

        _metaData.SetEntityName(body, Loc.GetString("zakat-flesh-humanoid-name"));
        _popup.PopupEntity(Loc.GetString("zakat-bioshmat-grown"), body, PopupType.LargeCaution);
        BecomeSlave(ent, body);
        return body;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ZakatBioshmatComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // a shmat nobody drives stays a wild thing (ghost role) instead of growing an empty body
            if (now >= comp.GrowAt && _mobState.IsAlive(uid) && _mind.TryGetMind(uid, out _, out _))
                _grow.Add(uid);
        }

        foreach (var uid in _grow)
        {
            if (TryComp<ZakatBioshmatComponent>(uid, out var comp) && !TerminatingOrDeleted(uid))
                Grow((uid, comp));
        }

        _grow.Clear();
    }
}
