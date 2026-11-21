using System.Collections.Generic;
using System.Linq;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// «Закат» core: two free picks (humanoid or animal) and nothing paid after them, biomass (+5 per new host,
/// +1 per living host a minute, counter alert), natural immunity off, crew share (animals excluded),
/// red/blue/grey icons only for Закат, cure -> blue, dying out.
/// </summary>
public sealed class ZakatTest : ZakatTestBase
{
    private List<string?> ActionIds()
    {
        var actions = SEntMan.System<SharedActionsSystem>();
        return actions.GetActions(SPlayer).Select(a => SEntMan.GetComponent<MetaDataComponent>(a.Owner).EntityPrototype?.ID).ToList();
    }

    [Test]
    public async Task ZakatCore()
    {
        EntityUid human1 = default, human2 = default, human3 = default, mouse = default;

        await Server.WaitPost(() =>
        {
            human1 = Spawn("MobHuman");
            human2 = Spawn("MobHuman", 2f);
            human3 = Spawn("MobHuman", -2f);
            mouse = Spawn("MobMouse", 0f, 2f);

            // human1 is a crew member (has a mind); the mouse never counts as crew
            var minds = SEntMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(null, "crew");
            minds.TransferTo(mind, human1);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var z = Zakat.Comp;
            Assert.That(z.Strain, Is.Not.Null);
            Assert.That(z.Genotype, Is.Not.Zero);
            Assert.That(z.Biomass, Is.Zero);
            Assert.That(z.StartPicksLeft, Is.EqualTo(2));
            Assert.That(ActionIds(), Is.SupersetOf(new[] { "ActionZakatInfect", "ActionZakatShop", "ActionZakatHostEvolve" }));
            Assert.That(SEntMan.System<AlertsSystem>().IsShowingAlert(SPlayer, z.BiomassAlert), Is.True, "no biomass counter");
        });

        // two free, guaranteed picks: a human and an animal
        await Server.WaitPost(() =>
        {
            Pick(human1);
            Pick(mouse);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var z = Zakat.Comp;
            Assert.That(z.StartPicksLeft, Is.Zero);
            Assert.That(Infected(human1) && Infected(mouse), Is.True);
            Assert.That(z.Biomass, Is.EqualTo(10f), "+5 biomass per new host");
            Assert.That(z.Infected, Has.Count.EqualTo(2));
            Assert.That(ActionIds(), Does.Not.Contain("ActionZakatInfect"), "the pick action must go away after two picks");
            Assert.That(z.CrewTotal, Is.EqualTo(1), "only humanoids with a mind are crew");
            Assert.That(z.CrewInfected, Is.EqualTo(1));
            Assert.That(ZakatSystem.CrewShare(z), Is.EqualTo(1f));
        });

        // no paid infection: a third pick does nothing and costs nothing
        await Server.WaitPost(() => Pick(human3));
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Infected(human3), Is.False);
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(10f));
        });

        // passive income: +1 per living host every 60 s
        await Server.WaitAssertion(() => Assert.That(Zakat.Comp.NextPassive - Now, Is.InRange(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(60))));
        await Server.WaitPost(() => Zakat.Comp.NextPassive = Now);
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(12f), "2 living hosts -> +2");
            Assert.That(Zakat.Comp.NextPassive - Now, Is.GreaterThan(TimeSpan.FromSeconds(59)));
        });

        // natural immunity never fights Закат, no mutation
        await RunSeconds(10);
        await Server.WaitAssertion(() =>
        {
            var d = HostDisease(human1);
            Assert.That(d.BypassesImmunity, Is.True);
            Assert.That(d.ImmunityProgress, Is.Zero, "natural immunity is growing against Закат");
            Assert.That(d.MutationRate, Is.Zero);
        });

        // a vaccinated human shows as immune
        await Server.WaitPost(() => Disease.TryAddImmunity(human2, Zakat.Comp.Genotype));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.Immune, Does.Contain(SEntMan.GetNetEntity(human2)));
            Assert.That(Zakat.Comp.HealthyCount, Is.EqualTo(1)); // human3
        });

        // the disease player sees red / blue / grey
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(ZakatIcon(human1), Is.EqualTo("ZakatIconInfected"));
            Assert.That(ZakatIcon(mouse), Is.EqualTo("ZakatIconInfected"));
            Assert.That(ZakatIcon(human2), Is.EqualTo("ZakatIconImmune"));
            Assert.That(ZakatIcon(human3), Is.EqualTo("ZakatIconHealthy"));
        });

        // cured by medicine: the host turns blue and loses its Закат parts
        await Server.WaitPost(() => Disease.TryCureAll(human1));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.Immune, Does.Contain(SEntMan.GetNetEntity(human1)));
            Assert.That(SEntMan.HasComponent<ZakatHostComponent>(human1), Is.False);
            Assert.That(Zakat.Comp.CrewInfected, Is.Zero);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(ZakatIcon(human1), Is.EqualTo("ZakatIconImmune")));

        // no hosts and no picks: Закат dies out
        await Server.WaitPost(() => Disease.TryCureAll(mouse));
        await RunSeconds(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(SPlayer), Is.True, "Закат survived without hosts"));
    }

    /// <summary>
    /// Medicine against the virus is slowed by resistance.
    /// </summary>
    [Test]
    public async Task ResistanceSlowsMedicine()
    {
        EntityUid human = default;

        await Server.WaitPost(() =>
        {
            human = Spawn("MobHuman");
            Pick(human);
        });
        await RunSeconds(20);

        float before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var d = HostDisease(human);
            Disease.SetCureMultiplier((d.Owner, d), 0.5f);
            before = d.InfectionProgress;
            SEntMan.EventBus.RaiseLocalEvent(human, new Content.Goobstation.Shared.EntityEffects.Disease.DiseaseProgressChange("Viral", -0.04f, false, 1f, 1f));
            after = d.InfectionProgress;
        });
        Assert.That(before - after, Is.EqualTo(0.02f).Within(0.0005f));
    }
}
