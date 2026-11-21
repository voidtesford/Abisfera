// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.Lavaland;

/// <summary>
/// Keeps NPCs of lavaland ruins (humanoids, fauna, turrets - anything with HTN) asleep while no player is nearby.
/// NPCs wake up when a living player-controlled mob comes within range or when they take damage,
/// and go back to sleep after no player has been near for a while.
/// </summary>
public sealed class LavalandRuinNpcSleepSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    /// How often NPCs are checked.
    /// </summary>
    public static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1.5);

    private TimeSpan _nextUpdate;

    private readonly Dictionary<MapId, List<Vector2>> _players = new();
    private readonly List<(EntityUid Uid, LavalandRuinNpcSleepGridComponent Grid)> _toTrack = new();
    private readonly List<EntityUid> _toSleep = new();
    private readonly List<EntityUid> _toWake = new();

    private EntityQuery<ActiveNPCComponent> _activeQuery;
    private EntityQuery<LavalandRuinNpcSleepGridComponent> _gridQuery;
    private EntityQuery<LavalandRuinNpcSleepComponent> _sleepQuery;

    public override void Initialize()
    {
        base.Initialize();

        _activeQuery = GetEntityQuery<ActiveNPCComponent>();
        _gridQuery = GetEntityQuery<LavalandRuinNpcSleepGridComponent>();
        _sleepQuery = GetEntityQuery<LavalandRuinNpcSleepComponent>();

        SubscribeLocalEvent<LavalandRuinNpcSleepComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<LavalandRuinNpcSleepComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        ent.Comp.LastActivity = _timing.CurTime;

        if (ent.Comp.SleptByUs && CanWake(ent))
            Wake(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;
        RunCheck(now);
    }

    /// <summary>
    /// Runs one sleep/wake pass right away. Public for tests.
    /// </summary>
    public void RunCheck(TimeSpan now)
    {
        CollectPlayers();
        TrackNewNpcs(now);

        _toSleep.Clear();
        _toWake.Clear();

        var query = EntityQueryEnumerator<LavalandRuinNpcSleepComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out _, out var xform))
        {
            var awake = _activeQuery.HasComp(uid);

            // Something else woke it (revived, admin, ...): it's no longer ours to wake.
            if (awake && comp.SleptByUs)
                comp.SleptByUs = false;

            if (IsPlayerNear(uid, xform, comp.WakeRange))
            {
                comp.LastActivity = now;
                if (!awake && comp.SleptByUs)
                    _toWake.Add(uid);

                continue;
            }

            if (awake && now - comp.LastActivity >= comp.SleepDelay)
                _toSleep.Add(uid);
        }

        foreach (var uid in _toWake)
        {
            var comp = _sleepQuery.Comp(uid);
            if (CanWake(uid))
                Wake((uid, comp));
        }

        foreach (var uid in _toSleep)
        {
            // Never touch NPCs controlled by a player or a mind.
            if (HasComp<ActorComponent>(uid)
                || TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
                continue;

            _npc.SleepNPC(uid);
            _sleepQuery.Comp(uid).SleptByUs = true;
        }
    }

    private void TrackNewNpcs(TimeSpan now)
    {
        _toTrack.Clear();

        var query = EntityQueryEnumerator<ActiveNPCComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (_sleepQuery.HasComp(uid)
                || xform.GridUid is not { } grid
                || !_gridQuery.TryComp(grid, out var gridComp))
                continue;

            _toTrack.Add((uid, gridComp));
        }

        foreach (var (uid, gridComp) in _toTrack)
        {
            var comp = AddComp<LavalandRuinNpcSleepComponent>(uid);
            comp.WakeRange = gridComp.WakeRange;
            comp.SleepDelay = gridComp.SleepDelay;
            // No grace period: if nobody is near on the first check, the NPC goes to sleep right away.
            comp.LastActivity = now - gridComp.SleepDelay;
        }
    }

    private void CollectPlayers()
    {
        foreach (var list in _players.Values)
        {
            list.Clear();
        }

        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var mobState, out var xform))
        {
            if (xform.MapID == MapId.Nullspace || _mobState.IsDead(uid, mobState))
                continue;

            if (!_players.TryGetValue(xform.MapID, out var list))
            {
                list = new List<Vector2>();
                _players[xform.MapID] = list;
            }

            list.Add(_transform.GetWorldPosition(xform));
        }
    }

    private bool IsPlayerNear(EntityUid uid, TransformComponent xform, float range)
    {
        if (!_players.TryGetValue(xform.MapID, out var list) || list.Count == 0)
            return false;

        var pos = _transform.GetWorldPosition(xform);
        var rangeSquared = range * range;
        foreach (var player in list)
        {
            if ((player - pos).LengthSquared() <= rangeSquared)
                return true;
        }

        return false;
    }

    private bool CanWake(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || _mobState.IsIncapacitated(uid) || HasComp<ActorComponent>(uid))
            return false;

        return !TryComp<MindContainerComponent>(uid, out var mind) || !mind.HasMind;
    }

    private void Wake(Entity<LavalandRuinNpcSleepComponent> ent)
    {
        ent.Comp.SleptByUs = false;
        _npc.WakeNPC(ent);
    }
}
