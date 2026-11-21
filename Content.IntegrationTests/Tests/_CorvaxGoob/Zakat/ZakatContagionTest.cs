using System.Collections.Generic;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Body.Systems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// «Закат» contagion works from the first second of an infection (no progress scaling):
/// sneeze/cough cones, contact (touch, hit, shove, pull) at a lower chance, vomit;
/// masks, hoods, gloves and bio suits lower it, internals stop anything airborne.
/// </summary>
public sealed class ZakatContagionTest : ZakatTestBase
{
    [Test]
    public async Task ContagionAtProgressZero()
    {
        EntityUid source = default, target = default;
        Entity<ZakatHostComponent> host = default;

        await Server.WaitPost(() =>
        {
            source = Spawn("MobHuman");
            target = Spawn("MobHuman", 1f);
            host = MakeHost(source);
        });

        await Server.WaitAssertion(() =>
        {
            var z = Zakat.Comp;
            Assert.That(HostDisease(source).InfectionProgress, Is.LessThan(0.01f), "the host must be fresh");

            // bare host: infectivity 1
            Assert.That(ZakatSys.GetHostInfectivity(Zakat, host.Comp), Is.EqualTo(1f).Within(0.001f));
            Assert.That(Contagion.GetSpreadProbability(host, target, ZakatContagionSystem.Aerial, z.AerialChance), Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(Contagion.GetSpreadProbability(host, target, ZakatContagionSystem.Contact, z.ContactChance), Is.EqualTo(0.08f).Within(0.001f));

            // sneeze, cough, sweating, rash raise this host's infectivity
            host.Comp.Symptoms.Add(ZakatSymptom.Sneeze);
            host.Comp.Symptoms.Add(ZakatSymptom.Cough);
            host.Comp.Symptoms.Add(ZakatSymptom.Sweating);
            host.Comp.Symptoms.Add(ZakatSymptom.Rash);
            Assert.That(ZakatSys.GetHostInfectivity(Zakat, host.Comp), Is.EqualTo(2f).Within(0.001f));
            Assert.That(Contagion.GetSpreadProbability(host, target, ZakatContagionSystem.Contact, z.ContactChance), Is.EqualTo(0.16f).Within(0.001f));
            host.Comp.Symptoms.Clear();
        });

        // a real cough at progress 0 hits the person in front of the host
        await Server.WaitPost(() =>
        {
            Zakat.Comp.AerialChance = 1f;
            var xform = SEntMan.System<SharedTransformSystem>();
            var dir = xform.GetWorldRotation(source).ToWorldVec();
            xform.SetWorldPosition(target, xform.GetWorldPosition(source) + dir * 1.2f);
            SEntMan.System<ZakatSymptomSystem>().AddSymptom(host, ZakatSymptom.Cough);
            SkipTo(source, ZakatTimer.Cough);
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(host.Comp.LastFired.ContainsKey(ZakatTimer.Cough), Is.True);
            Assert.That(Infected(target), Is.True, "a cough at infection progress 0 did not spread");
            Assert.That(HostDisease(source).InfectionProgress, Is.LessThan(0.05f));
            // the new host starts clean: symptoms belong to one host and are not copied
            Assert.That(SEntMan.GetComponent<ZakatHostComponent>(target).Symptoms, Is.Empty);
        });
    }

    [Test]
    public async Task ContactSpread()
    {
        EntityUid source = default, a = default, b = default, c = default, suited = default;

        await Server.WaitPost(() =>
        {
            source = Spawn("MobHuman");
            a = Spawn("MobHuman", 1f);
            b = Spawn("MobHuman", -1f);
            c = Spawn("MobHuman", 0f, 1f);
            suited = Spawn("MobHuman", 0f, -1f);
            MakeHost(source);
            Zakat.Comp.ContactChance = 1f;

            var inv = SEntMan.System<InventorySystem>();
            Assert.That(inv.TryEquip(suited, SEntMan.SpawnEntity("ClothingOuterBioGeneral", SEntMan.GetCoordinates(TargetCoords)), "outerClothing", force: true), Is.True);
        });
        // every living organism gets the contact hooks on the next Закат update
        await RunSeconds(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ZakatContactComponent>(b), Is.True));

        await Server.WaitAssertion(() =>
        {
            // gloves halve it, a bio suit blocks it
            var host = (source, SEntMan.GetComponent<ZakatHostComponent>(source));
            Assert.That(Contagion.GetSpreadProbability(host, suited, ZakatContagionSystem.Contact, 1f), Is.Zero);

            // touch: healthy grabs the host's hand -> the healthy one catches it
            Contagion.Touch(a, source);
            Assert.That(Infected(a), Is.True, "touch");
        });

        // hit (AttackedEvent on the target)
        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(b, new Content.Shared.Weapons.Melee.Events.AttackedEvent(source, source, SEntMan.GetCoordinates(TargetCoords)));
        });
        await Server.WaitAssertion(() => Assert.That(Infected(b), Is.True, "hit"));

        // shove (DisarmedEvent on the target)
        await Server.WaitPost(() =>
        {
            var ev = new Content.Shared.CombatMode.DisarmedEvent(c, source, 1f);
            SEntMan.EventBus.RaiseLocalEvent(c, ref ev);
        });
        await Server.WaitAssertion(() => Assert.That(Infected(c), Is.True, "shove"));

        // pull: the bio suit keeps the puller safe
        await Server.WaitPost(() =>
        {
            var msg = new Content.Shared.Movement.Pulling.Events.PullStartedMessage(suited, source);
            SEntMan.EventBus.RaiseLocalEvent(source, msg);
            SEntMan.EventBus.RaiseLocalEvent(suited, msg);
        });
        await Server.WaitAssertion(() => Assert.That(Infected(suited), Is.False, "bio suit"));

        // and a real pull by an unprotected human works
        EntityUid puller = default;
        await Server.WaitPost(() => puller = Spawn("MobHuman", 0.5f, 0.5f));
        await RunTicks(2);
        await Server.WaitPost(() =>
        {
            var pulling = SEntMan.System<Content.Shared.Movement.Pulling.Systems.PullingSystem>();
            Assert.That(pulling.TryStartPull(puller, source), Is.True);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() => Assert.That(Infected(puller), Is.True, "pull"));
    }

    [Test]
    public async Task MasksAndInternalsProtect()
    {
        EntityUid source = default, bare = default, masked = default, hooded = default, internals = default, gloved = default;

        await Server.WaitPost(() =>
        {
            source = Spawn("MobHuman");
            bare = Spawn("MobHuman", 1f);
            masked = Spawn("MobHuman", -1f);
            hooded = Spawn("MobHuman", 0f, 1f);
            internals = Spawn("MobHuman", 0f, -1f);
            gloved = Spawn("MobHuman", 1f, 1f);
            MakeHost(source);

            var inv = SEntMan.System<InventorySystem>();
            var at = SEntMan.GetCoordinates(TargetCoords);
            Assert.That(inv.TryEquip(masked, SEntMan.SpawnEntity("ClothingMaskSterile", at), "mask", force: true), Is.True);
            Assert.That(inv.TryEquip(hooded, SEntMan.SpawnEntity("ClothingMaskSterile", at), "mask", force: true), Is.True);
            Assert.That(inv.TryEquip(hooded, SEntMan.SpawnEntity("ClothingHeadHatHoodBioGeneral", at), "head", force: true), Is.True);
            Assert.That(inv.TryEquip(gloved, SEntMan.SpawnEntity("ClothingHandsGlovesLatex", at), "gloves", force: true), Is.True);

            Assert.That(inv.TryEquip(internals, SEntMan.SpawnEntity("ClothingMaskBreath", at), "mask", force: true), Is.True);
            var tank = SEntMan.SpawnEntity("OxygenTankFilled", at);
            Assert.That(inv.TryEquip(internals, tank, "back", force: true), Is.True);
            SEntMan.System<SharedGasTankSystem>().ConnectToInternals((tank, SEntMan.GetComponent<GasTankComponent>(tank)), internals);
        });
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var host = (source, SEntMan.GetComponent<ZakatHostComponent>(source));
            var aerial = Zakat.Comp.AerialChance;
            var pBare = Contagion.GetSpreadProbability(host, bare, ZakatContagionSystem.Aerial, aerial);
            var pMask = Contagion.GetSpreadProbability(host, masked, ZakatContagionSystem.Aerial, aerial);
            var pHood = Contagion.GetSpreadProbability(host, hooded, ZakatContagionSystem.Aerial, aerial);
            Assert.That(SEntMan.System<SharedInternalsSystem>().AreInternalsWorking(internals), Is.True, "test setup: internals are off");
            var pInternals = Contagion.GetSpreadProbability(host, internals, ZakatContagionSystem.Aerial, aerial);

            Assert.That(pBare, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(pMask, Is.LessThan(pBare), "a mask must protect");
            Assert.That(pHood, Is.LessThan(pMask), "a bio hood must protect more");
            Assert.That(pInternals, Is.Zero, "internals must stop airborne Закат");

            // internals do nothing against touch; gloves do
            Assert.That(Contagion.GetSpreadProbability(host, internals, ZakatContagionSystem.Contact, 0.08f), Is.EqualTo(0.08f).Within(0.001f));
            Assert.That(Contagion.GetSpreadProbability(host, gloved, ZakatContagionSystem.Contact, 0.08f), Is.EqualTo(0.04f).Within(0.001f));

            // nothing gets through with internals even at a 100% roll
            for (var i = 0; i < 20; i++)
                Contagion.TrySpread(host, internals, ZakatContagionSystem.Aerial, 1f);
            Assert.That(Infected(internals), Is.False);
        });
    }

    [Test]
    public async Task VomitSpreads()
    {
        EntityUid source = default, near = default, far = default;
        await Server.WaitPost(() =>
        {
            source = Spawn("MobHuman");
            near = Spawn("MobHuman", 0.8f);
            far = Spawn("MobHuman", 4f);
            MakeHost(source);
            Zakat.Comp.VomitChance = 1f;
        });
        await RunTicks(2);
        await Server.WaitPost(() =>
        {
            var host = SEntMan.GetComponent<ZakatHostComponent>(source);
            SEntMan.System<ZakatSymptomSystem>().AddSymptom((source, host), ZakatSymptom.Nausea);
            SkipTo(source, ZakatTimer.Nausea);
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Infected(near), Is.True, "vomit splash");
            Assert.That(Infected(far), Is.False);
        });
    }
}
