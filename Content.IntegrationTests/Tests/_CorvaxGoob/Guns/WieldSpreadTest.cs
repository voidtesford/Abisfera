// Abisfera - s4.4: a two-handed grip must tighten the spread, for trained and untrained shooters alike.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._CorvaxGoob.Skills;
using Content.Shared.CombatMode;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.IntegrationTests.Pair;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Guns;

[TestFixture]
public sealed class WieldSpreadTest
{
    /// <summary>Fires single aimed shots and returns the mean deviation (degrees) from the aim line.</summary>
    private static async Task<double> MeanDeviation(TestPair pair, EntityUid mob, EntityUid gun, EntityCoordinates aim, int shots)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var devs = new List<double>();
        for (var i = 0; i < shots; i++)
        {
            await server.WaitPost(() =>
            {
                var g = entMan.GetComponent<GunComponent>(gun);
                g.NextFire = TimeSpan.Zero;
                if (entMan.TryGetComponent<MeleeWeaponComponent>(gun, out var melee))
                    melee.NextAttack = TimeSpan.Zero;
                Assert.That(server.System<SharedGunSystem>().AttemptShoot(mob, (gun, g), aim), Is.True, "the gun didn't fire");
            });
            await pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                var q = entMan.EntityQueryEnumerator<ProjectileComponent, PhysicsComponent>();
                while (q.MoveNext(out var p, out _, out var phys))
                {
                    if (phys.LinearVelocity.LengthSquared() > 1f)
                        devs.Add(Math.Abs(Angle.ShortestDistance(Angle.Zero, phys.LinearVelocity.ToAngle()).Degrees));
                    entMan.DeleteEntity(p);
                }
            });
            await pair.RunTicksSync(15); // let the recoil decay back to the minimum
        }

        Assert.That(devs, Is.Not.Empty);
        return devs.Average();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WieldingTightensSpread(bool skilled)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default, gun = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            if (skilled)
                entMan.AddComponent<IgnoreSkillsComponent>(mob);
            server.System<SharedCombatModeSystem>().SetInCombatMode(mob, true);
            gun = entMan.SpawnEntity("WeaponRifleAkk", map.GridCoords); // requires the shooting skill
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(mob, gun, checkActionBlocker: false), Is.True);
            entMan.GetComponent<GunComponent>(gun).SelectedMode = SelectiveFire.SemiAuto;
        });
        await pair.RunTicksSync(5);
        var aim = map.GridCoords.Offset(new Vector2(10f, 0f));

        var hip = await MeanDeviation(pair, mob, gun, aim, 12);
        await server.WaitPost(() =>
            Assert.That(server.System<SharedWieldableSystem>().TryWield(gun, entMan.GetComponent<WieldableComponent>(gun), mob), Is.True));
        await pair.RunTicksSync(5);

        // The wielded spread reaches the client too (prediction uses the same values).
        await server.WaitAssertion(() =>
        {
            var g = entMan.GetComponent<GunComponent>(gun);
            var cg = pair.Client.EntMan.GetComponent<GunComponent>(pair.ToClientUid(gun));
            Assert.That(g.MaxAngleModified.Degrees, Is.EqualTo(1.0).Within(1e-3));
            Assert.That(g.MinAngleModified.Degrees, Is.EqualTo(0.5).Within(1e-3), "AKK wielded spread");
            Assert.That(cg.MaxAngleModified.Degrees, Is.EqualTo(g.MaxAngleModified.Degrees).Within(1e-3), "client didn't get the wielded spread");
            Assert.That(cg.MinAngleModified.Degrees, Is.EqualTo(g.MinAngleModified.Degrees).Within(1e-3));
        });

        var wielded = await MeanDeviation(pair, mob, gun, aim, 12);
        TestContext.Out.WriteLine($"skilled {skilled}: hip {hip:0.00}°, wielded {wielded:0.00}°");
        Assert.That(wielded, Is.LessThan(hip * 0.6), "wielding barely changed the spread");

        await server.WaitPost(() => server.System<SharedWieldableSystem>().TryUnwield(gun, entMan.GetComponent<WieldableComponent>(gun), mob));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var g = entMan.GetComponent<GunComponent>(gun);
            Assert.That(g.MinAngleModified.Degrees, Is.EqualTo(10.5).Within(1e-3), "the wield bonus stayed after unwielding");
            Assert.That(g.MaxAngleModified.Degrees, Is.EqualTo(16.0).Within(1e-3));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnskilledFactorAndNoNegativeSpread()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var gun = new GunComponent { MaxAngle = Angle.FromDegrees(32), MaxAngleModified = Angle.FromDegrees(32) };
            Assert.That(SharedGunSystem.GetUnskilledSpreadFactor(gun), Is.EqualTo(1f).Within(1e-4));
            gun.MaxAngleModified = Angle.FromDegrees(16);
            Assert.That(SharedGunSystem.GetUnskilledSpreadFactor(gun), Is.EqualTo(0.5f).Within(1e-4));
            gun.MaxAngleModified = Angle.FromDegrees(2);
            Assert.That(SharedGunSystem.GetUnskilledSpreadFactor(gun), Is.EqualTo(SharedGunSystem.MinUnskilledSpreadFactor).Within(1e-4));
        });

        // M7S: 15 / 55 base with a -20 / -30 wield bonus used to go negative when wielded.
        EntityUid mob = default, m7s = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            m7s = entMan.SpawnEntity("WeaponSubMachineGunM7S", map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(mob, m7s, checkActionBlocker: false), Is.True);
        });
        await server.WaitRunTicks(3);
        await server.WaitPost(() =>
            Assert.That(server.System<SharedWieldableSystem>().TryWield(m7s, entMan.GetComponent<WieldableComponent>(m7s), mob), Is.True));
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() =>
        {
            var g = entMan.GetComponent<GunComponent>(m7s);
            Assert.That(g.MinAngleModified.Degrees, Is.GreaterThanOrEqualTo(0.0), "wielded spread went negative");
            Assert.That(g.MaxAngleModified.Degrees, Is.EqualTo(25.0).Within(1e-3));
        });
        await pair.CleanReturnAsync();
    }
}
