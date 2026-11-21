using System.Collections.Generic;
using System.Linq;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Server.Body.Components;
using Content.Shared._CorvaxGoob.Zakat;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared.Nutrition.Components;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// Every one of the 18 host symptoms: its effect and its timing (timers are skipped forward, then checked).
/// </summary>
public sealed class ZakatSymptomTest : ZakatTestBase
{
    private ZakatSymptomSystem Symptoms => SEntMan.System<ZakatSymptomSystem>();

    private EntityUid _host;

    private ZakatHostComponent HostComp => SEntMan.GetComponent<ZakatHostComponent>(_host);

    private async Task SpawnHost(bool air = false)
    {
        if (air)
            await AddAtmosphere();

        await Server.WaitPost(() =>
        {
            _host = Spawn("MobHuman");
            MakeHost(_host);
        });
        await RunTicks(2);
    }

    /// <summary>
    /// Buys the symptom and every symptom before it in its branch (free), checks timers were armed with the right period.
    /// </summary>
    private async Task Grow(ZakatSymptom symptom)
    {
        await Server.WaitAssertion(() =>
        {
            var info = ZakatSymptoms.Get(symptom);
            var branch = ZakatSymptoms.Branches[info.Branch];
            for (var i = 0; i <= info.Index; i++)
            {
                Zakat.Comp.Biomass += ZakatSymptoms.Get(branch[i]).Cost;
                if (!HostComp.Symptoms.Contains(branch[i]))
                    Assert.That(ZakatSys.TryBuySymptom(Zakat, _host, branch[i]), Is.True, branch[i].ToString());
            }

            foreach (var timer in ZakatSymptoms.Timers(symptom))
                Assert.That(HostComp.Next[timer] - Now, Is.EqualTo(ZakatSymptoms.Period(timer)), $"{timer} first fires one period after purchase");
        });
    }

    /// <summary>
    /// Time skip: the timer fires now, and re-arms one period later.
    /// </summary>
    private async Task Fire(ZakatTimer timer)
    {
        TimeSpan at = default;
        await Server.WaitPost(() =>
        {
            at = Now;
            SkipTo(_host, timer);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(HostComp.LastFired.TryGetValue(timer, out var fired), Is.True, $"{timer} did not fire");
            Assert.That(fired, Is.GreaterThanOrEqualTo(at));
            Assert.That(HostComp.Next[timer] - fired, Is.EqualTo(ZakatSymptoms.Period(timer)), $"{timer} period");
        });
    }

    private async Task<bool> NotFiredWithin(ZakatTimer timer, TimeSpan time)
    {
        var before = TimeSpan.Zero;
        await Server.WaitPost(() => before = HostComp.LastFired.GetValueOrDefault(timer));
        await RunSeconds((float) time.TotalSeconds);
        var after = TimeSpan.Zero;
        await Server.WaitPost(() => after = HostComp.LastFired.GetValueOrDefault(timer));
        return before == after;
    }

    [Test]
    public async Task Periods()
    {
        // the spec numbers, in seconds
        var expected = new Dictionary<ZakatTimer, double>
        {
            [ZakatTimer.Pneumonia] = 60, [ZakatTimer.PulmonaryEdema] = 30,
            [ZakatTimer.Nausea] = 300, [ZakatTimer.Dysentery] = 60, [ZakatTimer.BloodyVomit] = 60,
            [ZakatTimer.Rash] = 300, [ZakatTimer.SkinLesions] = 180,
            [ZakatTimer.PsychosisDrop] = 300, [ZakatTimer.PsychosisStupor] = 600, [ZakatTimer.PsychosisSelfHit] = 60,
            [ZakatTimer.Epilepsy] = 180,
        };
        foreach (var (timer, seconds) in expected)
            Assert.That(ZakatSymptoms.Period(timer).TotalSeconds, Is.EqualTo(seconds), timer.ToString());

        Assert.That(ZakatSymptoms.InfectivityBonus(ZakatSymptom.Sneeze), Is.GreaterThan(0));
        Assert.That(ZakatSymptoms.InfectivityBonus(ZakatSymptom.Cough), Is.GreaterThan(ZakatSymptoms.InfectivityBonus(ZakatSymptom.Sneeze)), "cough: more infectivity");
        Assert.That(ZakatSymptoms.InfectivityBonus(ZakatSymptom.Sweating), Is.GreaterThan(0));
        Assert.That(ZakatSymptoms.InfectivityBonus(ZakatSymptom.Rash), Is.GreaterThan(0));
        await Task.CompletedTask;
    }

