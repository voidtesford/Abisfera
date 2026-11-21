using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Goobstation.Common.Atmos;
using Content.Goobstation.Common.Body.Components;
using Content.Goobstation.Common.Temperature.Components;
using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Roles;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// «Закат» p2: taking control of a host (Рост 1), the hive mind and «порабощение» (Рост 2),
/// every slave mutation and the «биошмат».
/// </summary>
public sealed class ZakatHiveTest : ZakatTestBase
{
    private ZakatSlaveSystem Slaves => SEntMan.System<ZakatSlaveSystem>();
    private ZakatSymptomSystem Symptoms => SEntMan.System<ZakatSymptomSystem>();
    private SharedMindSystem MindSys => SEntMan.System<SharedMindSystem>();
    private MobStateSystem MobStates => SEntMan.System<MobStateSystem>();
    private DamageableSystem Damageable => SEntMan.System<DamageableSystem>();

    private float Total(EntityUid uid) => SEntMan.GetComponent<DamageableComponent>(uid).TotalDamage.Float();

    private void Hit(EntityUid uid, string type, float amount)
    {
        Damageable.TryChangeDamage(uid, new DamageSpecifier { DamageDict = { [type] = amount } }, ignoreResistances: true, canMiss: false);
    }

    /// <summary>
    /// A host with its own (sessionless) mind, enslaved, with Рост 2 bought for free.
    /// </summary>
    private EntityUid MakeSlave(float dx = 0f, bool mind = true)
    {
        var uid = Spawn("MobHuman", dx);
        if (mind)
        {
            var m = MindSys.CreateMind(null, "slave");
            MindSys.TransferTo(m, uid);
        }

        var host = MakeHost(uid);
        if (Zakat.Comp.Growth < ZakatGrowth.HiveMindLevel)
            ZakatSys.SetGrowth(Zakat, ZakatGrowth.HiveMindLevel);

        Zakat.Comp.Biomass += ZakatGrowth.EnslaveCost;
        Assert.That(ZakatSys.TryEnslave(Zakat, uid), Is.True);
        Assert.That(host.Comp.Enslaved, Is.True);
        return uid;
    }

    private void Mutate(EntityUid slave, ZakatMutation mutation)
    {
        var before = Zakat.Comp.Biomass = 100f;
        Assert.That(ZakatSys.TryBuyMutation(Zakat, slave, mutation), Is.True, mutation.ToString());
        Assert.That(before - Zakat.Comp.Biomass, Is.EqualTo(ZakatGrowth.Cost(mutation)), mutation.ToString());
    }

