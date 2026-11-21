using Content.Server.RoundEnd;
using Content.Goobstation.Server.Disease;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Goobstation.Shared.Disease.Components;
using Content.Server.GameTicking;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusIcon.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// Server logic of the sentient disease «Закат» (prototype 1): biomass, the two free picks,
/// strain upgrades (disease neural map), host evolution (host neural map), host tracking and the round-end line.
/// The strain is a normal Goob disease (genotype, vaccines, Devirate) with natural immunity switched off.
/// </summary>
public sealed partial class ZakatSystem : EntitySystem
{
    [Dependency] private DiseaseSystem _disease = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ZakatSymptomSystem _symptoms = default!;
    [Dependency] private ZakatSlaveSystem _slaves = default!;
    [Dependency] private ZakatPossessionSystem _possession = default!;
    [Dependency] private RoundEndSystem _roundEnd = default!;
    [Dependency] private ZakatVineSystem _vines = default!;

    /// <summary>
    /// Round-end lines, kept after the disease itself is gone.
    /// </summary>
    private readonly Dictionary<EntityUid, string> _summaries = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ZakatComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ZakatComponent, ZakatInfectActionEvent>(OnInfect);
        SubscribeLocalEvent<ZakatComponent, ZakatOpenShopActionEvent>(OnOpenShop);
        SubscribeLocalEvent<ZakatComponent, ZakatHostEvolveActionEvent>(OnHostEvolve);

