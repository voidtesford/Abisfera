using System.Collections.Generic;
using Content.Goobstation.Client._CorvaxGoob.Zakat.UI;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Content.Shared._Starlight.CollectiveMind;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// Both neural maps: the core «Рост» chain and the per-host evolution tree
/// (sequential unlocks along each branch, spec costs, bought for ONE host only).
/// </summary>
public sealed class ZakatShopTest : ZakatTestBase
{
    [Test]
    public async Task HostTreeOrderAndCosts()
    {
        EntityUid host1 = default, host2 = default;
        await Server.WaitPost(() =>
        {
            host1 = Spawn("MobHuman");
            host2 = Spawn("MobHuman", 2f);
            MakeHost(host1);
            MakeHost(host2);
        });

        await Server.WaitAssertion(() =>
        {
            var z = Zakat;
            // spec table
            var expected = new (ZakatSymptom S, float Cost)[]
            {
                (ZakatSymptom.Sneeze, 5), (ZakatSymptom.Cough, 10), (ZakatSymptom.Pneumonia, 15), (ZakatSymptom.LungFibrosis, 20), (ZakatSymptom.PulmonaryEdema, 25),
                (ZakatSymptom.Nausea, 5), (ZakatSymptom.ImmuneSuppression, 10), (ZakatSymptom.Sweating, 15), (ZakatSymptom.Dysentery, 20), (ZakatSymptom.BloodyVomit, 25),
                (ZakatSymptom.Rash, 5), (ZakatSymptom.SkinLesions, 10), (ZakatSymptom.Psychosis, 15), (ZakatSymptom.Epilepsy, 20),
                (ZakatSymptom.Tachycardia, 5), (ZakatSymptom.Hemophilia, 10), (ZakatSymptom.RotBlood, 15), (ZakatSymptom.OrganDecay, 20),
            };
            Assert.That(ZakatSymptoms.Branches, Has.Length.EqualTo(4));
            Assert.That(ZakatSymptoms.Get(ZakatSymptom.Tachycardia).Branch, Is.EqualTo(3));
            foreach (var (s, cost) in expected)
                Assert.That(ZakatSymptoms.Get(s).Cost, Is.EqualTo(cost), s.ToString());

            z.Comp.Biomass = 1000f;

            // out of order is refused and costs nothing
            Assert.That(ZakatSys.TryBuySymptom(z, host1, ZakatSymptom.Cough), Is.False);
            Assert.That(ZakatSys.TryBuySymptom(z, host1, ZakatSymptom.Epilepsy), Is.False);
            Assert.That(z.Comp.Biomass, Is.EqualTo(1000f));

            // in order, each at its own price
            var spent = 0f;
            foreach (var branch in ZakatSymptoms.Branches)
            {
                foreach (var s in branch)
                {
                    var before = z.Comp.Biomass;
                    Assert.That(ZakatSys.TryBuySymptom(z, host1, s), Is.True, s.ToString());
                    Assert.That(before - z.Comp.Biomass, Is.EqualTo(ZakatSymptoms.Get(s).Cost), s.ToString());
                    spent += ZakatSymptoms.Get(s).Cost;
                }
            }

            Assert.That(spent, Is.EqualTo(5 + 10 + 15 + 20 + 25 + 5 + 10 + 15 + 20 + 25 + 5 + 10 + 15 + 20 + 5 + 10 + 15 + 20));
            Assert.That(ZakatSys.TryBuySymptom(z, host2, ZakatSymptom.Hemophilia), Is.False, "organs are sequential too");
            // twice is refused
            Assert.That(ZakatSys.TryBuySymptom(z, host1, ZakatSymptom.Sneeze), Is.False);

            // per host: host2 has nothing and starts at the root of each branch
            var h2 = SEntMan.GetComponent<ZakatHostComponent>(host2);
            Assert.That(h2.Symptoms, Is.Empty);
            Assert.That(ZakatSys.TryBuySymptom(z, host2, ZakatSymptom.Cough), Is.False);
            Assert.That(ZakatSys.TryBuySymptom(z, host2, ZakatSymptom.Sneeze), Is.True);
            Assert.That(h2.Symptoms, Is.EquivalentTo(new[] { ZakatSymptom.Sneeze }));

            // not enough biomass
            z.Comp.Biomass = 9f;
            Assert.That(ZakatSys.TryBuySymptom(z, host2, ZakatSymptom.Cough), Is.False);
            Assert.That(z.Comp.Biomass, Is.EqualTo(9f));
            z.Comp.Biomass = 10f;
            Assert.That(ZakatSys.TryBuySymptom(z, host2, ZakatSymptom.Cough), Is.True);
            Assert.That(z.Comp.Biomass, Is.Zero);
        });
    }