    [Test]
    public async Task Costs()
    {
        var expected = new Dictionary<ZakatMutation, float>
        {
            [ZakatMutation.SpaceResistance] = 15, [ZakatMutation.NoPain] = 20, [ZakatMutation.Regeneration] = 20,
            [ZakatMutation.LivingArmor] = 20, [ZakatMutation.RotGrowths] = 20, [ZakatMutation.ParasiteHeart] = 25,
            [ZakatMutation.Claws] = 10, [ZakatMutation.MeatHarpoon] = 20, [ZakatMutation.Devourer] = 20,
        };
        Assert.That(ZakatGrowth.Mutations, Is.EquivalentTo(expected.Keys));
        foreach (var (m, cost) in expected)
            Assert.That(ZakatGrowth.Cost(m), Is.EqualTo(cost), m.ToString());
        Assert.That(ZakatGrowth.EnslaveCost, Is.EqualTo(20f));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Possession()
    {
        EntityUid host = default, victimMind = default;
        await Server.WaitPost(() =>
        {
            // the interaction harness drives the disease without a mind; give it the real one
            var own = MindSys.GetOrCreateMind(ServerSession.UserId);
            MindSys.TransferTo(own, SPlayer);

            host = Spawn("MobHuman");
            victimMind = MindSys.CreateMind(null, "victim");
            MindSys.TransferTo(victimMind, host);
            MakeHost(host);
        });

        var possession = SEntMan.System<ZakatPossessionSystem>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(possession.TryPossess(Zakat, host), Is.False, "needs Рост 1");
            ZakatSys.SetGrowth(Zakat, 1);
            Assert.That(Zakat.Comp.Growth, Is.EqualTo(1));
            Assert.That(SEntMan.GetComponent<ZakatHostComponent>(host).Zakat, Is.EqualTo(SPlayer));
            Assert.That(MobStates.IsAlive(host), Is.True);
            Assert.That(possession.TryPossess(Zakat, host), Is.True);

            Assert.That(MindSys.TryGetMind(host, out var inHost, out _), Is.True);
            Assert.That(MindSys.TryGetMind(SPlayer, out var zakatMind, out _), Is.False, "the disease left its own body");
            Assert.That(inHost, Is.Not.EqualTo(victimMind));
            var stash = Zakat.Comp.VictimStash;
            Assert.That(stash, Is.Not.Null);
            Assert.That(MindSys.TryGetMind(stash!.Value, out var stashed, out _) && stashed == victimMind, "the victim waits aside");
            Assert.That(SEntMan.HasComponent<ZakatBleedingEyesComponent>(host), Is.True, "blood from the eyes");
            Assert.That(Zakat.Comp.PossessEnd - Now, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(possession.TryPossess(Zakat, host), Is.False, "one at a time");

            // time is up
            Zakat.Comp.PossessEnd = Now;
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.PossessedHost, Is.Null);
            Assert.That(MindSys.TryGetMind(SPlayer, out _, out _), Is.True, "the disease is back");
            Assert.That(MindSys.TryGetMind(host, out var back, out _) && back == victimMind, "the victim got the body back");
            Assert.That(SEntMan.HasComponent<ZakatBleedingEyesComponent>(host), Is.False);
            Assert.That(Zakat.Comp.VictimStash, Is.Null);

            // death ends it early
            Assert.That(possession.TryPossess(Zakat, host), Is.True);
            Hit(host, "Poison", 400);
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.PossessedHost, Is.Null);
            Assert.That(MindSys.TryGetMind(SPlayer, out _, out _), Is.True);
        });
    }

    [Test]
    public async Task Enslaving()
    {
        EntityUid host = default, mindId = default;
        await Server.WaitPost(() =>
        {
            host = Spawn("MobHuman");
            mindId = MindSys.CreateMind(null, "slave");
            MindSys.TransferTo(mindId, host);
            MakeHost(host);
        });

        await Server.WaitAssertion(() =>
        {
            var hostComp = SEntMan.GetComponent<ZakatHostComponent>(host);
            Zakat.Comp.Biomass = 1000f;
            foreach (var s in new[] { ZakatSymptom.Sneeze, ZakatSymptom.Cough, ZakatSymptom.Pneumonia, ZakatSymptom.Nausea, ZakatSymptom.ImmuneSuppression, ZakatSymptom.Sweating })
                Assert.That(ZakatSys.TryBuySymptom(Zakat, host, s), Is.True, s.ToString());

            Zakat.Comp.Biomass = 100f;
            Assert.That(ZakatSys.TryEnslave(Zakat, host), Is.False, "needs Рост 2");
            ZakatSys.SetGrowth(Zakat, 1);
            Assert.That(ZakatSys.TryEnslave(Zakat, host), Is.False, "needs Рост 2");
            ZakatSys.SetGrowth(Zakat, 2);
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(100f));
            Assert.That(ZakatSys.TryEnslave(Zakat, host), Is.True);
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(80f), "20 biomass");
            Assert.That(ZakatSys.TryEnslave(Zakat, host), Is.False, "once");

            // debuffs are gone, spreading stays
            Assert.That(hostComp.Symptoms, Is.EquivalentTo(new[] { ZakatSymptom.Sneeze, ZakatSymptom.Cough, ZakatSymptom.Sweating }));
            Assert.That(SEntMan.HasComponent<Content.Shared._CorvaxGoob.Zakat.PassiveRegenSuppressedComponent>(host), Is.False);
            Assert.That(ZakatSys.TryBuySymptom(Zakat, host, ZakatSymptom.Pneumonia), Is.False, "no new debuffs for a slave");

            // hive mind shared with the disease, role and objective
            var hive = new Robust.Shared.Prototypes.ProtoId<CollectiveMindPrototype>(Zakat.Comp.HiveMind);
            Assert.That(SEntMan.GetComponent<CollectiveMindComponent>(host).Channels, Does.Contain(hive));
            Assert.That(SEntMan.GetComponent<CollectiveMindComponent>(SPlayer).Channels, Does.Contain(hive));
            Assert.That(SEntMan.System<SharedRoleSystem>().MindHasRole<ZakatSlaveRoleComponent>(mindId), Is.True);
            var mind = SEntMan.GetComponent<MindComponent>(mindId);
            Assert.That(mind.Objectives.Any(o => SEntMan.HasComponent<ZakatSlaveConditionComponent>(o)), Is.True);

            // mutations: any order, each once
            Zakat.Comp.Biomass = 100f;
            Assert.That(ZakatSys.TryBuyMutation(Zakat, host, ZakatMutation.Devourer), Is.True);
            Assert.That(ZakatSys.TryBuyMutation(Zakat, host, ZakatMutation.Claws), Is.True);
            Assert.That(ZakatSys.TryBuyMutation(Zakat, host, ZakatMutation.Claws), Is.False);
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(70f));

            // cured: the hive lets go
            Symptoms.ClearHost((host, hostComp));
            Assert.That(SEntMan.HasComponent<ZakatSlaveComponent>(host), Is.False);
            Assert.That(SEntMan.GetComponent<CollectiveMindComponent>(host).Channels, Does.Not.Contain(hive));
            Assert.That(SEntMan.System<SharedRoleSystem>().MindHasRole<ZakatSlaveRoleComponent>(mindId), Is.False);
        });
    }

    [Test]
    public async Task BodyMutations()
    {
        EntityUid slave = default;
        EntityUid control = default;
        await Server.WaitPost(() =>
        {
            slave = MakeSlave();
            control = Spawn("MobHuman", -2f);
        });
        await Server.WaitAssertion(() =>
        {
            // устойчивость к космосу
            Mutate(slave, ZakatMutation.SpaceResistance);
            Assert.That(SEntMan.HasComponent<SpecialBreathingImmunityComponent>(slave), Is.True);
            Assert.That(SEntMan.HasComponent<SpecialPressureImmunityComponent>(slave), Is.True);
            Assert.That(SEntMan.HasComponent<SpecialLowTempImmunityComponent>(slave), Is.True);

            // живая броня: 30% off everything, nothing on examine
            Mutate(slave, ZakatMutation.LivingArmor);
            var before = Total(slave);
            Damageable.TryChangeDamage(slave, new DamageSpecifier { DamageDict = { ["Blunt"] = 10 } }, canMiss: false);
            Assert.That(Total(slave) - before, Is.EqualTo(7f).Within(0.05f));
            Assert.That(SEntMan.HasComponent<Content.Shared.Armor.ArmorComponent>(slave), Is.False);

            // когти: an action puts bone claws in the hand and takes them back
            Mutate(slave, ZakatMutation.Claws);
            var slaveComp = SEntMan.GetComponent<ZakatSlaveComponent>(slave);
            Assert.That(slaveComp.ClawsAction, Is.Not.Null, "no claws action");
            var hands = SEntMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
            var toggle = new ZakatSlaveClawsActionEvent { Performer = slave };
            SEntMan.EventBus.RaiseLocalEvent(slave, toggle);
            Assert.That(toggle.Handled, Is.True);
            Assert.That(slaveComp.ClawsItem, Is.Not.Null, "no claws out");
            var claws = slaveComp.ClawsItem!.Value;
            Assert.That(hands.IsHolding(slave, claws), Is.True, "the claws aren't in the hand");
            var melee = SEntMan.GetComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(claws);
            Assert.That(melee.Damage.DamageDict.GetValueOrDefault("Slash").Float(), Is.EqualTo(15f));
            Assert.That(hands.TryDrop(slave, claws), Is.False, "the claws can be dropped");
            toggle = new ZakatSlaveClawsActionEvent { Performer = slave };
            SEntMan.EventBus.RaiseLocalEvent(slave, toggle);
            Assert.That(slaveComp.ClawsItem, Is.Null, "the claws didn't go back in");
            Assert.That(SEntMan.Deleted(claws) || SEntMan.IsQueuedForDeletion(claws), Is.True);
        });

        // регенерация: 2 per second
        // (rejuvenating would cure the strain, so every phase gets a fresh slave)
        await Server.WaitPost(() =>
        {
            slave = MakeSlave(2f);
            Mutate(slave, ZakatMutation.Regeneration);
            Hit(slave, "Blunt", 30);
            Hit(control, "Blunt", 30);
        });
        await RunSeconds(5);
        // the same wound keeps getting worse on both (bleeding), the slave knits about 2 per second on top
        await Server.WaitAssertion(() => Assert.That(Total(control) - Total(slave), Is.InRange(7f, 12.5f), "about 2/s"));

        // боли нет: no crit, death still comes
        await Server.WaitPost(() =>
        {
            slave = MakeSlave(-4f);
            Mutate(slave, ZakatMutation.NoPain);
            Assert.That(SEntMan.System<MobThresholdSystem>().TryGetThresholdForState(slave, MobState.Critical, out var crit), Is.True);
            Hit(slave, "Poison", crit!.Value.Float() + 10f);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(MobStates.IsAlive(slave), Is.True, "a slave without pain does not fall into crit");
            Hit(slave, "Poison", 300);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() => Assert.That(MobStates.IsDead(slave), Is.True));
    }

    [Test]
    public async Task RotGrowths()
    {
        EntityUid slave = default, victim = default;
        float crit0 = 0, dead0 = 0, sprint0 = 0;
        await Server.WaitPost(() =>
        {
            slave = MakeSlave();
            var thresholds = SEntMan.System<MobThresholdSystem>();
            thresholds.TryGetThresholdForState(slave, MobState.Critical, out var c);
            thresholds.TryGetThresholdForState(slave, MobState.Dead, out var d);
            crit0 = c!.Value.Float();
            dead0 = d!.Value.Float();
            sprint0 = SEntMan.GetComponent<MovementSpeedModifierComponent>(slave).CurrentSprintSpeed;
            Mutate(slave, ZakatMutation.RotGrowths);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var thresholds = SEntMan.System<MobThresholdSystem>();
            thresholds.TryGetThresholdForState(slave, MobState.Critical, out var c);
            thresholds.TryGetThresholdForState(slave, MobState.Dead, out var d);
            Assert.That(c!.Value.Float(), Is.EqualTo(crit0 + 50f), "+50 max HP");
            Assert.That(d!.Value.Float(), Is.EqualTo(dead0 + 50f));
            Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(slave).CurrentSprintSpeed, Is.EqualTo(sprint0 - 0.5f).Within(0.01f));
            Assert.That(SEntMan.HasComponent<ZakatRotGrowthsComponent>(slave), Is.True, "growths overlay");

            // a wound splashes infectious blood
            Hit(slave, "Slash", 5);
            var puddles = SEntMan.EntityQuery<ZakatInfectiousPuddleComponent>().ToList();
            Assert.That(puddles, Is.Not.Empty, "no infectious puddle");
        });

        // a bare human stepping in it catches the strain
        await Server.WaitPost(() =>
        {
            victim = Spawn("MobHuman", 3f);
            var puddle = SEntMan.EntityQuery<ZakatInfectiousPuddleComponent>().First().Owner;
            var xform = SEntMan.System<SharedTransformSystem>();
            xform.SetWorldPosition(victim, xform.GetWorldPosition(puddle));
        });
        await RunSeconds(1.5f);
        await Server.WaitAssertion(() => Assert.That(Infected(victim), Is.True, "stepped in infectious blood and stayed clean"));
    }

    [Test]
    public async Task HarpoonAndDevourer()
    {
        EntityUid slave = default, target = default, corpse = default;
        Vector2 slavePos = default;
        await AddGravity();
        await AddGravity(MapData.MapUid);
        await Server.WaitPost(() =>
        {
            slave = MakeSlave();
            Mutate(slave, ZakatMutation.MeatHarpoon);
            Mutate(slave, ZakatMutation.Devourer);
            var comp = SEntMan.GetComponent<ZakatSlaveComponent>(slave);
            Assert.That(comp.HarpoonAction, Is.Not.Null);
            Assert.That(comp.DevourAction, Is.Not.Null);

            target = Spawn("MobHuman", 5f);
        });
        await RunTicks(3);
        await Server.WaitPost(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            slavePos = xform.GetWorldPosition(slave);
            var hit = Slaves.FireHarpoon(slave, xform.GetMapCoordinates(target));
            Assert.That(hit, Is.EqualTo(target));
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(target), Is.True, "knocked down");
        });
        await RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            Assert.That((xform.GetWorldPosition(target) - slavePos).Length(), Is.LessThan(3f), "yanked to the slave");
        });

        // пожиратель: the corpse is torn apart, the slave healed, the disease fed
        float biomass0 = 0;
        await Server.WaitPost(() =>
        {
            corpse = Spawn("MobHuman", 1f);
            Hit(corpse, "Poison", 250);
            // upd2: the do-after takes seconds - no bleeding (blunt) and no suffocating on the airless test map meanwhile
            SEntMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(slave);
            Hit(slave, "Blunt", 80);
            biomass0 = Zakat.Comp.Biomass;
        });
        await RunTicks(2);
        float hurt = 0;
        await Server.WaitAssertion(() =>
        {
            Assert.That(MobStates.IsDead(corpse), Is.True);
            hurt = Total(slave);
            var comp = SEntMan.GetComponent<ZakatSlaveComponent>(slave);
            Assert.That(Slaves.TryDevour((slave, comp), target), Is.False, "only corpses");
            Assert.That(Slaves.TryDevour((slave, comp), corpse), Is.True);
            // upd2: not instant - a do-after with the gore effect on the corpse
            Assert.That(SEntMan.EntityExists(corpse), Is.True, "devoured instantly");
            Assert.That(Total(slave), Is.EqualTo(hurt).Within(0.5f), "healed instantly");
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(biomass0), "fed instantly");
            Assert.That(comp.DevourEffect, Is.Not.Null, "no devour effect");
            Assert.That(SEntMan.GetComponent<TransformComponent>(comp.DevourEffect!.Value).ParentUid, Is.EqualTo(corpse));
        });
        await RunSeconds((float) ZakatSlaveSystem.DevourTime.TotalSeconds * 0.5f);
        await Server.WaitAssertion(() => Assert.That(SEntMan.EntityExists(corpse), Is.True, "devoured before the do-after ended"));
        await RunSeconds((float) ZakatSlaveSystem.DevourTime.TotalSeconds * 0.5f + 1f);
        await Server.WaitAssertion(() =>
        {
            var dmg = string.Join(", ", SEntMan.GetComponent<DamageableComponent>(slave).Damage.DamageDict.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}={kv.Value}"));
            // 70 healed evenly over every damage type; the airless map adds a little heat/cold meanwhile
            Assert.That(hurt - Total(slave), Is.GreaterThan(40f), dmg);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(slave).Damage.DamageDict["Blunt"].Float(), Is.LessThan(80f - 30f), dmg);
            Assert.That(Zakat.Comp.Biomass - biomass0, Is.EqualTo(10f));
            Assert.That(SEntMan.GetComponent<ZakatSlaveComponent>(slave).DevourEffect, Is.Null, "the effect stayed");
        });
        await RunTicks(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(corpse) || !SEntMan.EntityExists(corpse), Is.True, "the corpse was gibbed"));

        // upd2: a hit breaks the devouring off; the corpse stays
        EntityUid corpse2 = default;
        await Server.WaitPost(() =>
        {
            corpse2 = Spawn("MobHuman", 1f);
            Hit(corpse2, "Poison", 250);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            biomass0 = Zakat.Comp.Biomass;
            Assert.That(Slaves.TryDevour((slave, SEntMan.GetComponent<ZakatSlaveComponent>(slave)), corpse2), Is.True);
        });
        await RunSeconds(1f);
        await Server.WaitPost(() => Hit(slave, "Blunt", 10));
        await RunSeconds((float) ZakatSlaveSystem.DevourTime.TotalSeconds + 1f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(corpse2) && !SEntMan.IsQueuedForDeletion(corpse2), Is.True, "devoured despite the interruption");
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(biomass0));
            Assert.That(SEntMan.GetComponent<ZakatSlaveComponent>(slave).DevourEffect, Is.Null, "the effect stayed after the interruption");
        });
    }

    [Test]
    public async Task ParasiteHeart()
    {
        EntityUid slave = default, mindId = default, corpse = default;
        await Server.WaitPost(() =>
        {
            slave = MakeSlave();
            MindSys.TryGetMind(slave, out mindId, out _);
            Mutate(slave, ZakatMutation.ParasiteHeart);
            Hit(slave, "Poison", 400);
        });
        await RunTicks(3);
        EntityUid shmat = default;
        await Server.WaitAssertion(() =>
        {
            var shmats = SEntMan.EntityQuery<ZakatBioshmatComponent>().ToList();
            Assert.That(shmats, Has.Count.EqualTo(1), "no bioshmat burst out");
            shmat = shmats[0].Owner;
            Assert.That(MindSys.TryGetMind(shmat, out var m, out _) && m == mindId, "the slave's mind rides the bioshmat");
            Assert.That(SEntMan.System<MobThresholdSystem>().TryGetThresholdForState(shmat, MobState.Dead, out var dead) && dead == 25, "25 HP");
            Assert.That(shmats[0].InfestAction, Is.Not.Null);

            corpse = Spawn("MobHuman", 1f);
            Hit(corpse, "Poison", 250);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var bios = SEntMan.System<ZakatBioshmatSystem>();
            Assert.That(MobStates.IsAlive(shmat), Is.True, "shmat alive");
            Assert.That(MobStates.IsDead(corpse), Is.True, "corpse dead");
            Assert.That(bios.TryInfest((shmat, SEntMan.GetComponent<ZakatBioshmatComponent>(shmat)), corpse), Is.True);
            Assert.That(MobStates.IsAlive(corpse), Is.True, "the corpse got up");
            Assert.That(MindSys.TryGetMind(corpse, out var m, out _) && m == mindId);
            Assert.That(SEntMan.GetComponent<ZakatHostComponent>(corpse).Enslaved, Is.True);
        });
        await RunTicks(2);
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(shmat), Is.True);
            Assert.That(SEntMan.HasComponent<ZakatSlaveComponent>(corpse), Is.True, "the revived body stays a slave");
            Assert.That(Infected(corpse), Is.True);
        });

        // left alone for 5 minutes, a driven shmat grows a flesh body of its own
        EntityUid shmat2 = default;
        await Server.WaitPost(() =>
        {
            shmat2 = Spawn("MobZakatBioshmat", 2f);
            var comp = SEntMan.GetComponent<ZakatBioshmatComponent>(shmat2);
            comp.Zakat = SPlayer;
            Assert.That(comp.GrowAt - Now, Is.EqualTo(TimeSpan.FromMinutes(5)));
            var m = MindSys.CreateMind(null, "shmat");
            MindSys.TransferTo(m, shmat2);
            comp.GrowAt = Now;
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(shmat2) || !SEntMan.EntityExists(shmat2), Is.True);
            var grown = SEntMan.EntityQuery<ZakatSlaveComponent>().Select(s => s.Owner)
                .Where(u => u != corpse && u != slave).ToList();
            Assert.That(grown, Has.Count.EqualTo(1), "no flesh humanoid");
            Assert.That(MobStates.IsAlive(grown[0]), Is.True);
            Assert.That(SEntMan.GetComponent<ZakatHostComponent>(grown[0]).Mutations, Is.Empty, "upgradable again from scratch");
        });
    }
}
