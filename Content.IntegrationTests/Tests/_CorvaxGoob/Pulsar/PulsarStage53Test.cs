using System.Collections.Generic;
using System.Linq;
using Content.Goobstation.Common.CCVar;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Server._CorvaxGoob.Pulsar.Units;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Server.Gravity;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._CorvaxGoob.Pulsar.PulsarRequestsTest;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 5.3: units really heal, carry the dead, recharge, share the work; anomalies;
/// door names on the station maps; unit names and nicknames in requests.
/// </summary>
[TestFixture]
[NonParallelizable]
[TestOf(typeof(PulsarUnitsSystem))]
public sealed class PulsarStage53Test
{
    internal static async Task Walking(Ctx ctx)
    {
        var server = ctx.Pair.Server;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, false);
            var gravity = ctx.EntMan.EnsureComponent<GravityComponent>(ctx.Map.Grid);
            server.System<GravitySystem>().EnableGravity(ctx.Map.Grid, gravity);
        });
        await ctx.Pair.RunSeconds(1);
    }

    internal static async Task StopWalking(Ctx ctx)
    {
        await ctx.Pair.Server.WaitPost(() => ctx.Pair.Server.CfgMan.SetCVar(GoobCVars.DisablePathfinding, true));
    }

    internal static async Task<EntityUid> LinkedUnit(Ctx ctx, string proto, int x, int y)
    {
        var units = ctx.Pair.Server.System<PulsarUnitsSystem>();
        EntityUid unit = default;
        await ctx.Pair.Server.WaitPost(() => unit = ctx.EntMan.SpawnEntity(proto, ctx.At(x, y)));
        await ctx.Pair.RunTicksSync(5);
        await ctx.Pair.Server.WaitAssertion(() => Assert.That(units.TryLink(unit, ctx.Core, out var fail), fail));
        await ctx.Pair.RunTicksSync(2);
        return unit;
    }

    internal static async Task Hurt(Ctx ctx, EntityUid mob, string type, float amount)
    {
        var server = ctx.Pair.Server;
        await server.WaitPost(() =>
        {
            var spec = new DamageSpecifier(server.ResolveDependency<IPrototypeManager>().Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>(type), amount);
            server.System<DamageableSystem>().TryChangeDamage(mob, spec, ignoreResistances: true, canMiss: false);
        });
    }

    internal static float Total(Ctx ctx, EntityUid mob)
    {
        var dict = ctx.EntMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict;
        var sum = 0f;
        foreach (var type in new[] { "Blunt", "Slash", "Piercing", "Heat", "Cold", "Shock", "Caustic", "Poison" })
        {
            if (dict.TryGetValue(type, out var v))
                sum += v.Float();
        }

        return sum;
    }

    /// <summary>The test map has no air: the patient must not suffocate or burst while the test runs.</summary>
    internal static async Task NoAir(Ctx ctx, EntityUid mob)
    {
        await ctx.Pair.Server.WaitPost(() =>
        {
            ctx.EntMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(mob);
            ctx.EntMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(mob);
        });
    }

    /// <summary>The medical cyborg uses its first aid kit items on the patient: the damage really goes down.</summary>
    [Test]
    public async Task MedicalBorgReallyHeals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Passenger");
        var server = pair.Server;
        await Walking(ctx);
        var patient = await Person(ctx, "PassengerIDCard", 2, 3);
        await NoAir(ctx, patient);
        await Hurt(ctx, patient, "Blunt", 30);
        await Hurt(ctx, patient, "Heat", 20);
        var before = 0f;
        await server.WaitPost(() => before = Total(ctx, patient));

        var borg = await LinkedUnit(ctx, "BorgChassisMedical", -3, 0);
        var after = before;
        for (var i = 0; i < 60 && after > before - 25; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                after = Total(ctx, patient);
                if (i % 5 == 0)
                    TestContext.Out.WriteLine($"{i}s damage {after} task {ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Task?.Kind}/{ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Task?.Stage}");
            });
        }

        await StopWalking(ctx);
        Assert.That(after, Is.LessThan(before - 25), $"healed from {before} to {after}");
        await pair.CleanReturnAsync();
    }

    /// <summary>At 20% the cyborg goes into the recharging station, charges, comes out and carries on with its order.</summary>
    [Test]
    public async Task RechargesAndResumes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Passenger");
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();
        var cells = server.System<Content.Shared.PowerCell.PowerCellSystem>();
        var battery = server.System<Content.Shared.Power.EntitySystems.SharedBatterySystem>();
        await Walking(ctx);
        EntityUid charger = default;
        await server.WaitPost(() =>
        {
            charger = ctx.EntMan.SpawnEntity("BorgCharger", ctx.At(-6, 4));
            server.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().SetNeedsPower(charger, false);
        });
        var borg = await LinkedUnit(ctx, "BorgChassisEngineer", 0, 0);
        await server.WaitPost(() =>
        {
            var slot = ctx.EntMan.GetComponent<Content.Shared.PowerCell.Components.PowerCellSlotComponent>(borg);
            var cell = ctx.EntMan.SpawnEntity("PowerCellHigh", ctx.At(0, 0));
            var containers = server.System<Robust.Shared.Containers.SharedContainerSystem>();
            var slotContainer = containers.GetContainer(borg, slot.CellSlotId);
            foreach (var old in slotContainer.ContainedEntities.ToList())
                containers.Remove(old, slotContainer);
            Assert.That(containers.Insert(cell, slotContainer), "cell inserted");
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(cells.TryGetBatteryFromSlot(borg, out var b), "power cell");
            battery.SetCharge(b!.Value.AsNullable(), b.Value.Comp.MaxCharge * 0.15f);
            var link = (borg, ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg));
            Assert.That(units.TryOrder(link, new PulsarUnitOrder
            {
                Target = ctx.At(8, -4), Place = "склад", Rank = PulsarOrderRank.Captain, By = "test",
            }, out _));
        });

        var charged = false;
        var wasInside = false;
        for (var i = 0; i < 240 && !charged; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var inside = server.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(borg);
                wasInside |= inside;
                var level = units.ChargeLevel(borg);
                charged = wasInside && !inside && level >= PulsarUnitsSystem.FullCharge - 0.01f;
                if (i % 10 == 0)
                    TestContext.Out.WriteLine($"{i}s level {level:0.00} inside {inside} task {ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Task?.Kind}/{ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Task?.Stage}");
            });
        }

        Assert.That(wasInside, "went into the recharging station");
        Assert.That(charged, "charged and came out");
        var reached = false;
        for (var i = 0; i < 60 && !reached; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() => reached = ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Order is { Arrived: true });
        }

        await StopWalking(ctx);
        Assert.That(reached, "carried on with the order");
        await pair.CleanReturnAsync();
    }

    private static async Task<EntityUid> Body(Ctx ctx, string name, int x, int y)
    {
        var mob = await Person(ctx, "PassengerIDCard", x, y);
        await NoAir(ctx, mob);
        await ctx.Pair.Server.WaitPost(() => ctx.Pair.Server.System<MetaDataSystem>().SetEntityName(mob, name));
        for (var i = 0; i < 12; i++)
        {
            var dead = false;
            await ctx.Pair.Server.WaitPost(() => dead = ctx.Pair.Server.System<MobStateSystem>().IsDead(mob));
            if (dead)
                break;
            await Hurt(ctx, mob, "Blunt", 40);
            await ctx.Pair.RunSeconds(0.5f);
        }
        await ctx.Pair.Server.WaitAssertion(() =>
            Assert.That(ctx.Pair.Server.System<MobStateSystem>().IsDead(mob), $"dead: {ctx.EntMan.GetComponent<DamageableComponent>(mob).TotalDamage} {ctx.Pair.Server.System<MobStateSystem>().IsCritical(mob)}"));
        return mob;
    }

    /// <summary>A stranger's body (not in the crew manifest) goes into the morgue tray.</summary>
    [Test]
    public async Task StrangerToMorgue()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Passenger");
        var server = pair.Server;
        await Walking(ctx);
        EntityUid morgue = default;
        await server.WaitPost(() => morgue = ctx.EntMan.SpawnEntity("Morgue", ctx.At(8, 6)));
        var body = await Body(ctx, "Неизвестный Гость", -2, -4);
        await LinkedUnit(ctx, "BorgChassisMedical", -5, -4);

        var inside = false;
        for (var i = 0; i < 120 && !inside; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
                inside = server.System<Robust.Shared.Containers.SharedContainerSystem>().TryGetContainingContainer(body, out var c) && c.Owner == morgue);
        }

        await StopWalking(ctx);
        Assert.That(inside, "the body is in the morgue tray");
        await pair.CleanReturnAsync();
    }

    /// <summary>A crew member's body (in the manifest) goes to the medical bed and the cyborg tries to revive them.</summary>
    [Test]
    public async Task CrewToMedbayAndRevived()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Passenger");
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();
        await Walking(ctx);
        EntityUid bed = default, morgue = default;
        await server.WaitPost(() =>
        {
            bed = ctx.EntMan.SpawnEntity("MedicalBed", ctx.At(8, 6));
            morgue = ctx.EntMan.SpawnEntity("Morgue", ctx.At(-8, 6));
            server.System<Content.Server.StationRecords.Systems.StationRecordsSystem>().AddRecordEntry(ctx.Station,
                new Content.Shared.StationRecords.GeneralStationRecord { Name = "Иван Петров", JobPrototype = "Passenger" });
        });
        var body = await Body(ctx, "Иван Петров", -2, -4);
        var borg = await LinkedUnit(ctx, "BorgChassisMedical", -5, -4);
        await server.WaitAssertion(() => Assert.That(units.IsCrew(ctx.Core, body), "in the manifest"));

        var nearBed = false;
        var alive = false;
        var xforms = server.System<SharedTransformSystem>();
        for (var i = 0; i < 180 && !alive; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                nearBed |= (xforms.GetWorldPosition(body) - xforms.GetWorldPosition(bed)).Length() < 2.5f;
                alive = !server.System<MobStateSystem>().IsDead(body);
                if (i % 10 == 0)
                    TestContext.Out.WriteLine($"{i}s dmg {ctx.EntMan.GetComponent<DamageableComponent>(body).TotalDamage} at {xforms.GetWorldPosition(body)} task {ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Task?.Kind}/{ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Task?.Stage}");
            });
        }

        await StopWalking(ctx);
        Assert.That(nearBed, "brought to the medical bed");
        await server.WaitAssertion(() => Assert.That(server.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(body), Is.False, "not in the morgue"));
        Assert.That(alive, "revived with the defibrillator");
        await pair.CleanReturnAsync();
    }

    /// <summary>Nicknames, compounds and declensions of the unit kinds; units by their own names.</summary>
    [Test]
    public async Task UnitNamesAndNicknames()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>().Index<Content.Shared._CorvaxGoob.Pulsar.PulsarLexiconPrototype>("Default");

        await server.WaitAssertion(() =>
        {
            HashSet<PulsarUnitKind> Kinds(string line) => pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize(line))?.Kinds ?? new();

            foreach (var line in new[] { "пришли мед юнит в бар", "пришли медборга в бар", "пришли мед борга в бар", "нужен медик в бар",
                         "пришли медика в бар", "отправь медицинского киборга в бар", "пришлите медборгов в бар" })
            {
                Assert.That(Kinds(line), Does.Contain(PulsarUnitKind.BorgMedical), line);
            }

            Assert.That(Kinds("пришли медика"), Does.Contain(PulsarUnitKind.Medibot), "a medic may be a medibot too");
            Assert.That(Kinds("пришли инжборга в бриг"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgEngineering }));
            Assert.That(Kinds("нужен инженерный юнит в бриг"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgEngineering }));
            Assert.That(Kinds("пришли инж борга"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgEngineering }));
            Assert.That(Kinds("пришли научборга к аномалии"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgScience }));
            Assert.That(Kinds("пришли научного юнита"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgScience }));
            Assert.That(Kinds("пришли сервисного борга в бар"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgService }));
            Assert.That(Kinds("пришли сервисборга в бар"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgService }));
            Assert.That(Kinds("пришли уборщика в бар"), Does.Contain(PulsarUnitKind.Cleanbot).And.Contain(PulsarUnitKind.BorgJanitor));
            Assert.That(Kinds("пришли чистобота"), Is.EquivalentTo(new[] { PulsarUnitKind.Cleanbot }));
            Assert.That(Kinds("пришли уборочного бота"), Is.EquivalentTo(new[] { PulsarUnitKind.Cleanbot }));
            Assert.That(Kinds("пришли полотера"), Is.EquivalentTo(new[] { PulsarUnitKind.Cleanbot }));
            Assert.That(Kinds("пришли пожарника в бар"), Is.EquivalentTo(new[] { PulsarUnitKind.Firebot }));
            Assert.That(Kinds("пришли пожарного бота"), Is.EquivalentTo(new[] { PulsarUnitKind.Firebot }));
            Assert.That(Kinds("пришли сварщика"), Is.EquivalentTo(new[] { PulsarUnitKind.Weldbot }));
            Assert.That(Kinds("пришли шахтерского борга"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgMining }));
            Assert.That(Kinds("пришли научного борга"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgScience }));
            Assert.That(Kinds("пришли инженерного борга"), Is.EquivalentTo(new[] { PulsarUnitKind.BorgEngineering }));
            Assert.That(Kinds("пришли пожарного"), Is.EquivalentTo(new[] { PulsarUnitKind.Firebot }));
            Assert.That(Kinds("пришли медицинского"), Does.Contain(PulsarUnitKind.BorgMedical));
            Assert.That(Kinds("пришли борга в медотсек"), Does.Not.Contain(PulsarUnitKind.Medibot), "medotsek is a place");
            Assert.That(Kinds("пришли мидибота сюда"), Is.EquivalentTo(new[] { PulsarUnitKind.Medibot }), "a typo");
            Assert.That(pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize("нужен медицинский осмотр")), Is.Null);
            Assert.That(pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize("открой медбей")), Is.Null);
            Assert.That(pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize("пришли медикаменты")), Is.Null);

            var a = new EntityUid(1001);
            var b = new EntityUid(1002);
            var c = new EntityUid(1003);
            var named = new List<(EntityUid, string, PulsarUnitKind)>
            {
                (a, "Келлер (Синт-7259)", PulsarUnitKind.BorgMedical),
                (b, "Бип (Синт-4151)", PulsarUnitKind.BorgEngineering),
                (c, "медибот (Синт-8711)", PulsarUnitKind.Medibot),
            };
            var r = pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize("пришли Келлера в бар"), named)!;
            Assert.That(r, Is.Not.Null);
            Assert.That(r.Units, Is.EquivalentTo(new[] { a }), "Келлер by name, «Синт» names nobody");
            Assert.That(r.PlaceTokens, Does.Contain("бар"));
            r = pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize("Келлер и медибот, в бар"), named)!;
            Assert.That(r.Units, Is.EquivalentTo(new[] { a }));
            Assert.That(r.Kinds, Does.Contain(PulsarUnitKind.Medibot));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A unit called by its name goes, not just the nearest one of its kind.</summary>
    [Test]
    public async Task SendsUnitByName()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain");
        var server = pair.Server;
        var near = await LinkedUnit(ctx, "BorgChassisMedical", 1, 1);
        var far = await LinkedUnit(ctx, "BorgChassisMedical", -9, -9);
        await server.WaitPost(() => server.System<MetaDataSystem>().SetEntityName(far, "Келлер"));
        var captain = await Person(ctx, "CaptainIDCard", 0, 2);
        var d = await Say(ctx, captain, "Пульсар, пришли Келлера сюда");
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsSent), d.Reply);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.GetComponent<PulsarLinkedComponent>(far).Order, Is.Not.Null, "Келлер got the order");
            Assert.That(ctx.EntMan.GetComponent<PulsarLinkedComponent>(near).Order, Is.Null, "the nearer one did not");
        });
        d = await Say(ctx, captain, "Пульсар, пришли медборга сюда");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsSent), d.Reply);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Point 5: an anomaly of the science generator is left alone; one that appeared by itself gets a science unit;
    /// a scientist can keep it («не трогай аномалию») and give it back, a passenger cannot.
    /// </summary>
    [Test]
    public async Task AnomaliesOfScienceAreLeftAlone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain", "ResearchDirector", "Scientist", "Passenger");
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();

        // A science unit: a chassis with the anomaly module.
        EntityUid borg = default;
        await server.WaitPost(() =>
        {
            borg = ctx.EntMan.SpawnEntity("BorgChassisGeneric", ctx.At(0, 3));
            var module = ctx.EntMan.SpawnEntity("BorgModuleAnomaly", ctx.At(0, 3));
            var chassis = ctx.EntMan.GetComponent<Content.Shared.Silicons.Borgs.Components.BorgChassisComponent>(borg);
            server.System<Content.Server.Silicons.Borgs.BorgSystem>().InsertModule((borg, chassis), module);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(units.TryLink(borg, ctx.Core, out var fail), fail));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
            Assert.That(ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg).Kind, Is.EqualTo(PulsarUnitKind.BorgScience)));

        // The generator finishes: its anomaly is marked as science's own.
        EntityUid generated = default;
        await server.WaitPost(() =>
        {
            var gen = ctx.EntMan.SpawnEntity("MachineAnomalyGenerator", ctx.At(-8, -8));
            // Generating with the end time already passed (zero): it finishes on the next update.
            ctx.EntMan.AddComponent<Content.Server.Anomaly.Components.GeneratingAnomalyGeneratorComponent>(gen);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var q = ctx.EntMan.EntityQueryEnumerator<Content.Shared.Anomaly.Components.AnomalyComponent>();
            while (q.MoveNext(out var uid, out _))
                generated = uid;
            Assert.That(generated.Valid, "the generator made an anomaly");
            Assert.That(ctx.EntMan.TryGetComponent<PulsarScienceAnomalyComponent>(generated, out var mark) && mark.Generated,
                "a generated anomaly is science's own");
            Assert.That(units.LeftToScience(generated));
            // Out of the way of «эту аномалию» below.
            server.System<SharedTransformSystem>().SetCoordinates(generated, ctx.At(-11, -11));
        });

        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            var linked = ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg);
            Assert.That(linked.Task?.Target, Is.Not.EqualTo(generated), "no unit for the generated anomaly");
            Assert.That(linked.Order?.Focus, Is.Not.EqualTo(generated));
        });

        // One that appeared by itself: the science unit takes it.
        EntityUid wild = default;
        await server.WaitPost(() => wild = ctx.EntMan.SpawnEntity("AnomalyFlora", ctx.At(6, 6)));
        var taken = false;
        for (var i = 0; i < 10 && !taken; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var linked = ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg);
                taken = linked.Task?.Target == wild || linked.Order?.Focus == wild;
            });
        }
        Assert.That(taken, "a science unit goes to a wild anomaly");

        // A passenger cannot keep it.
        var passenger = await Person(ctx, "PassengerIDCard", 5, 6);
        var d = await Say(ctx, passenger, "Пульсар, не трогай аномалию");
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Denied), d.Reply);
        Assert.That(d.Reply, Does.Contain("научн"), "in character: science decides");

        // A scientist can.
        var scientist = await Person(ctx, "ResearchIDCard", 6, 5);
        d = await Say(ctx, scientist, "Пульсар, не трогай эту аномалию, она нам нужна");
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.AnomalyKept), d.Reply);
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(units.LeftToScience(wild));
            var linked = ctx.EntMan.GetComponent<PulsarLinkedComponent>(borg);
            Assert.That(linked.Task?.Target, Is.Not.EqualTo(wild), "the unit leaves the kept anomaly");
            Assert.That(linked.Order?.Focus, Is.Not.EqualTo(wild));
        });

        // And gives it back.
        d = await Say(ctx, scientist, "Пульсар, аномалию можно нейтрализовать");
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.AnomalyReleased), d.Reply);
        await server.WaitAssertion(() => Assert.That(units.LeftToScience(wild), Is.False));
        await server.WaitAssertion(() => Assert.That(units.LeftToScience(generated), "the far generated one stays science's"));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Point 3: a job of the module's own goes to a much nearer free unit when one turns up, and two medical units
    /// never take the same patient.
    /// </summary>
    [Test]
    public async Task TasksAreSharedAndRebalanced()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Passenger");
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();
        var p1 = await Person(ctx, "PassengerIDCard", 9, 0);
        var p2 = await Person(ctx, "PassengerIDCard", 9, 4);
        foreach (var p in new[] { p1, p2 })
        {
            await NoAir(ctx, p);
            await Hurt(ctx, p, "Blunt", 30);
        }

        // Far unit sent first.
        var far = await LinkedUnit(ctx, "BorgChassisMedical", -10, 0);
        await server.WaitPost(() =>
            Assert.That(units.Dispatch(ctx.Core, PulsarNeed.Patient, ctx.EntMan.GetComponent<TransformComponent>(p1).Coordinates, "тест", p1),
                Is.EqualTo(1)));
        await server.WaitAssertion(() =>
            Assert.That(ctx.EntMan.GetComponent<PulsarLinkedComponent>(far).Order?.Focus, Is.EqualTo(p1)));

        // A free one right next to the patient: it takes the job over, the far one is free again.
        var near = await LinkedUnit(ctx, "BorgChassisMedical", 7, 0);
        var moved = false;
        for (var i = 0; i < 8 && !moved; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var n = ctx.EntMan.GetComponent<PulsarLinkedComponent>(near);
                moved = n.Order?.Focus == p1 || n.Task?.Target == p1;
            });
        }

        Assert.That(moved, "the near unit took the patient over");
        await server.WaitAssertion(() =>
        {
            var f = ctx.EntMan.GetComponent<PulsarLinkedComponent>(far);
            Assert.That(f.Order?.Focus, Is.Not.EqualTo(p1), "the far unit was released");
            Assert.That(f.Task?.Target, Is.Not.EqualTo(p1));
        });

        // Two units, two patients: never the same one.
        for (var i = 0; i < 10; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitAssertion(() =>
            {
                var a = ctx.EntMan.GetComponent<PulsarLinkedComponent>(near);
                var b = ctx.EntMan.GetComponent<PulsarLinkedComponent>(far);
                var ta = a.Task?.Target ?? a.Order?.Focus;
                var tb = b.Task?.Target ?? b.Order?.Focus;
                if (ta != null && tb != null)
                    Assert.That(ta, Is.Not.EqualTo(tb), "one patient per unit");
            });
        }

        await pair.CleanReturnAsync();
    }
}
