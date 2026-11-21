using System.Linq;
using System.Numerics;
using Content.Goobstation.Maths.FixedPoint;
using Content.Server._CorvaxGoob.Lavaland;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Lavaland;

/// <summary>
/// NPCs on lavaland ruin grids sleep while no player is near, wake when a player comes close or when they are hurt,
/// and go back to sleep after no player has been near for the sleep delay.
/// </summary>
[TestFixture]
public sealed class LavalandRuinNpcSleepTest
{
    private const string Turret = "WeaponTurretSyndicate";

    [Test]
    public async Task SleepWakeOnProximityAndDamage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sleepSys = entMan.System<LavalandRuinNpcSleepSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();

        var map = await pair.CreateTestMap();
        EntityUid ruinNpc = default;
        EntityUid otherNpc = default;
        EntityUid player = default;

        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<LavalandRuinNpcSleepGridComponent>(map.Grid);
            ruinNpc = entMan.SpawnEntity(Turret, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            // Same map, but not on a ruin grid: never touched.
            otherNpc = entMan.SpawnEntity(Turret, new MapCoordinates(200, 200, map.MapId));
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(ruinNpc), "NPC should be awake after MapInit");
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(otherNpc));
        });

        // Regular update loop: nobody around, the ruin NPC goes to sleep within one check interval.
        var ticks = (int) Math.Ceiling(LavalandRuinNpcSleepSystem.UpdateInterval.TotalSeconds * 2 / timing.TickPeriod.TotalSeconds) + 2;
        await server.WaitRunTicks(ticks);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(ruinNpc), Is.False, "ruin NPC should sleep with no player near");
            Assert.That(entMan.HasComponent<LavalandRuinNpcSleepComponent>(ruinNpc));
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(otherNpc), "NPC outside ruin grids must stay awake");
            Assert.That(entMan.HasComponent<LavalandRuinNpcSleepComponent>(otherNpc), Is.False);
        });

        // A player-controlled living mob far away does not wake it, within range it does.
        await server.WaitPost(() =>
        {
            player = entMan.SpawnEntity(null, new MapCoordinates(100, 0, map.MapId));
            entMan.EnsureComponent<MobStateComponent>(player);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.First(), player);
            sleepSys.RunCheck(timing.CurTime);
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(ruinNpc), Is.False, "player 100 tiles away should not wake it");

            xformSys.SetCoordinates(player, new EntityCoordinates(map.Grid, 10f, 0f));
            sleepSys.RunCheck(timing.CurTime);
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(ruinNpc), "player within range should wake it");

            // Player leaves: stays awake until the sleep delay passes without a player near.
            xformSys.SetCoordinates(player, new EntityCoordinates(map.Grid, 100f, 0f));
            sleepSys.RunCheck(timing.CurTime + TimeSpan.FromSeconds(10));
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(ruinNpc), "should stay awake during the sleep delay");
            sleepSys.RunCheck(timing.CurTime + TimeSpan.FromSeconds(61));
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(ruinNpc), Is.False, "should sleep again after the delay");

            // Taking damage wakes it even with no player near.
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");
            entMan.System<DamageableSystem>().TryChangeDamage(ruinNpc, new DamageSpecifier(blunt, FixedPoint2.New(5)), true);
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(ruinNpc), "damage should wake it");
        });

        await pair.CleanReturnAsync();
    }
}
