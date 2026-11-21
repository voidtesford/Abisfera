using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Guns;

/// <summary>
/// AKK: half AKMS spread (hip and wielded), 70 tile/s projectiles, own 7.62x39 ammo (24 Piercing), rejects .30 magazines.
/// </summary>
[TestFixture]
[TestOf(typeof(GunComponent))]
public sealed class AkkRifleTest
{
    [Test]
    public async Task AkkStatsAmmoAndMagazineWhitelist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var slots = server.System<ItemSlotsSystem>();
        var guns = server.System<SharedGunSystem>();
        var map = await pair.CreateTestMap();
        EntityUid gun = default, lightMag = default, akkMag = default;

        await server.WaitAssertion(() =>
        {
            var rifle = protos.Index<EntityPrototype>("WeaponRifleAkk");
            Assert.That(rifle.TryGetComponent<GunComponent>(out var gunProto, factory), Is.True);
            Assert.That(gunProto!.MinAngle.Degrees, Is.EqualTo(10.5).Within(1e-4), "hip: half the AKM's 21");
            Assert.That(gunProto.MaxAngle.Degrees, Is.EqualTo(16.0).Within(1e-4), "hip: half the AKM's 32");
            // fix1: wielded = base + wield bonus = 0.5 / 1 deg (half the AKM's 1 / 2), never negative.
            Assert.That(rifle.TryGetComponent<Content.Shared.Weapons.Ranged.Components.GunWieldBonusComponent>(out var bonus, factory), Is.True);
            Assert.That((gunProto.MinAngle + bonus!.MinAngle).Degrees, Is.EqualTo(0.5).Within(1e-4));
            Assert.That((gunProto.MaxAngle + bonus.MaxAngle).Degrees, Is.EqualTo(1.0).Within(1e-4));
            Assert.That(gunProto.AngleIncrease.Degrees, Is.EqualTo(0.25).Within(1e-4));
            Assert.That(gunProto.AngleDecay.Degrees, Is.EqualTo(4.0).Within(1e-4));
            Assert.That(gunProto.ProjectileSpeed, Is.EqualTo(70f).Within(0.01f));
            Assert.That(gunProto.FireRate, Is.EqualTo(5f).Within(0.01f));

            var bullet = protos.Index<EntityPrototype>("BulletAkk762");
            Assert.That(bullet.TryGetComponent<ProjectileComponent>(out var proj, factory), Is.True);
            Assert.That(proj!.Damage.DamageDict["Piercing"].Float(), Is.EqualTo(24f).Within(0.01f));
            Assert.That(proj.Damage.ArmorPenetration, Is.EqualTo(0.4f).Within(0.001f));

            var cart = protos.Index<EntityPrototype>("CartridgeAkk762");
            Assert.That(cart.TryGetComponent<CartridgeAmmoComponent>(out var cartAmmo, factory), Is.True);
            Assert.That(cartAmmo!.Prototype, Is.EqualTo(new EntProtoId("BulletAkk762")));

            Assert.That(protos.HasIndex<EntityPrototype>("MagazineAkk762"));
            Assert.That(protos.HasIndex<EntityPrototype>("MagazineBoxAkk762"));
            Assert.That(protos.HasIndex<EntityPrototype>("MagazineAkk762Empty"));
        });

        await server.WaitPost(() =>
        {
            gun = entMan.SpawnEntity("WeaponRifleAkk", map.GridCoords);
            lightMag = entMan.SpawnEntity("MagazineLightRifle", map.GridCoords.Offset(new Vector2(1f, 0f)));
            akkMag = entMan.SpawnEntity("MagazineAkk762Empty", map.GridCoords.Offset(new Vector2(2f, 0f)));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var gunComp = entMan.GetComponent<GunComponent>(gun);
            Assert.That(gunComp.MinAngle.Degrees, Is.EqualTo(10.5).Within(1e-4));
            Assert.That(gunComp.ProjectileSpeed, Is.EqualTo(70f).Within(0.01f));

            var chambered = guns.GetChamberEntity(gun);
            Assert.That(chambered, Is.Not.Null, "starts with a 7.62x39 in the chamber");
            Assert.That(entMan.GetComponent<MetaDataComponent>(chambered!.Value).EntityPrototype!.ID, Is.EqualTo("CartridgeAkk762"));
            Assert.That(entMan.GetComponent<CartridgeAmmoComponent>(chambered.Value).Prototype, Is.EqualTo(new EntProtoId("BulletAkk762")));

            Assert.That(slots.TryEject(gun, "gun_magazine", null, out var stockMag), Is.True);
            Assert.That(entMan.GetComponent<MetaDataComponent>(stockMag!.Value).EntityPrototype!.ID, Is.EqualTo("MagazineAkk762"));

            Assert.That(slots.TryInsert(gun, "gun_magazine", lightMag, null), Is.False, "rejects .30 light-rifle magazines");
            Assert.That(slots.TryInsert(gun, "gun_magazine", akkMag, null), Is.True, "accepts its own 7.62x39 magazine");
        });

        await pair.CleanReturnAsync();
    }
}
