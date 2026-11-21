using Content.Shared.Station.Components;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared.Radio.Components;
using Content.Server.Silicons.StationAi;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._CorvaxGoob.CCCVars;
using Content.Shared.Chat;
using Content.Shared.Mind.Components;
using Content.Shared.Radio;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар»: runs a station AI core while no player is in it.
/// <para>Takeover/handoff: every <see cref="PulsarAutonomousAiComponent.StateCheckInterval"/> the core is looked at.
/// If the AI in the core has a mind (a player, connected or not: an SSD/disconnected player keeps their mind in the
/// body) or is remote-controlling a borg, the module is <see cref="PulsarState.Suspended"/>. It never moves,
/// removes or replaces anything in the core, so job spawns, late joins, admin takeover and reconnects work as
/// before. When the core becomes free the module waits <see cref="PulsarAutonomousAiComponent.VacancyGrace"/>
/// and then takes over again.</para>
/// <para>Costs: the state check is a few component lookups per core. Vision is one camera window
/// (32x32 tiles by default) per <see cref="PulsarAutonomousAiComponent.ScanInterval"/>, using the same
/// <see cref="StationAiVisionSystem"/> the AI player uses, so a whole station is resampled every ~10-15 s
/// without ever scanning the full map in one tick. Mobs/doors are refreshed once a second against the
/// cached visibility mask (O(humanoids + known doors)). Hearing is event driven.</para>
/// </summary>
public sealed partial class PulsarAutonomousAiSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly StationAiSystem _stationAi = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private bool _globalEnabled = true;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, AbisferaCVars.PulsarEnabled, v => _globalEnabled = v, true);

        SubscribeLocalEvent<PulsarAutonomousAiComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PulsarAutonomousAiComponent, ComponentShutdown>(OnShutdown);

        InitializeVision();
        InitializeHearing();
        InitializeTargeting();
        InitializeAnnouncements();
    }

    private void OnMapInit(Entity<PulsarAutonomousAiComponent> ent, ref MapInitEvent args)
    {
        var now = _timing.CurTime;
        ent.Comp.StartedAt = now;
        ent.Comp.State = PulsarState.Starting;
        ent.Comp.NextStateCheck = now + ent.Comp.StartupDelay;
    }

    private void OnShutdown(Entity<PulsarAutonomousAiComponent> ent, ref ComponentShutdown args)
    {
        // Removed from a living core (admin): give it its normal name back. A deleted core needs nothing.
        if (ent.Comp.State == PulsarState.Active && !TerminatingOrDeleted(ent))
            Deactivate(ent, PulsarState.Disabled, raise: false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PulsarAutonomousAiComponent, StationAiCoreComponent>();
        while (query.MoveNext(out var uid, out var comp, out var core))
        {
            var ent = (uid, comp);

            if (now >= comp.NextStateCheck)
            {
                comp.NextStateCheck = now + comp.StateCheckInterval;
                UpdateState(ent, core, now);
            }

            if (comp.State != PulsarState.Active)
                continue;

            if (now >= comp.NextStationRefresh)
            {
                comp.NextStationRefresh = now + comp.StationRefreshInterval;
                RefreshStation(ent);
            }

            // Stage 3: laws (text compare every LawCheckInterval).
            CheckLaws(ent, now);

            if (now >= comp.NextScan)
            {
                comp.NextScan = now + comp.ScanInterval;
                ScanStep(ent, now);
            }

            // upd1: depressurisation lines (merged breaches, rate limited, one reminder).
            FlushBreaches(ent, now);

            // Stage 5.2: the linked units' own sensors (before the mob refresh, which uses what they see).
            UnitSensorStep(ent, now);

            if (now >= comp.NextMobRefresh)
            {
                comp.NextMobRefresh = now + comp.MobRefreshInterval;
                RefreshMobs(ent, now);
                RefreshDoors(ent, now);
                // Stage 3: alerts about people, protective bolts.
                CheckMobAlerts(ent, now);
                UpdateBolts(ent, now);
            }

            // Stage 2: understand what was heard since the last tick (deferred so replies are never sent from inside
            // the chat/radio code that delivered the line).
            ProcessIncoming(ent, now);

            if (now >= comp.NextThink)
            {
                comp.NextThink = now + comp.ThinkInterval;
                var ev = new PulsarThinkEvent(now);
                RaiseLocalEvent(uid, ref ev);
            }
        }
    }

    /// <summary>
    /// True when a player owns the AI in this core: the held AI has a mind (online, SSD or disconnected alike) or
    /// it is remote-controlling a borg right now. The module must stay out of the way then.
    /// </summary>
    public bool IsPlayerControlled(Entity<StationAiCoreComponent?> core)
    {
        if (!_stationAi.TryGetHeld(core, out var held))
            return false;

        if (TryComp<StationAiHeldComponent>(held, out var heldComp) && heldComp.CurrentConnectedEntity != null)
            return true;

        return TryComp<MindContainerComponent>(held, out var mind) && mind.HasMind;
    }

    private void UpdateState(Entity<PulsarAutonomousAiComponent> ent, StationAiCoreComponent core, TimeSpan now)
    {
        var comp = ent.Comp;

        if (!_globalEnabled || !comp.Enabled)
        {
            if (comp.State == PulsarState.Active)
                Deactivate(ent, PulsarState.Disabled);
            comp.State = PulsarState.Disabled;
            comp.VacantSince = null;
            return;
        }

        if (IsPlayerControlled((ent.Owner, core)))
        {
            comp.PlayerHeldSinceActive = true;
            if (comp.State == PulsarState.Active)
                Deactivate(ent, PulsarState.Suspended);
            comp.State = PulsarState.Suspended;
            comp.VacantSince = null;
            return;
        }

        switch (comp.State)
        {
            case PulsarState.Active:
                // The shared AI system renames the core after whatever gets inserted; keep our name while we run it.
                var name = Loc.GetString(comp.NameLoc);
                if (MetaData(ent).EntityName != name)
                    _meta.SetEntityName(ent, name);
                return;
            case PulsarState.Starting:
            case PulsarState.Disabled:
                Activate(ent, now);
                return;
            case PulsarState.Suspended:
                comp.State = PulsarState.Standby;
                comp.VacantSince = now;
                return;
            case PulsarState.Standby:
                comp.VacantSince ??= now;
                if (now - comp.VacantSince.Value >= comp.VacancyGrace)
                    Activate(ent, now);
                return;
        }
    }

    private void Activate(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        comp.State = PulsarState.Active;
        comp.VacantSince = null;
        comp.NextStationRefresh = now;
        comp.NextScan = now;
        comp.NextMobRefresh = now;
        comp.NextThink = now + comp.ThinkInterval;

        _meta.SetEntityName(ent, Loc.GetString(comp.NameLoc));

        if (!HasComp<ActiveRadioComponent>(ent))
        {
            AddComp<ActiveRadioComponent>(ent);
            comp.AddedRadio = true;
        }

        RefreshStation(ent);
        CheckLaws(ent, now, force: true);
        comp.LastAlertLevel = CurrentAlertLevel(ent);
        Greet(ent, now);

        var ev = new PulsarActivatedEvent();
        RaiseLocalEvent(ent, ref ev);
    }

    /// <summary>
    /// Stage 2.1. First activation in the round: the activation greeting. Activation after a player held the AI:
    /// the reactivation greeting. Re-enabling (cvar / VV) with no player in between: nothing.
    /// Stage 4: said as a station announcement signed by the module (see <see cref="PulsarAnnounce"/>).
    /// </summary>
    private void Greet(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        PulsarGreetingKind kind;
        if (!comp.EverActivated)
            kind = comp.PlayerHeldSinceActive ? PulsarGreetingKind.Reactivation : PulsarGreetingKind.Activation;
        else if (comp.PlayerHeldSinceActive)
            kind = PulsarGreetingKind.Reactivation;
        else
            return;

        comp.EverActivated = true;
        comp.PlayerHeldSinceActive = false;

        var text = Loc.GetString(kind == PulsarGreetingKind.Activation ? comp.GreetingActivation : comp.GreetingReactivation);
        PulsarAnnounce(ent, text, kind == PulsarGreetingKind.Activation ? "greeting-activation" : "greeting-reactivation");
        comp.Greetings.Add(new PulsarGreeting(now, kind, text, null));
    }

    private void Deactivate(Entity<PulsarAutonomousAiComponent> ent, PulsarState newState, bool raise = true)
    {
        var comp = ent.Comp;
        comp.State = newState;

        // Give the core back its normal name: the held AI's name, or the prototype name when empty
        // (the same thing the shared station AI system does on insert/remove).
        if (_stationAi.TryGetHeld((ent.Owner, (StationAiCoreComponent?) null), out var held))
            _meta.SetEntityName(ent, MetaData(held.Value).EntityName);
        else
            _meta.SetEntityName(ent, Prototype(ent)?.Name ?? string.Empty);

        if (comp.AddedRadio)
        {
            RemCompDeferred<ActiveRadioComponent>(ent);
            comp.AddedRadio = false;
        }

        // Conversations end with the handoff; a player AI answers from now on.
        comp.Incoming.Clear();
        comp.PendingArmory.Clear();
        comp.Conversations.Clear();
        comp.DeferredReplies.Clear();
        comp.Clarifications.Clear();
        comp.RecentPoints.Clear();
        comp.RecentUtterances.Clear();
        comp.NextChat.Clear();

        // Automatic bolts are Pulsar's own business: lifted when it hands over. Requested ones stay.
        foreach (var (door, bolt) in comp.Bolts.ToArray())
        {
            if (bolt.Auto || bolt.Pending)
                UnboltDoor(ent, door);
        }
        comp.Bolts.Clear();
        comp.MobAlertStates.Clear();

        // Memory stays (it is "in its head"), but nothing is in view while it is switched off.
        foreach (var grid in comp.Grids.Values)
            grid.ClearVisible();
        foreach (var mob in comp.Mobs.Values)
            mob.InView = false;

        if (!raise)
            return;

        var ev = new PulsarDeactivatedEvent(newState);
        RaiseLocalEvent(ent, ref ev);
    }

    /// <summary>Station grids, telecom channels, holopads.</summary>
    private void RefreshStation(Entity<PulsarAutonomousAiComponent> ent)
    {
        var comp = ent.Comp;
        comp.StationGrids.Clear();

        if (_station.GetOwningStation(ent) is { } station && TryComp<StationDataComponent>(station, out var data))
        {
            foreach (var grid in data.Grids)
                comp.StationGrids.Add(grid);
        }
        else if (Transform(ent).GridUid is { } ownGrid)
        {
            // Not part of a station (tests, admin-built cores): watch the grid it stands on.
            comp.StationGrids.Add(ownGrid);
        }

        foreach (var grid in comp.StationGrids)
        {
            if (!comp.Grids.ContainsKey(grid))
                comp.Grids[grid] = new PulsarGridMemory();
        }

        var stale = new List<EntityUid>();
        foreach (var grid in comp.Grids.Keys)
        {
            if (!comp.StationGrids.Contains(grid) || TerminatingOrDeleted(grid))
                stale.Add(grid);
        }

        foreach (var grid in stale)
            comp.Grids.Remove(grid);

        RefreshChannels(ent);
        RefreshHolopads(ent);
        RefreshBeacons(ent);
    }

    // ---- public API for later stages, commands and tests ----

    /// <summary>Per-core admin switch (the global one is the pulsar.enabled cvar).</summary>
    public void SetEnabled(Entity<PulsarAutonomousAiComponent> ent, bool enabled)
    {
        ent.Comp.Enabled = enabled;
        ent.Comp.NextStateCheck = _timing.CurTime;
    }

    public void SetVacancyGrace(Entity<PulsarAutonomousAiComponent> ent, TimeSpan grace)
    {
        ent.Comp.VacancyGrace = grace;
    }

    /// <summary>Re-reads station grids, channels and holopads and restarts the vision sweep right away.</summary>
    public void RefreshNow(Entity<PulsarAutonomousAiComponent> ent)
    {
        var now = _timing.CurTime;
        ent.Comp.NextStateCheck = now;
        ent.Comp.NextStationRefresh = now;
        ent.Comp.NextScan = now;
        ent.Comp.NextMobRefresh = now;
    }

    public PulsarTileKind GetKnownTile(Entity<PulsarAutonomousAiComponent> ent, EntityUid grid, Vector2i tile)
    {
        return ent.Comp.Grids.TryGetValue(grid, out var mem) ? mem.GetTile(tile) : PulsarTileKind.Unknown;
    }

    public bool IsTileInView(Entity<PulsarAutonomousAiComponent> ent, EntityUid grid, Vector2i tile)
    {
        return ent.Comp.Grids.TryGetValue(grid, out var mem) && mem.IsSeen(tile);
    }

    public bool TryGetDoor(Entity<PulsarAutonomousAiComponent> ent, EntityUid grid, Vector2i tile, out PulsarDoorInfo? door)
    {
        door = null;
        return ent.Comp.Grids.TryGetValue(grid, out var mem) && mem.Doors.TryGetValue(tile, out door);
    }
}
