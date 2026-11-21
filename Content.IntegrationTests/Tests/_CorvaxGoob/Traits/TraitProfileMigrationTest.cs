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
public sealed class TraitProfileMigrationTest
{
    [Test]
    public async Task OldIdsAreMappedAndUnknownDropped()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var ent = server.ResolveDependency<IEntityManager>();
            var profile = HumanoidCharacterProfile.DefaultWithSpecies();

            Assert.That(cfg.GetCVar(CCVars.GameTraitsEnabled), Is.True);
            Assert.That(TraitPreferences.MapId("Nearsighted"), Is.EqualTo("PoorVision"));
            Assert.That(TraitPreferences.MapId("Southern"), Is.EqualTo("SouthernAccent"));
            Assert.That(TraitPreferences.MapId("Stutter"), Is.EqualTo("SocialAnxiety"));
            Assert.That(TraitPreferences.MapId("british"), Is.EqualTo("BritishAccent"));
            Assert.That(TraitPreferences.MapId("British"), Is.EqualTo("BritishAccent"));
            Assert.That(TraitPreferences.MapId("SocialAnxietyTrait"), Is.EqualTo("SocialAnxiety"));

            Assert.That(proto.HasIndex<TraitPrototype>("PoorVision"));
            Assert.That(proto.HasIndex<TraitPrototype>("SouthernAccent"));
            Assert.That(proto.HasIndex<TraitPrototype>("SocialAnxiety"));
            Assert.That(proto.HasIndex<TraitPrototype>("BritishAccent"));

            // Vision + one accent: these do not exclude each other.
            var trimmed = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "Nearsighted", "Southern", "ThisTraitDoesNotExist" },
                profile, proto, cfg, ent);

            Assert.That(trimmed, Does.Contain((ProtoId<TraitPrototype>) "PoorVision"),
                "trimmed=" + string.Join(",", trimmed.Select(x => x.ToString())));
            Assert.That(trimmed, Does.Contain((ProtoId<TraitPrototype>) "SouthernAccent"));
            Assert.That(trimmed.Select(t => t.ToString()), Does.Not.Contain("ThisTraitDoesNotExist"));
            Assert.That(trimmed.Select(t => t.ToString()), Does.Not.Contain("Nearsighted"));
            Assert.That(trimmed.Select(t => t.ToString()), Does.Not.Contain("Southern"));

            foreach (var (oldId, newId) in new[]
                     {
                         ("Stutter", "SocialAnxiety"),
                         ("british", "BritishAccent"),
                     })
            {
                var one = TraitPreferences.MapAndTrim(
                    new ProtoId<TraitPrototype>[] { oldId },
                    profile, proto, cfg, ent);
                Assert.That(one, Does.Contain((ProtoId<TraitPrototype>) newId),
                    $"{oldId} -> trimmed=" + string.Join(",", one.Select(x => x.ToString())));
            }

            // Mutually exclusive accents in one old profile are silently trimmed to a valid subset.
            var exclusive = TraitPreferences.MapAndTrim(
                new ProtoId<TraitPrototype>[] { "Southern", "Stutter", "british" },
                profile, proto, cfg, ent);
            var exclusiveIds = exclusive.Select(t => t.ToString()).ToHashSet();
            Assert.That(exclusiveIds.IsSubsetOf(new[] { "SouthernAccent", "SocialAnxiety", "BritishAccent" }));
            Assert.That(exclusiveIds.Count, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }
}