    /// <summary>
    /// Living infected hosts, spread around the test tile; UpdateZakat recounts them.
    /// </summary>
    private List<EntityUid> SpawnHosts(int count)
    {
        var list = new List<EntityUid>();
        for (var i = 0; i < count; i++)
        {
            var host = Spawn("MobHuman", (i % 4) * 0.6f, (i / 4) * 0.6f);
            MakeHost(host);
            list.Add(host);
        }

        ZakatSys.UpdateZakat(Zakat);
        return list;
    }

    [Test]
    public async Task GrowthShop()
    {
        await Server.WaitAssertion(() =>
        {
            var z = Zakat;
            Assert.That(ZakatGrowth.GrowthCosts, Is.EqualTo(new[] { 25f, 50f, 75f, 1000f }));
            Assert.That(ZakatGrowth.RequiredHosts(1), Is.Zero);
            Assert.That(ZakatGrowth.RequiredHosts(2), Is.EqualTo(5));
            Assert.That(ZakatGrowth.RequiredHosts(3), Is.EqualTo(10));
            Assert.That(z.Comp.Growth, Is.Zero);
            Assert.That(z.Comp.PossessActionEntity, Is.Null);

            z.Comp.Biomass = 24f;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.False);
            Assert.That(z.Comp.Biomass, Is.EqualTo(24f));

            // Рост 1: 25, grants taking control of a host (no hosts needed)
            z.Comp.Biomass = 25f;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.True);
            Assert.That(z.Comp.Growth, Is.EqualTo(1));
            Assert.That(z.Comp.Biomass, Is.Zero);
            Assert.That(z.Comp.PossessActionEntity, Is.Not.Null);
            Assert.That(SEntMan.HasComponent<CollectiveMindComponent>(SPlayer), Is.False, "the hive mind is Рост 2");

            // Рост 2: 50 and at least 5 living infected hosts
            var hosts = SpawnHosts(4);
            Assert.That(z.Comp.LivingHosts, Is.EqualTo(4));
            z.Comp.Biomass = 60f;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.False, "4 hosts are not enough");
            Assert.That(z.Comp.Biomass, Is.EqualTo(60f));
            Assert.That(z.Comp.Growth, Is.EqualTo(1));