        Subs.BuiEvents<ZakatComponent>(ZakatUiKey.Shop, subs =>
        {
            subs.Event<ZakatBuyGrowthMessage>(OnBuyGrowthMessage);
        });
        Subs.BuiEvents<ZakatComponent>(ZakatUiKey.Host, subs =>
        {
            subs.Event<ZakatBuySymptomMessage>(OnBuySymptomMessage);
            subs.Event<ZakatEnslaveMessage>(OnEnslaveMessage);
            subs.Event<ZakatBuyMutationMessage>(OnBuyMutationMessage);
        });

        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _summaries.Clear());
    }

    #region lifecycle

    private void OnMapInit(Entity<ZakatComponent> ent, ref MapInitEvent args)
    {
        var strain = Spawn(ent.Comp.StrainPrototype, MapCoordinates.Nullspace);
        ent.Comp.Strain = strain;
        ent.Comp.Genotype = _random.Next(1, int.MaxValue);
        _disease.SetGenotype(strain, ent.Comp.Genotype);
        ApplyStrain(ent, strain);

        var now = _timing.CurTime;
        ent.Comp.NextUpdate = now + ent.Comp.UpdateInterval;
        ent.Comp.NextPassive = now + ent.Comp.PassiveInterval;

        if (ent.Comp.StartPicksLeft > 0)
            _actions.AddAction(ent, ref ent.Comp.InfectActionEntity, ent.Comp.InfectAction);

        _alerts.ShowAlert(ent.Owner, ent.Comp.BiomassAlert);
        Dirty(ent);
    }

    private void OnShutdown(Entity<ZakatComponent> ent, ref ComponentShutdown args)
    {
        _possession.EndPossession(ent);
        if (ent.Comp.Strain is { } strain && !TerminatingOrDeleted(strain))
            QueueDel(strain);
    }

    #endregion

    #region actions

    private void OnInfect(Entity<ZakatComponent> ent, ref ZakatInfectActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartPick(ent, args.Target);
    }

    /// <summary>
    /// One of the free starting picks. The only way to infect by hand: after the picks are spent the action is gone.
    /// </summary>
    public bool TryStartPick(Entity<ZakatComponent> ent, EntityUid target)
    {
        if (ent.Comp.StartPicksLeft <= 0 || ent.Comp.Strain is not { } strain)
            return false;

        if (IsInfected(ent.Comp, target))
        {
            _popup.PopupEntity(Loc.GetString("zakat-infect-already", ("target", target)), ent, ent);
            return false;
        }

        if (!ZakatHelpers.IsOrganic(EntityManager, target) || !_mobState.IsAlive(target))
        {
            _popup.PopupEntity(Loc.GetString("zakat-infect-not-organic"), ent, ent);
            return false;
        }

        if (!InfectForced(ent, target))
            return false;

        ent.Comp.StartPicksLeft--;
        if (ent.Comp.StartPicksLeft <= 0)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.InfectActionEntity);
            ent.Comp.InfectActionEntity = null;
        }

        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("zakat-infect-start-success",
            ("target", target), ("left", ent.Comp.StartPicksLeft)), ent, ent, PopupType.Medium);
        return true;
    }

    private void OnOpenShop(Entity<ZakatComponent> ent, ref ZakatOpenShopActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.TryToggleUi(ent.Owner, ZakatUiKey.Shop, ent.Owner);
        UpdateShopUi(ent);
    }

    private void OnHostEvolve(Entity<ZakatComponent> ent, ref ZakatHostEvolveActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ZakatHostComponent>(args.Target, out var host) || host.Zakat != ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString("zakat-host-not-infected"), ent, ent);
            return;
        }

        args.Handled = true;
        ent.Comp.ViewedHost = args.Target;
        _ui.OpenUi(ent.Owner, ZakatUiKey.Host, ent.Owner);
        UpdateHostUi(ent);
    }

    #endregion

    #region shop

    private void OnBuyGrowthMessage(Entity<ZakatComponent> ent, ref ZakatBuyGrowthMessage args)
    {
        TryBuyGrowth(ent);
    }

    private void OnBuySymptomMessage(Entity<ZakatComponent> ent, ref ZakatBuySymptomMessage args)
    {
        if (GetEntity(args.Host) is { Valid: true } host && host == ent.Comp.ViewedHost)
            TryBuySymptom(ent, host, args.Symptom);
    }

    private void OnEnslaveMessage(Entity<ZakatComponent> ent, ref ZakatEnslaveMessage args)
    {
        if (GetEntity(args.Host) is { Valid: true } host && host == ent.Comp.ViewedHost)
            TryEnslave(ent, host);
    }

    private void OnBuyMutationMessage(Entity<ZakatComponent> ent, ref ZakatBuyMutationMessage args)
    {
        if (GetEntity(args.Host) is { Valid: true } host && host == ent.Comp.ViewedHost)
            TryBuyMutation(ent, host, args.Mutation);
    }

    public void AddBiomass(Entity<ZakatComponent> ent, float amount)
    {
        ent.Comp.Biomass += amount;
        Dirty(ent);
        UpdateUis(ent);
    }

    public bool TryPay(Entity<ZakatComponent> ent, float cost)
    {
        if (ent.Comp.Biomass < cost)
        {
            _popup.PopupEntity(Loc.GetString("zakat-not-enough-biomass", ("cost", cost)), ent, ent);
            return false;
        }

        ent.Comp.Biomass -= cost;
        Dirty(ent);
        return true;
    }

    /// <summary>
    /// Disease map: the next «Рост» of the core.
    /// </summary>
    public bool TryBuyGrowth(Entity<ZakatComponent> ent)
    {
        var level = ent.Comp.Growth;
        if (level >= ZakatGrowth.MaxGrowth)
            return false;

        if (!CanGrow(ent.Comp, level + 1))
        {
            _popup.PopupEntity(Loc.GetString(level + 1 == ZakatGrowth.VineLevel ? "zakat-growth-locked" : "zakat-growth-locked-hosts"), ent, ent);
            return false;
        }

        if (!TryPay(ent, ZakatGrowth.GrowthCosts[level]))
            return false;

        SetGrowth(ent, level + 1);
        _popup.PopupEntity(Loc.GetString("zakat-growth-success"), ent, ent, PopupType.Medium);
        UpdateUis(ent);
        return true;
    }

    /// <summary>
    /// Рост 4 opens only once the born disease holds <see cref="ZakatGrowth.VineTiles"/> tiles of flesh.
    /// </summary>
    public static bool CanGrow(ZakatComponent comp, int level)
    {
        if (comp.LivingHosts < ZakatGrowth.RequiredHosts(level))
            return false;

        if (level != ZakatGrowth.VineLevel)
            return true;

        return comp.Born && !comp.Defeated && comp.CapturedTiles >= ZakatGrowth.VineTiles;
    }

    /// <summary>
    /// Sets the core growth and grants what every reached level opens.
    /// </summary>
    public void SetGrowth(Entity<ZakatComponent> ent, int level)
    {
        ent.Comp.Growth = Math.Clamp(level, 0, ZakatGrowth.MaxGrowth);
        Dirty(ent);

        if (ent.Comp.Growth >= ZakatGrowth.PossessLevel && ent.Comp.PossessActionEntity == null)
            _actions.AddAction(ent, ref ent.Comp.PossessActionEntity, ent.Comp.PossessAction);

        if (ent.Comp.Growth >= ZakatGrowth.HiveMindLevel)
            _slaves.JoinHiveMind(ent.Owner, ent.Comp);

        if (ent.Comp.Growth >= ZakatGrowth.BirthLevel && !ent.Comp.Born && ent.Comp.BirthActionEntity == null)
            _actions.AddAction(ent, ref ent.Comp.BirthActionEntity, ent.Comp.BirthAction);

        if (ent.Comp.Growth >= ZakatGrowth.VineLevel && !ent.Comp.VinesReleased && _vines.Release(ent))
        {
            // the vines are the end: the round-end summary (greentext) must already say so
            _summaries[ent.Owner] = BuildSummary(ent);
            if (ent.Comp.VinesEndRound)
                _roundEnd.EndRound(ent.Comp.VineRoundRestart);
        }
    }

    /// <summary>
    /// Host neural map: buy a symptom for ONE host. Nodes unlock in order along each branch.
    /// </summary>
    public bool TryBuySymptom(Entity<ZakatComponent> ent, EntityUid hostUid, ZakatSymptom symptom)
    {
        if (!TryComp<ZakatHostComponent>(hostUid, out var host) || host.Zakat != ent.Owner)
            return false;

        if (host.Symptoms.Contains(symptom))
            return false;

        if (ZakatSymptoms.Previous(symptom) is { } prev && !host.Symptoms.Contains(prev))
            return false;

        // a slave has no use for symptoms that only cripple it
        if (host.Enslaved && ZakatSymptoms.IsDebuff(symptom))
            return false;

        var cost = ZakatSymptoms.Get(symptom).Cost;
        if (!TryPay(ent, cost))
            return false;

        _symptoms.AddSymptom((hostUid, host), symptom);
        _popup.PopupEntity(Loc.GetString("zakat-symptom-bought",
            ("symptom", Loc.GetString(ZakatSymptoms.LocId(symptom))), ("target", hostUid)), ent, ent, PopupType.Medium);
        UpdateUis(ent);
        return true;
    }

    /// <summary>
    /// The host's central neuron «порабощение»: the host becomes a «раб улья». Needs «Рост 2» (the hive mind).
    /// </summary>
    public bool TryEnslave(Entity<ZakatComponent> ent, EntityUid hostUid)
    {
        if (!TryComp<ZakatHostComponent>(hostUid, out var host) || host.Zakat != ent.Owner || host.Enslaved)
            return false;

        if (ent.Comp.Growth < ZakatGrowth.HiveMindLevel || !_slaves.CanEnslave(hostUid))
            return false;

        if (!TryPay(ent, ZakatGrowth.EnslaveCost))
            return false;

        _slaves.Enslave(ent, (hostUid, host));
        _popup.PopupEntity(Loc.GetString("zakat-enslave-success", ("target", hostUid)), ent, ent, PopupType.Medium);
        UpdateUis(ent);
        return true;
    }

    /// <summary>
    /// Slave circular map: any mutation, any order.
    /// </summary>
    public bool TryBuyMutation(Entity<ZakatComponent> ent, EntityUid hostUid, ZakatMutation mutation)
    {
        if (!TryComp<ZakatHostComponent>(hostUid, out var host) || host.Zakat != ent.Owner || !host.Enslaved
            || HasComp<ZakatPraetorianComponent>(hostUid))
            return false;

        if (host.Mutations.Contains(mutation) || !TryPay(ent, ZakatGrowth.Cost(mutation)))
            return false;

        _slaves.AddMutation((hostUid, host), mutation);
        _popup.PopupEntity(Loc.GetString("zakat-symptom-bought",
            ("symptom", Loc.GetString(ZakatGrowth.LocId(mutation))), ("target", hostUid)), ent, ent, PopupType.Medium);
        UpdateUis(ent);
        return true;
    }

    /// <summary>
    /// How contagious one host is: strain infectivity plus that host's own symptoms.
    /// </summary>
    public float GetHostInfectivity(Entity<ZakatComponent> ent, ZakatHostComponent? host)
    {
        var value = 1f;
        if (host != null)
        {
            foreach (var symptom in host.Symptoms)
                value += ZakatSymptoms.InfectivityBonus(symptom);
        }

        return value;
    }

    public void UpdateUis(Entity<ZakatComponent> ent)
    {
        UpdateShopUi(ent);
        UpdateHostUi(ent);
    }

    private void UpdateShopUi(Entity<ZakatComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, ZakatUiKey.Shop))
            return;

        _ui.SetUiState(ent.Owner, ZakatUiKey.Shop, new ZakatShopState(ent.Comp.Biomass, ent.Comp.Growth, ent.Comp.CapturedTiles, ent.Comp.LivingHosts));
    }

    private void UpdateHostUi(Entity<ZakatComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, ZakatUiKey.Host))
            return;

        if (ent.Comp.ViewedHost is not { } hostUid || !TryComp<ZakatHostComponent>(hostUid, out var host) || host.Zakat != ent.Owner)
        {
            ent.Comp.ViewedHost = null;
            _ui.CloseUi(ent.Owner, ZakatUiKey.Host);
            return;
        }

        _ui.SetUiState(ent.Owner, ZakatUiKey.Host, new ZakatHostState(GetNetEntity(hostUid), Name(hostUid),
            ent.Comp.Biomass, ent.Comp.Growth, new List<ZakatSymptom>(host.Symptoms), host.Enslaved,
            new List<ZakatMutation>(host.Mutations), HasComp<ZakatPraetorianComponent>(hostUid)));
    }

    #endregion

    #region strain

    /// <summary>
    /// Brings one disease entity of this strain to the current stats.
    /// Host symptoms are not disease effects (they live on <see cref="ZakatHostComponent"/>), so they never spread with a clone.
    /// </summary>
    public void ApplyStrain(Entity<ZakatComponent> ent, EntityUid disease)
    {
        if (!TryComp<DiseaseComponent>(disease, out var comp))
            return;

        // p4 (vaccine, swabs) hooks in here; the strain itself has no tunable stats any more
        _disease.SetCureMultiplier((disease, comp), 1f);
    }

    /// <summary>
    /// Puts a fresh copy of the strain into the target, no rolls. Used by the free picks and by successful contagion.
    /// </summary>
    public bool InfectForced(Entity<ZakatComponent> ent, EntityUid target)
    {
        if (ent.Comp.Strain is not { } strain || HasComp<ZakatCreatureComponent>(target))
            return false;

        EnsureComp<DiseaseCarrierComponent>(target);
        var clone = _disease.TryClone(strain);
        if (clone == null)
            return false;

        if (!_disease.TryInfect(target, clone.Value, force: true))
        {
            QueueDel(clone.Value);
            return false;
        }

        RegisterHost(ent, target);
        return true;
    }

    /// <summary>
    /// Carries the strain, or is one of the disease's own creatures (they are flesh of the disease itself).
    /// </summary>
    public bool IsInfected(ZakatComponent comp, EntityUid uid)
    {
        if (IsCreatureOf(comp, uid))
            return true;

        return TryComp<DiseaseCarrierComponent>(uid, out var carrier)
               && _disease.TryFindDisease((uid, carrier), comp.Genotype, out _);
    }

    /// <summary>
    /// A creature of this disease (биошмат, hunter, flesh humanoid).
    /// </summary>
    public bool IsCreatureOf(ZakatComponent comp, EntityUid uid)
    {
        return TryComp<ZakatCreatureComponent>(uid, out var creature)
               && creature.Zakat is { } owner
               && TryComp<ZakatComponent>(owner, out var ownerComp)
               && ownerComp == comp;
    }

    public bool IsImmune(ZakatComponent comp, EntityUid uid)
    {
        return _disease.HasImmunity(uid, comp.Genotype);
    }

    /// <summary>
    /// Marks a host and pays for it the first time.
    /// </summary>
    private void RegisterHost(Entity<ZakatComponent> ent, EntityUid uid)
    {
        var host = EnsureComp<ZakatHostComponent>(uid);
        host.Zakat = ent.Owner;
        EnsureComp<StatusIconComponent>(uid);

        if (!ent.Comp.KnownHosts.Add(uid))
            return;

        ent.Comp.Biomass += ent.Comp.BiomassPerNewHost;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("zakat-new-host", ("target", uid), ("gain", ent.Comp.BiomassPerNewHost)), ent, ent);
    }

    #endregion

    #region update

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ZakatComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.NextPassive)
            {
                comp.NextPassive = now + comp.PassiveInterval;
                PassiveIncome((uid, comp));
            }

            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + comp.UpdateInterval;
            UpdateZakat((uid, comp));
        }
    }

    /// <summary>
    /// +1 biomass per living host every minute.
    /// </summary>
    public void PassiveIncome(Entity<ZakatComponent> ent)
    {
        var living = 0;
        var hosts = EntityQueryEnumerator<ZakatHostComponent, MobStateComponent>();
        while (hosts.MoveNext(out var uid, out var host, out var mob))
        {
            if (host.Zakat == ent.Owner && _mobState.IsAlive(uid, mob))
                living++;
        }

        if (living == 0)
            return;

        ent.Comp.Biomass += living * ent.Comp.BiomassPerLivingHost;
        Dirty(ent);
        UpdateUis(ent);
    }

    public void UpdateZakat(Entity<ZakatComponent> ent)
    {
        var infected = new HashSet<NetEntity>();
        var immune = new HashSet<NetEntity>();
        var healthy = 0;
        var crew = 0;
        var crewInfected = 0;
        var aliveInfected = 0;
        var livingHosts = 0;

        var mobs = EntityQueryEnumerator<MobStateComponent>();
        while (mobs.MoveNext(out var uid, out var mobState))
        {
            // the disease's own creatures: always infected flesh, never hosts, never crew, never healthy
            if (IsCreatureOf(ent.Comp, uid))
            {
                infected.Add(GetNetEntity(uid));
                EnsureComp<StatusIconComponent>(uid);
                if (_mobState.IsAlive(uid, mobState))
                    aliveInfected++;
                continue;
            }

            if (!ZakatHelpers.IsOrganic(EntityManager, uid))
                continue;

            var alive = _mobState.IsAlive(uid, mobState);

            // every organism can catch Закат, including animals that normally carry nothing
            if (alive)
            {
                EnsureComp<DiseaseCarrierComponent>(uid);
                EnsureComp<StatusIconComponent>(uid);
                EnsureComp<ZakatContactComponent>(uid);
            }

            var isInfected = IsInfected(ent.Comp, uid);
            if (isInfected)
            {
                infected.Add(GetNetEntity(uid));
                if (alive)
                {
                    aliveInfected++;
                    livingHosts++;
                }

                // caught some other way (e.g. Goob blood spread): still ours
                if (!TryComp<ZakatHostComponent>(uid, out var hostComp) || hostComp.Zakat != ent.Owner)
                    RegisterHost(ent, uid);
            }
            else
            {
                // cured: the host loses everything Закат grew in it
                if (TryComp<ZakatHostComponent>(uid, out var hostComp) && hostComp.Zakat == ent.Owner)
                    _symptoms.ClearHost((uid, hostComp));

                if (IsImmune(ent.Comp, uid))
                    immune.Add(GetNetEntity(uid));
                else if (alive)
                    healthy++;
            }

            if (alive && HasComp<HumanoidAppearanceComponent>(uid)
                && TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
            {
                crew++;
                if (isInfected)
                    crewInfected++;
            }
        }

        var captured = 0;
        var tiles = EntityQueryEnumerator<ZakatFleshTileComponent>();
        while (tiles.MoveNext(out var tile))
        {
            if (tile.Zakat == ent.Owner && tile.Active && !tile.Wall)
                captured++;
        }

        if (captured != ent.Comp.CapturedTiles || livingHosts != ent.Comp.LivingHosts)
        {
            ent.Comp.CapturedTiles = captured;
            ent.Comp.LivingHosts = livingHosts;
            Dirty(ent);
        }

        var changed = !infected.SetEquals(ent.Comp.Infected) || !immune.SetEquals(ent.Comp.Immune)
                      || healthy != ent.Comp.HealthyCount || crew != ent.Comp.CrewTotal || crewInfected != ent.Comp.CrewInfected;
        ent.Comp.Infected = infected;
        ent.Comp.Immune = immune;
        ent.Comp.HealthyCount = healthy;
        ent.Comp.CrewTotal = crew;
        ent.Comp.CrewInfected = crewInfected;
        ent.Comp.PeakInfected = Math.Max(ent.Comp.PeakInfected, infected.Count);
        if (crew > 0)
            ent.Comp.PeakCrewShare = Math.Max(ent.Comp.PeakCrewShare, (float) crewInfected / crew);
        if (changed)
            Dirty(ent);

        // full station takeover: every living crew member carries the strain
        if (!ent.Comp.Victory && crew > 0 && crewInfected == crew)
        {
            ent.Comp.Victory = true;
            _popup.PopupEntity(Loc.GetString("zakat-victory"), ent, ent, PopupType.LargeCaution);
        }

        _summaries[ent.Owner] = BuildSummary(ent);
        UpdateUis(ent);

        // no hosts and no picks left: Закат dies out
        if (ent.Comp.StartPicksLeft <= 0 && aliveInfected == 0)
        {
            _popup.PopupEntity(Loc.GetString("zakat-extinct"), ent, ent, PopupType.LargeCaution);
            QueueDel(ent);
        }
    }

    /// <summary>
    /// Share of the living station crew carrying the strain (animals excluded), 0..1.
    /// </summary>
    public static float CrewShare(ZakatComponent comp) => comp.CrewTotal == 0 ? 0f : (float) comp.CrewInfected / comp.CrewTotal;

    #endregion

    #region round end

    private string BuildSummary(Entity<ZakatComponent> ent)
    {
        var player = TryComp<ActorComponent>(ent, out var actor) ? actor.PlayerSession.Name : Loc.GetString("zakat-roundend-unknown");
        return Loc.GetString("zakat-roundend",
            ("player", player),
            ("percent", (int) MathF.Round(CrewShare(ent.Comp) * 100f)),
            ("crewInfected", ent.Comp.CrewInfected),
            ("crew", ent.Comp.CrewTotal),
            ("peak", (int) MathF.Round(ent.Comp.PeakCrewShare * 100f)),
            ("infected", ent.Comp.Infected.Count),
            ("immune", ent.Comp.Immune.Count),
            ("victory", ent.Comp.VinesReleased ? "vines" : ent.Comp.Victory ? "yes" : "no"));
    }

    /// <summary>
    /// This disease's line for the round-end summary, as it stands now.
    /// </summary>
    public string? Summary(EntityUid uid) => _summaries.GetValueOrDefault(uid);

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        foreach (var line in _summaries.Values)
            args.AddLine(line);
    }

    #endregion
}
