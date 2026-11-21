using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// Marks a host the disease is driving right now (Рост 1).
/// </summary>
[RegisterComponent]
public sealed partial class ZakatPossessedComponent : Component
{
    [ViewVariables]
    public EntityUid Zakat;
}

/// <summary>
/// «Рост 1»: the disease takes a host's body for a while. The host's own mind waits in a blind eye riding the body
/// and comes back when the time is up, the body dies or the disease is gone.
/// </summary>
public sealed partial class ZakatPossessionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;

    public static readonly EntProtoId VictimEye = "ZakatVictimEye";

    private readonly List<EntityUid> _ending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatComponent, ZakatPossessActionEvent>(OnPossessAction);
        SubscribeLocalEvent<ZakatPossessedComponent, MobStateChangedEvent>(OnPossessedState);
        SubscribeLocalEvent<ZakatPossessedComponent, EntityTerminatingEvent>(OnPossessedTerminating);
    }

    private void OnPossessAction(Entity<ZakatComponent> ent, ref ZakatPossessActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryPossess(ent, args.Target);
    }

    public bool TryPossess(Entity<ZakatComponent> ent, EntityUid target)
    {
        if (ent.Comp.Growth < ZakatGrowth.PossessLevel || ent.Comp.PossessedHost != null)
            return false;

        if (!TryComp<ZakatHostComponent>(target, out var host) || host.Zakat != ent.Owner
            || !_mobState.IsAlive(target) || !HasComp<MindContainerComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("zakat-host-not-infected"), ent, ent);
            return false;
        }

        if (!_mind.TryGetMind(ent, out var zakatMind, out var zakatMindComp))
            return false;

        // the host's own mind is put aside, riding the body and seeing what is done with it
        if (_mind.TryGetMind(target, out var victimMind, out var victimMindComp))
        {
            var stash = Spawn(VictimEye, Transform(target).Coordinates);
            _transform.SetParent(stash, target);
            _mind.TransferTo(victimMind, stash, mind: victimMindComp);
            ent.Comp.VictimStash = stash;
            ent.Comp.VictimMind = victimMind;
            _popup.PopupEntity(Loc.GetString("zakat-possess-victim"), stash, stash, PopupType.LargeCaution);
        }

        _mind.TransferTo(zakatMind, target, mind: zakatMindComp);
        ent.Comp.PossessedHost = target;
        ent.Comp.PossessEnd = _timing.CurTime + ent.Comp.PossessDuration;
        EnsureComp<ZakatPossessedComponent>(target).Zakat = ent.Owner;
        EnsureComp<ZakatBleedingEyesComponent>(target);
        _popup.PopupEntity(Loc.GetString("zakat-possess-start"), target, target, PopupType.Medium);
        return true;
    }

    /// <summary>
    /// Gives the body back. Safe to call at any time.
    /// </summary>
    public void EndPossession(Entity<ZakatComponent> ent)
    {
        if (ent.Comp.PossessedHost is not { } host)
            return;

        ent.Comp.PossessedHost = null;
        var hostAlive = !TerminatingOrDeleted(host);

        // the disease goes home first, so the body is free for its owner
        if (hostAlive && _mind.TryGetMind(host, out var zakatMind, out var zakatMindComp))
        {
            if (!TerminatingOrDeleted(ent))
                _mind.TransferTo(zakatMind, ent, mind: zakatMindComp);
            else
                _mind.TransferTo(zakatMind, null, mind: zakatMindComp);
        }

        if (ent.Comp.VictimMind is { } victimMind && !TerminatingOrDeleted(victimMind))
        {
            if (hostAlive)
                _mind.TransferTo(victimMind, host);
            else
                _mind.TransferTo(victimMind, null);
        }

        if (ent.Comp.VictimStash is { } stash && !TerminatingOrDeleted(stash))
            QueueDel(stash);

        ent.Comp.VictimMind = null;
        ent.Comp.VictimStash = null;

        if (hostAlive)
        {
            RemComp<ZakatPossessedComponent>(host);
            RemComp<ZakatBleedingEyesComponent>(host);
        }
    }

    private void OnPossessedState(Entity<ZakatPossessedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            _ending.Add(ent.Comp.Zakat);
    }

    private void OnPossessedTerminating(Entity<ZakatPossessedComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakat) && zakat.PossessedHost == ent.Owner)
            EndPossession((ent.Comp.Zakat, zakat));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ZakatComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.PossessedHost != null && now >= comp.PossessEnd)
                _ending.Add(uid);
        }

        foreach (var uid in _ending)
        {
            if (TryComp<ZakatComponent>(uid, out var comp))
                EndPossession((uid, comp));
        }

        _ending.Clear();
    }
}