    [Test]
    public async Task SneezeAndCough()
    {
        await SpawnHost();
        await Grow(ZakatSymptom.Sneeze);
        await Server.WaitAssertion(() => Assert.That(ZakatSys.GetHostInfectivity(Zakat, HostComp), Is.EqualTo(1.2f).Within(0.001f)));
        await Fire(ZakatTimer.Sneeze);
        await Grow(ZakatSymptom.Cough);
        await Server.WaitAssertion(() => Assert.That(ZakatSys.GetHostInfectivity(Zakat, HostComp), Is.EqualTo(1.55f).Within(0.001f)));
        await Fire(ZakatTimer.Cough);
    }

    [Test]
    public async Task LungBranch()
    {
        await SpawnHost(air: true);

        // pneumonia: 5 s without air, once a minute, real suffocation
        await Grow(ZakatSymptom.Pneumonia);
        await Fire(ZakatTimer.Pneumonia);
        await Server.WaitAssertion(() =>
        {
            var block = SEntMan.GetComponent<ZakatBreathBlockedComponent>(_host);
            Assert.That(block.Until - HostComp.LastFired[ZakatTimer.Pneumonia], Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
        await RunSeconds(4);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ZakatBreathBlockedComponent>(_host), Is.True);
            Assert.That(SEntMan.GetComponent<RespiratorComponent>(_host).SuffocationCycles, Is.GreaterThan(0), "not actually suffocating");
        });
        await RunSeconds(4);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ZakatBreathBlockedComponent>(_host), Is.False, "breath block must end after 5 s"));
        await RunSeconds(4);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<RespiratorComponent>(_host).SuffocationCycles, Is.Zero, "breathing again with air"));

        // fibrosis: -30 max stamina
        await Grow(ZakatSymptom.LungFibrosis);
        await Server.WaitAssertion(() =>
        {
            var stamina = SEntMan.GetComponent<StaminaComponent>(_host);
            Assert.That(stamina.CritThreshold, Is.EqualTo(stamina.BaseCritThreshold - 30f).Within(0.01f));
        });

        // edema: 10 s without air every 30 s
        await Grow(ZakatSymptom.PulmonaryEdema);
        await Fire(ZakatTimer.PulmonaryEdema);
        await Server.WaitAssertion(() =>
        {
            var block = SEntMan.GetComponent<ZakatBreathBlockedComponent>(_host);
            Assert.That(block.Until - HostComp.LastFired[ZakatTimer.PulmonaryEdema], Is.EqualTo(TimeSpan.FromSeconds(10)));
        });
        await RunSeconds(8);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ZakatBreathBlockedComponent>(_host), Is.True));
        await RunSeconds(3);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ZakatBreathBlockedComponent>(_host), Is.False));

        // cured: stamina comes back
        await Server.WaitPost(() => Disease.TryCureAll(_host));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            var stamina = SEntMan.GetComponent<StaminaComponent>(_host);
            Assert.That(stamina.CritThreshold, Is.EqualTo(stamina.BaseCritThreshold).Within(0.01f));
        });
    }

    [Test]
    public async Task GutBranch()
    {
        // with air: no vacuum/cold damage muddying the regeneration check
        await SpawnHost(air: true);

        float Thirst() => SEntMan.GetComponent<ThirstComponent>(_host).CurrentThirst;

        // nausea: vomit every 5 min (a vomit costs 40 thirst)
        await Grow(ZakatSymptom.Nausea);
        float before = 0;
        await Server.WaitPost(() =>
        {
            SEntMan.System<Content.Shared.Nutrition.EntitySystems.ThirstSystem>().SetThirst(_host, SEntMan.GetComponent<ThirstComponent>(_host), 300f);
            before = Thirst();
        });
        await Fire(ZakatTimer.Nausea);
        await Server.WaitAssertion(() => Assert.That(before - Thirst(), Is.GreaterThan(35f), "nausea did not vomit"));
        Assert.That(await NotFiredWithin(ZakatTimer.Nausea, TimeSpan.FromSeconds(5)), Is.True);

        // immune suppression: wounds stop healing on their own
        await Grow(ZakatSymptom.ImmuneSuppression);
        EntityUid control = default;
        var dmg = new DamageSpecifier { DamageDict = { ["Heat"] = 6 } };
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.HasComponent<PassiveRegenSuppressedComponent>(_host), Is.True);
            control = Spawn("MobHuman", 2f);
            var damageable = SEntMan.System<DamageableSystem>();
            damageable.TryChangeDamage(_host, dmg, ignoreResistances: true, canMiss: false);
            damageable.TryChangeDamage(control, dmg, ignoreResistances: true, canMiss: false);
        });
        float hostHeat0 = 0, controlHeat0 = 0;
        await RunTicks(2);
        await Server.WaitPost(() =>
        {
            hostHeat0 = SEntMan.GetComponent<DamageableComponent>(_host).Damage.DamageDict.GetValueOrDefault("Heat").Float();
            controlHeat0 = SEntMan.GetComponent<DamageableComponent>(control).Damage.DamageDict.GetValueOrDefault("Heat").Float();
        });
        await RunSeconds(12);
        await Server.WaitAssertion(() =>
        {
            var hostHeat = SEntMan.GetComponent<DamageableComponent>(_host).Damage.DamageDict.GetValueOrDefault("Heat").Float();
            var controlHeat = SEntMan.GetComponent<DamageableComponent>(control).Damage.DamageDict.GetValueOrDefault("Heat").Float();
            Assert.That(controlHeat, Is.LessThan(controlHeat0), "test setup: a normal human regenerates");
            Assert.That(hostHeat, Is.GreaterThanOrEqualTo(hostHeat0), "suppressed host regenerated");
        });

        // sweating: +infectivity
        await Grow(ZakatSymptom.Sweating);
        await Server.WaitAssertion(() => Assert.That(ZakatSys.GetHostInfectivity(Zakat, HostComp), Is.EqualTo(1.25f).Within(0.001f)));

        // dysentery: vomit every minute
        await Grow(ZakatSymptom.Dysentery);
        await Server.WaitPost(() =>
        {
            SEntMan.System<Content.Shared.Nutrition.EntitySystems.ThirstSystem>().SetThirst(_host, SEntMan.GetComponent<ThirstComponent>(_host), 300f);
            before = Thirst();
        });
        await Fire(ZakatTimer.Dysentery);
        await Server.WaitAssertion(() => Assert.That(before - Thirst(), Is.GreaterThan(35f), "dysentery did not vomit"));

        // bloody vomit: 30u of own blood every minute
        await Grow(ZakatSymptom.BloodyVomit);
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        float blood0 = 0;
        await Server.WaitPost(() =>
        {
            var bs = SEntMan.GetComponent<BloodstreamComponent>(_host);
            Assert.That(solutions.ResolveSolution(_host, bs.BloodSolutionName, ref bs.BloodSolution, out var sol), Is.True);
            blood0 = sol!.Volume.Float();
        });
        await Fire(ZakatTimer.BloodyVomit);
        await Server.WaitAssertion(() =>
        {
            var bs = SEntMan.GetComponent<BloodstreamComponent>(_host);
            solutions.ResolveSolution(_host, bs.BloodSolutionName, ref bs.BloodSolution, out var sol);
            Assert.That(blood0 - sol!.Volume.Float(), Is.EqualTo(30f).Within(1.5f));
        });
    }

    [Test]
    public async Task SkinAndMindBranch()
    {
        await SpawnHost(air: true);

        // rash: +infectivity, itching every 5 min
        await Grow(ZakatSymptom.Rash);
        await Server.WaitAssertion(() => Assert.That(ZakatSys.GetHostInfectivity(Zakat, HostComp), Is.EqualTo(1.2f).Within(0.001f)));
        await Fire(ZakatTimer.Rash);

        // skin lesions: 2 piercing + 2 slash + 2 blunt every 3 min
        await Grow(ZakatSymptom.SkinLesions);
        float total0 = 0;
        await Server.WaitPost(() => total0 = SEntMan.GetComponent<DamageableComponent>(_host).TotalDamage.Float());
        await Fire(ZakatTimer.SkinLesions);
        await Server.WaitAssertion(() =>
        {
            var d = SEntMan.GetComponent<DamageableComponent>(_host);
            Assert.That(d.TotalDamage.Float() - total0, Is.EqualTo(6f).Within(0.3f));
            Assert.That(d.Damage.DamageDict.GetValueOrDefault("Piercing").Float(), Is.GreaterThan(1.5f));
            Assert.That(d.Damage.DamageDict.GetValueOrDefault("Slash").Float(), Is.GreaterThan(1.5f));
            Assert.That(d.Damage.DamageDict.GetValueOrDefault("Blunt").Float(), Is.GreaterThan(1.5f));
        });

        // psychosis
        await Grow(ZakatSymptom.Psychosis);
        var hands = SEntMan.System<SharedHandsSystem>();
        EntityUid item = default;
        await Server.WaitPost(() =>
        {
            item = Spawn("Crowbar");
            Assert.That(hands.TryPickupAnyHand(_host, item), Is.True);
        });

        // self-hit with the held crowbar, every minute
        await Server.WaitPost(() => total0 = SEntMan.GetComponent<DamageableComponent>(_host).TotalDamage.Float());
        await Fire(ZakatTimer.PsychosisSelfHit);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DamageableComponent>(_host).TotalDamage.Float(), Is.GreaterThan(total0 + 1f), "self-hit"));

        // drops everything, every 5 min
        await Fire(ZakatTimer.PsychosisDrop);
        await Server.WaitAssertion(() => Assert.That(hands.EnumerateHeld(_host).Any(), Is.False, "still holding items"));

        // 10 s stupor on the floor, every 10 min
        await Fire(ZakatTimer.PsychosisStupor);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<StunnedComponent>(_host), Is.True);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(_host), Is.True);
        });
        await RunSeconds(11);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<StunnedComponent>(_host), Is.False, "stupor longer than 10 s"));

        // epilepsy: falls and shakes 20 s without breathing, then sleeps 10 s; every 3 min
        await Grow(ZakatSymptom.Epilepsy);
        await Fire(ZakatTimer.Epilepsy);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<StunnedComponent>(_host), Is.True);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(_host), Is.True);
            Assert.That(SEntMan.HasComponent<JitteringComponent>(_host), Is.True);
            Assert.That(SEntMan.HasComponent<ZakatBreathBlockedComponent>(_host), Is.True);
            Assert.That(SEntMan.HasComponent<SleepingComponent>(_host), Is.False);
        });
        await RunSeconds(19);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ZakatBreathBlockedComponent>(_host), Is.True, "seizure shorter than 20 s"));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ZakatBreathBlockedComponent>(_host), Is.False);
            Assert.That(SEntMan.HasComponent<SleepingComponent>(_host), Is.True, "no sleep after the seizure");
        });
        await RunSeconds(11);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<SleepingComponent>(_host), Is.False, "slept longer than 10 s"));
    }

    [Test]
    public async Task OrgansBranch()
    {
        await SpawnHost(air: true);
        var bodySys = SEntMan.System<Content.Shared.Body.Systems.SharedBodySystem>();

        // тахикардия: reagents are worked off 3x slower (organ metabolizers only)
        await Grow(ZakatSymptom.Tachycardia);
        await Server.WaitAssertion(() =>
        {
            var metabolizers = 0;
            foreach (var (organ, _) in bodySys.GetBodyOrgans(_host))
            {
                if (!SEntMan.TryGetComponent<MetabolizerComponent>(organ, out var m))
                    continue;

                metabolizers++;
                Assert.That(m.UpdateIntervalMultiplier, Is.EqualTo(ZakatSymptomSystem.TachycardiaSlowdown).Within(0.001f), SEntMan.ToPrettyString(organ).ToString());
            }

            Assert.That(metabolizers, Is.GreaterThan(0));
            Assert.That(SEntMan.GetComponent<RespiratorComponent>(_host).UpdateIntervalMultiplier, Is.EqualTo(1f), "breathing is untouched");
        });

        // гемофилия: bleeding never stops on its own
        await Grow(ZakatSymptom.Hemophilia);
        EntityUid control = default;
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.HasComponent<ZakatHemophiliaComponent>(_host), Is.True);
            var ev = new Content.Shared.Body.Events.BleedModifierEvent(5f, 0.33f);
            SEntMan.EventBus.RaiseLocalEvent(_host, ref ev);
            Assert.That(ev.BleedReductionAmount, Is.Zero);

            control = Spawn("MobHuman", 2f);
            var blood = SEntMan.System<Content.Shared.Body.Systems.SharedBloodstreamSystem>();
            blood.TryModifyBleedAmount(_host, 2f);
            blood.TryModifyBleedAmount(control, 2f);
        });
        await RunSeconds(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(control).BleedAmount, Is.LessThan(2f), "test setup: a normal human clots");
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(_host).BleedAmount, Is.GreaterThanOrEqualTo(2f), "the host clotted");
        });

        // гнилокровие: 5 Poison once a minute
        await Grow(ZakatSymptom.RotBlood);
        float poison0 = 0;
        await Server.WaitPost(() => poison0 = Poison());
        await Fire(ZakatTimer.RotBlood);
        await Server.WaitAssertion(() => Assert.That(Poison() - poison0, Is.EqualTo(5f).Within(0.05f)));
        Assert.That(ZakatSymptoms.Period(ZakatTimer.RotBlood), Is.EqualTo(TimeSpan.FromMinutes(1)));

        // распад органов: every 2 minutes every organ loses 10%
        await Grow(ZakatSymptom.OrganDecay);
        var before = new Dictionary<EntityUid, float>();
        await Server.WaitPost(() =>
        {
            foreach (var (organ, comp) in bodySys.GetBodyOrgans(_host))
                before[organ] = comp.OrganIntegrity.Float();
        });
        await Fire(ZakatTimer.OrganDecay);
        await Server.WaitAssertion(() =>
        {
            var organs = 0;
            foreach (var (organ, comp) in bodySys.GetBodyOrgans(_host))
            {
                organs++;
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(organ).EntityPrototype?.ID, Is.Not.EqualTo("Butt"), "no butt organ");
                Assert.That(before[organ] - comp.OrganIntegrity.Float(), Is.EqualTo(comp.IntegrityCap.Float() * 0.1f).Within(0.05f), SEntMan.ToPrettyString(organ).ToString());
            }

            Assert.That(organs, Is.GreaterThan(0));
        });
        Assert.That(ZakatSymptoms.Period(ZakatTimer.OrganDecay), Is.EqualTo(TimeSpan.FromMinutes(2)));

        // cured: everything grown goes away
        await Server.WaitAssertion(() =>
        {
            Symptoms.ClearHost((_host, HostComp));
            Assert.That(SEntMan.HasComponent<ZakatHemophiliaComponent>(_host), Is.False);
            foreach (var (organ, _) in bodySys.GetBodyOrgans(_host))
            {
                if (SEntMan.TryGetComponent<MetabolizerComponent>(organ, out var m))
                    Assert.That(m.UpdateIntervalMultiplier, Is.EqualTo(1f).Within(0.001f));
            }
        });
    }

    private float Poison() => SEntMan.GetComponent<DamageableComponent>(_host).Damage.DamageDict.GetValueOrDefault("Poison").Float();
}
