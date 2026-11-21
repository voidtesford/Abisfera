// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.Damage.Components;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Physics;
using Content.Shared.Preferences;
using Content.Shared.Bed.Sleep;
using Content.Shared.Speech.Muting;
using Content.Shared.Traits;
using Content.Shared.Traits.Assorted.Components;
using Content.Server.Station.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Traits;

[TestFixture]
public sealed class TraitSpawnApplicationTest
{
    [Test]
    public async Task SpawnAppliesRepresentativeFunctions()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var ent = server.ResolveDependency<IEntityManager>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            var station = ent.System<StationSpawningSystem>();

            var traits = new ProtoId<TraitPrototype>[]
            {
                "Pacifist",          // TraitAddComponent
                "Muted",             // AddComponent + ModifyLanguages
                "Snoring",           // AddComponent
            };

            var profile = HumanoidCharacterProfile.DefaultWithSpecies()
                .WithTraitPreferences(traits);

            var mob = station.SpawnPlayerMob(map.GridCoords, job: "Passenger", profile, station: null);

            Assert.That(ent.HasComponent<PacifiedComponent>(mob), Is.True, "Pacifist adds Pacified");
            Assert.That(ent.HasComponent<MutedComponent>(mob), Is.True, "Muted adds Muted");
            Assert.That(ent.HasComponent<SnoringComponent>(mob), Is.True, "Snoring adds Snoring");

            ent.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpawnAppliesDensityAndStaminaFunctions()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var ent = server.ResolveDependency<IEntityManager>();
            var station = ent.System<StationSpawningSystem>();

            var baseline = station.SpawnPlayerMob(
                map.GridCoords, job: "Passenger", HumanoidCharacterProfile.DefaultWithSpecies(), station: null);
            var baseDensity = FirstDensity(ent, baseline);
            var baseStamina = ent.GetComponent<StaminaComponent>(baseline).CritThreshold;
            ent.DeleteEntity(baseline);

            var profile = HumanoidCharacterProfile.DefaultWithSpecies()
                .WithTraitPreferences(new ProtoId<TraitPrototype>[] { "Bodybuilder", "Vigor" });
            var mob = station.SpawnPlayerMob(map.GridCoords, job: "Passenger", profile, station: null);

            var density = FirstDensity(ent, mob);
            Assert.That(density, Is.GreaterThan(baseDensity), "Bodybuilder increases density");

            if (ent.TryGetComponent(mob, out StaminaComponent? stamina))
                Assert.That(stamina.CritThreshold, Is.GreaterThan(baseStamina), "Vigor raises stamina crit threshold");

            ent.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    private static float FirstDensity(IEntityManager ent, EntityUid uid)
    {
        var fixtures = ent.GetComponent<FixturesComponent>(uid);
        return fixtures.Fixtures.First().Value.Density;
    }
}
