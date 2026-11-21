// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Traits;

[TestFixture]
public sealed class TraitBudgetTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: trait
  id: TestTraitRefund
  points: 3
  slots: 1

- type: trait
  id: TestTraitCostly
  points: -6
  slots: 1

- type: trait
  id: TestTraitCostly2
  points: -6
  slots: 1

- type: trait
  id: TestTraitFree
  points: 0
  slots: 1

- type: trait
  id: TestTraitSlotty
  points: 0
  slots: 5
";

    [Test]
    public async Task OverBudgetIsSilentlyTrimmed()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var ent = server.ResolveDependency<IEntityManager>();
            cfg.SetCVar(CCVars.GameTraitsDefaultPoints, 10);
            cfg.SetCVar(CCVars.GameTraitsMax, 14);

            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            // 10 + (-6) + (-6) = -2, over budget. One costly trait must be dropped.
            var trimmed = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "TestTraitCostly", "TestTraitCostly2" },
                profile, proto, cfg, ent);

            Assert.That(trimmed.Count, Is.EqualTo(1), "over-budget set is silently trimmed to one trait");
            Assert.That(TraitPreferences.PointsRemaining(trimmed, proto, cfg), Is.GreaterThanOrEqualTo(0));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OverSlotsIsSilentlyTrimmed()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var ent = server.ResolveDependency<IEntityManager>();
            cfg.SetCVar(CCVars.GameTraitsDefaultPoints, 10);
            cfg.SetCVar(CCVars.GameTraitsMax, 14);

            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            var trimmed = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "TestTraitSlotty", "TestTraitSlotty", "TestTraitCostly", "TestTraitRefund", "TestTraitFree" },
                profile, proto, cfg, ent);

            Assert.That(TraitPreferences.SlotsRemaining(trimmed, proto, cfg), Is.GreaterThanOrEqualTo(0));
            Assert.That(TraitPreferences.PointsRemaining(trimmed, proto, cfg), Is.GreaterThanOrEqualTo(0));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExactBudgetIsKept()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var ent = server.ResolveDependency<IEntityManager>();
            cfg.SetCVar(CCVars.GameTraitsDefaultPoints, 10);
            cfg.SetCVar(CCVars.GameTraitsMax, 14);

            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            // 10 + 3 + (-6) + 0 = 7, 3 slots
            var set = new ProtoId<TraitPrototype>[] { "TestTraitRefund", "TestTraitCostly", "TestTraitFree" };
            var trimmed = TraitPreferences.MapAndTrim(set, profile, proto, cfg, ent);
            Assert.That(trimmed.Count, Is.EqualTo(3));
            Assert.That(TraitPreferences.PointsRemaining(trimmed, proto, cfg), Is.EqualTo(7));
        });
        await pair.CleanReturnAsync();
    }
}