            hosts.AddRange(SpawnHosts(1));
            Assert.That(z.Comp.LivingHosts, Is.EqualTo(5));
            z.Comp.Biomass = 60f; // every new host also pays biomass
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.True);
            Assert.That(z.Comp.Growth, Is.EqualTo(2));
            Assert.That(z.Comp.Biomass, Is.EqualTo(10f));
            var mind = SEntMan.GetComponent<CollectiveMindComponent>(SPlayer);
            Assert.That(mind.Channels, Does.Contain(new ProtoId<CollectiveMindPrototype>(z.Comp.HiveMind)));
            Assert.That(mind.Minds.ContainsKey(z.Comp.HiveMind), Is.True);

            // Рост 3: 75 and at least 10 living infected hosts
            Assert.That(z.Comp.BirthActionEntity, Is.Null);
            z.Comp.Biomass = 1000f;
            hosts.AddRange(SpawnHosts(5));
            Assert.That(z.Comp.LivingHosts, Is.EqualTo(10));

            // the dead and the flesh's own creatures don't count
            SEntMan.System<MobStateSystem>().ChangeMobState(hosts[0], MobState.Dead);
            var hunter = Spawn("MobZakatHunter");
            SEntMan.System<ZakatFleshSystem>().SetupCreature(hunter, z);
            ZakatSys.UpdateZakat(z);
            Assert.That(z.Comp.LivingHosts, Is.EqualTo(9));
            z.Comp.Biomass = 1000f;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.False, "9 living hosts + a creature are not enough");
            Assert.That(z.Comp.Biomass, Is.EqualTo(1000f));

            hosts.AddRange(SpawnHosts(1));
            Assert.That(z.Comp.LivingHosts, Is.EqualTo(10));
            z.Comp.Biomass = 1000f;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.True);
            Assert.That(z.Comp.Growth, Is.EqualTo(3));
            Assert.That(z.Comp.BirthActionEntity, Is.Not.Null);
            Assert.That(z.Comp.Biomass, Is.EqualTo(925f));

            // Рост 4: 1000, only once born and holding 200 tiles of flesh
            z.Comp.Biomass = 1000f;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.False, "not born yet");
            z.Comp.Born = true;
            z.Comp.CapturedTiles = 199;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.False, "199 tiles");
            Assert.That(z.Comp.Biomass, Is.EqualTo(1000f));
            z.Comp.CapturedTiles = 200;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.True);
            Assert.That(z.Comp.Growth, Is.EqualTo(4));
            Assert.That(z.Comp.Biomass, Is.Zero);

            // nothing above Рост 4
            z.Comp.Biomass = 1000f;
            Assert.That(ZakatSys.TryBuyGrowth(z), Is.False);
            Assert.That(z.Comp.Growth, Is.EqualTo(ZakatGrowth.MaxGrowth));
            Assert.That(z.Comp.Biomass, Is.EqualTo(1000f));
        });
    }

    /// <summary>
    /// The core window has no branches: one central neuron, clicked to buy the next «Рост», bigger every level.
    /// Рост 4 stays locked (name and cost only) without 200 captured tiles.
    /// </summary>
    [Test]
    public async Task GrowthByClickingCore()
    {
        await Server.WaitPost(() =>
        {
            SpawnHosts(10);
            Zakat.Comp.Biomass = 1000f;
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(SPlayer, ZakatUiKey.Shop, SPlayer);
            ZakatSys.UpdateUis(Zakat);
        });
        await RunTicks(5);

        var lastScale = 0f;
        for (var level = 1; level <= ZakatGrowth.MaxGrowth; level++)
        {
            var expected = level;
            await Client.WaitAssertion(() =>
            {
                var bui = CEntMan.System<SharedUserInterfaceSystem>().TryGetOpenUi<ZakatBoundUserInterface>(CPlayer, ZakatUiKey.Shop, out var ui) ? ui : null;
                Assert.That(bui?.Window, Is.Not.Null, "the core window is open");
                var map = bui!.Window!.Map;
                Assert.That(map.Nodes, Is.Empty, "no branches, only the core");
                Assert.That(map.CenterNode, Is.Not.Null);
                Assert.That(map.CenterScale, Is.GreaterThan(lastScale), "the core grows");
                Assert.That(map.CenterSubtitle, Is.Empty, "no counters");
                lastScale = map.CenterScale;

                var node = map.CenterNode!;
                Assert.That(node.Title, Is.EqualTo($"Рост {expected}").Or.EqualTo($"Growth {expected}"));
                Assert.That(node.Cost, Does.Contain(((int) ZakatGrowth.GrowthCosts[expected - 1]).ToString()));
                if (expected < ZakatGrowth.VineLevel)
                {
                    Assert.That(node.State, Is.EqualTo(ZakatNodeState.Available));
                    Assert.That(map.PressCenter(), Is.True);
                }
                else
                {
                    Assert.That(node.State, Is.EqualTo(ZakatNodeState.Locked), "200 tiles of flesh first");
                    Assert.That(map.PressCenter(), Is.False);
                }
            });
            await RunTicks(5);
            if (expected < ZakatGrowth.VineLevel)
                await Server.WaitAssertion(() => Assert.That(Zakat.Comp.Growth, Is.EqualTo(expected)));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.Growth, Is.EqualTo(3));
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(1000f - 25f - 50f - 75f));
        });
    }

    /// <summary>
    /// Without enough living hosts the next «Рост» is locked: name and cost only, the click does nothing.
    /// </summary>
    [Test]
    public async Task GrowthLockedWithoutHosts()
    {
        await Server.WaitPost(() =>
        {
            ZakatSys.SetGrowth(Zakat, 1);
            SpawnHosts(3);
            Zakat.Comp.Biomass = 1000f;
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(SPlayer, ZakatUiKey.Shop, SPlayer);
            ZakatSys.UpdateUis(Zakat);
        });
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var bui = CEntMan.System<SharedUserInterfaceSystem>().TryGetOpenUi<ZakatBoundUserInterface>(CPlayer, ZakatUiKey.Shop, out var ui) ? ui : null;
            var map = bui!.Window!.Map;
            var node = map.CenterNode!;
            Assert.That(node.State, Is.EqualTo(ZakatNodeState.Locked));
            Assert.That(node.Title, Is.EqualTo("Рост 2").Or.EqualTo("Growth 2"));
            Assert.That(node.Cost, Does.Contain("50"));
            Assert.That(map.CenterSubtitle, Is.Empty, "no counters");
            Assert.That(map.PressCenter(), Is.False);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.Growth, Is.EqualTo(1));
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(1000f));
        });
    }
}
