using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// Doors of biomass: they part only for the flesh's own (the infected and the hive's creatures) and close behind them.
/// </summary>
public sealed partial class ZakatFleshDoorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private INetManager _net = default!;

    public static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(3);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZakatFleshDoorComponent, BeforeDoorOpenedEvent>(OnBeforeOpen);
        SubscribeLocalEvent<ZakatFleshDoorComponent, DoorStateChangedEvent>(OnStateChanged);
    }

    public static bool CanPass(IEntityManager entMan, EntityUid? user)
    {
        return user is { } u && entMan.HasComponent<ZakatFleshFriendComponent>(u);
    }

    private void OnBeforeOpen(Entity<ZakatFleshDoorComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (!CanPass(EntityManager, args.User))
            args.Cancel();
    }

    private void OnStateChanged(EntityUid uid, ZakatFleshDoorComponent comp, DoorStateChangedEvent args)
    {
        if (args.State == DoorState.Open)
            comp.CloseAt = _timing.CurTime + CloseDelay;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ZakatFleshDoorComponent, DoorComponent>();
        while (query.MoveNext(out var uid, out var flesh, out var door))
        {
            if (door.State != DoorState.Open || flesh.CloseAt is not { } at || now < at)
                continue;

            // someone in the doorway: try again a bit later
            if (!_door.TryClose(uid, door))
                flesh.CloseAt = now + TimeSpan.FromSeconds(1);
            else
                flesh.CloseAt = null;
        }
    }
}
