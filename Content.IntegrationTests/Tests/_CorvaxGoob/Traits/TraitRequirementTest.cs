// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Traits;

[TestFixture]
public sealed class TraitRequirementTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: trait
  id: TestTraitHumanOnly
  points: 0
  requirements:
    - !type:CharacterSpeciesRequirement
      species:
        - Human

- type: trait
  id: TestTraitPassengerOnly
  points: 0
  requirements:
    - !type:CharacterJobRequirement
      jobs:
        - Passenger

- type: trait
  id: TestTraitExcludesPacifist
  points: 0
  requirements:
    - !type:CharacterTraitRequirement
      inverted: true
      traits:
        - Pacifist
";

    [Test]
    public async Task SpeciesRequirementDropsInvalid()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var ent = server.ResolveDependency<IEntityManager>();

            var human = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            var kept = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "TestTraitHumanOnly" }, human, proto, cfg, ent);
            Assert.That(kept, Does.Contain((ProtoId<TraitPrototype>) "TestTraitHumanOnly"));

            var reptile = HumanoidCharacterProfile.DefaultWithSpecies("Reptilian");
            var dropped = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "TestTraitHumanOnly" }, reptile, proto, cfg, ent);
            Assert.That(dropped, Does.Not.Contain((ProtoId<TraitPrototype>) "TestTraitHumanOnly"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task JobRequirementDropsWhenJobIsSet()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var ent = server.ResolveDependency<IEntityManager>();
            var profile = HumanoidCharacterProfile.DefaultWithSpecies();

            var passenger = proto.Index<JobPrototype>("Passenger");
            var kept = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "TestTraitPassengerOnly" }, profile, proto, cfg, ent, passenger);
            Assert.That(kept, Does.Contain((ProtoId<TraitPrototype>) "TestTraitPassengerOnly"));

            var mime = proto.Index<JobPrototype>("Mime");
            var dropped = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "TestTraitPassengerOnly" }, profile, proto, cfg, ent, mime);
            Assert.That(dropped, Does.Not.Contain((ProtoId<TraitPrototype>) "TestTraitPassengerOnly"));

            // No job selected: job requirements pass so the lobby can show the trait.
            var lobby = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "TestTraitPassengerOnly" }, profile, proto, cfg, ent, job: null);
            Assert.That(lobby, Does.Contain((ProtoId<TraitPrototype>) "TestTraitPassengerOnly"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraitExclusionDropsTheInvalidPair()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var ent = server.ResolveDependency<IEntityManager>();
            var profile = HumanoidCharacterProfile.DefaultWithSpecies()
                .WithTraitPreferences(new ProtoId<TraitPrototype>[] { "Pacifist", "TestTraitExcludesPacifist" });

            // MapAndTrim checks requirements against a candidate profile that already contains both,
            // so the exclusive trait should be dropped.
            var trimmed = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "Pacifist", "TestTraitExcludesPacifist" },
                profile, proto, cfg, ent);

            Assert.That(trimmed, Does.Contain((ProtoId<TraitPrototype>) "Pacifist"));
            Assert.That(trimmed, Does.Not.Contain((ProtoId<TraitPrototype>) "TestTraitExcludesPacifist"));
        });
        await pair.CleanReturnAsync();
    }
}
