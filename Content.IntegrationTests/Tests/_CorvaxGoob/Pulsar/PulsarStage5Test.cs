using System.Collections.Generic;
using System.Linq;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Server._CorvaxGoob.Pulsar.Units;
using Content.Server.NPC.HTN;
using Content.Shared._CorvaxGoob.Pulsar;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mind;
using Content.Shared.NPC;
using Content.Shared.Robotics;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._CorvaxGoob.Pulsar.PulsarRequestsTest;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 5: units linked to the AI (minibots and mindless cyborgs), requests with counts,
/// order priorities, anchoring, the robotics console data.
/// </summary>
[TestFixture]
[TestOf(typeof(PulsarUnitsSystem))]
public sealed class PulsarStage5Test
{
    private static async Task<EntityUid> Spawn(Ctx ctx, string proto, int x, int y)
    {
        EntityUid uid = default;
        await ctx.Pair.Server.WaitPost(() => uid = ctx.EntMan.SpawnEntity(proto, ctx.At(x, y)));
        return uid;
    }

    private static async Task Link(Ctx ctx, EntityUid unit, bool expect = true)
    {
        var units = ctx.Pair.Server.System<PulsarUnitsSystem>();
        await ctx.Pair.Server.WaitAssertion(() =>
            Assert.That(units.TryLink(unit, ctx.Core, out var fail), Is.EqualTo(expect), fail));
        await ctx.Pair.RunTicksSync(2);
    }

    private static Entity<PulsarLinkedComponent> Linked(Ctx ctx, EntityUid uid) => (uid, ctx.EntMan.GetComponent<PulsarLinkedComponent>(uid));

    [Test]
    public async Task UnitRequestParsing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>().Index<PulsarLexiconPrototype>("Default");

        await server.WaitAssertion(() =>
        {
            PulsarAutonomousAiSystem.UnitRequest Parse(string line) => pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize(line))!;

            var r = Parse("Пульсар, пришли двух медиботов в медбей");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.Medibot }));
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r.PlaceTokens, Does.Contain("медбей"));
            Assert.That(r.PlaceTokens, Does.Not.Contain("медиботов"));

            r = Parse("закрепи инженерного борга в инженерном отделе");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.BorgEngineering }));
            Assert.That(r.Count, Is.EqualTo(0), "no count = one");
            Assert.That(r.PlaceTokens, Does.Contain("инженерном"), "the place keeps its own «инженерном»");

            r = Parse("отправь пару пожарных ботов сюда");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.Firebot }));
            Assert.That(r.Count, Is.EqualTo(2));

            r = Parse("отзови всех чистоботов");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.Cleanbot }));
            Assert.That(r.Count, Is.EqualTo(int.MaxValue));

            r = Parse("пришли 3 сваркобота в бар");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.Weldbot }));
            Assert.That(r.Count, Is.EqualTo(3));

            r = Parse("нужен медицинский юнит в химлабораторию");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.BorgMedical }));

            r = Parse("пришлите научнаго борга к аномалии");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.BorgScience }), "typo in the adjective");

            r = Parse("пришли мидибота сюда");
            Assert.That(r.Kinds, Is.EquivalentTo(new[] { PulsarUnitKind.Medibot }), "typo in the noun");

            r = Parse("пришли юнита сюда");
            Assert.That(r.Kinds, Does.Contain(PulsarUnitKind.BorgEngineering).And.Contain(PulsarUnitKind.BorgMedical), "any unit");

            Assert.That(pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize("открой медбей")), Is.Null);
            Assert.That(pulsar.ParseUnitRequest(proto, PulsarSpeech.Tokenize("где капитан")), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PriorityRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        var pulsar = server.System<PulsarAutonomousAiSystem>();
        var units = server.System<PulsarUnitsSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>().Index<PulsarLexiconPrototype>("Default");

        await server.WaitAssertion(() =>
        {
            int Rank(string? job, PulsarUnitKind kind) => pulsar.UnitRank(proto, job, kind, ctx.Station);

            Assert.That(Rank("Captain", PulsarUnitKind.Medibot), Is.EqualTo(PulsarOrderRank.Captain));
            Assert.That(Rank("ChiefEngineer", PulsarUnitKind.Medibot), Is.EqualTo(PulsarOrderRank.Head), "heads over all units");
            Assert.That(Rank("StationEngineer", PulsarUnitKind.Weldbot), Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(Rank("StationEngineer", PulsarUnitKind.BorgEngineering), Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(Rank("StationEngineer", PulsarUnitKind.Medibot), Is.EqualTo(PulsarOrderRank.Crew));
            Assert.That(Rank("MedicalDoctor", PulsarUnitKind.Medibot), Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(Rank("MedicalDoctor", PulsarUnitKind.BorgMedical), Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(Rank("Janitor", PulsarUnitKind.Cleanbot), Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(Rank("Bartender", PulsarUnitKind.BorgService), Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(Rank("Chef", PulsarUnitKind.BorgJanitor), Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(Rank("Passenger", PulsarUnitKind.Cleanbot), Is.EqualTo(PulsarOrderRank.Crew), "an assistant is not service");
            Assert.That(Rank("SecurityOfficer", PulsarUnitKind.Medibot), Is.EqualTo(PulsarOrderRank.Crew), "green: security is crew");
            Assert.That(Rank(null, PulsarUnitKind.Medibot), Is.EqualTo(PulsarOrderRank.Crew));
        });

        await SetAlert(ctx, "red");
        await server.WaitAssertion(() =>
        {
            int Rank(string? job, PulsarUnitKind kind) => pulsar.UnitRank(proto, job, kind, ctx.Station);
            Assert.That(Rank("SecurityOfficer", PulsarUnitKind.Medibot), Is.EqualTo(PulsarOrderRank.RedAlertSecurity));
            Assert.That(Rank("SecurityOfficer", PulsarUnitKind.Medibot), Is.GreaterThan(Rank("ChiefMedicalOfficer", PulsarUnitKind.Medibot)), "red: security over heads");
            Assert.That(Rank("Captain", PulsarUnitKind.Medibot), Is.GreaterThan(Rank("SecurityOfficer", PulsarUnitKind.Medibot)), "the captain is highest");
        });

        // Orders: a lower rank cannot override, equal or higher can.
        var bot = await Spawn(ctx, "MobMedibot", 6, 6);
        await Link(ctx, bot);
        await server.WaitAssertion(() =>
        {
            var link = Linked(ctx, bot);
            PulsarUnitOrder Order(int rank, bool assigned = false) =>
                new() { Target = ctx.At(0, 0), Place = "x", Rank = rank, Assigned = assigned, By = rank.ToString() };

            Assert.That(units.TryOrder(link, Order(PulsarOrderRank.Department, assigned: true), out _), Is.True);
            Assert.That(units.TryOrder(link, Order(PulsarOrderRank.Crew), out var blocking), Is.False, "crew cannot override department staff");
            Assert.That(blocking!.Rank, Is.EqualTo(PulsarOrderRank.Department));
            Assert.That(units.TryOrder(link, Order(PulsarOrderRank.Autonomous), out _), Is.False, "the AI's own dispatch is lowest");
            Assert.That(units.TryRelease(link, PulsarOrderRank.Crew, out _), Is.False);
            Assert.That(units.TryOrder(link, Order(PulsarOrderRank.Head), out _), Is.True, "a head overrides");
            Assert.That(link.Comp.Order!.Previous, Is.Not.Null, "the visit remembers the assignment");
            Assert.That(units.TryOrder(link, Order(PulsarOrderRank.Department), out _), Is.False, "the head's order now blocks staff");
            Assert.That(units.TryRelease(link, PulsarOrderRank.Captain, out _), Is.True);
            Assert.That(link.Comp.Order, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LinkUnlinkAndAnchoring()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();
        var xforms = server.System<SharedTransformSystem>();

        var medibot = await Spawn(ctx, "MobMedibot", 5, 5);
        var cleanbot = await Spawn(ctx, "MobCleanBot", 6, 5);
        var firebot = await Spawn(ctx, "MobFireBot", 7, 5);
        var weldbot = await Spawn(ctx, "MobWeldbot", 8, 5);
        var human = await Spawn(ctx, "MobHuman", 9, 5);

        await server.WaitAssertion(() =>
        {
            Assert.That(units.GetKind(medibot), Is.EqualTo(PulsarUnitKind.Medibot));
            Assert.That(units.GetKind(cleanbot), Is.EqualTo(PulsarUnitKind.Cleanbot));
            Assert.That(units.GetKind(firebot), Is.EqualTo(PulsarUnitKind.Firebot));
            Assert.That(units.GetKind(weldbot), Is.EqualTo(PulsarUnitKind.Weldbot));
            Assert.That(units.GetKind(human), Is.Null);
            Assert.That(units.TryLink(human, ctx.Core, out var fail), Is.False);
            Assert.That(fail, Is.EqualTo("robotics-console-pulsar-fail-kind"));
        });

        // The four minibot HTNs go to the AI's order first.
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        foreach (var root in new[] { "MedibotCompound", "WeldbotCompound", "FirebotCompound", "CleanbotCompound" })
        {
            var compound = protoMan.Index<HTNCompoundPrototype>(root);
            var first = compound.Branches[0].Tasks[0] as HTNCompoundTask;
            Assert.That(first?.Task.Id, Is.EqualTo("PulsarGotoCompound"), root);
        }

        await Link(ctx, medibot);
        await Link(ctx, cleanbot);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.HasComponent<PulsarLinkedComponent>(medibot));
            Assert.That(units.LinkedUnits(ctx.Core).Count(), Is.EqualTo(2));
            Assert.That(units.Controllable(Linked(ctx, medibot)), Is.True);
            units.Unlink(cleanbot);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(ctx.EntMan.HasComponent<PulsarLinkedComponent>(cleanbot), Is.False, "unlinked"));

        // Wrenched down: the AI lets go.
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.HasComponent<Content.Shared.Construction.Components.AnchorableComponent>(firebot), "firebots are anchorable now");
            Assert.That(ctx.EntMan.HasComponent<Content.Shared.Construction.Components.AnchorableComponent>(cleanbot), "cleanbots are anchorable now");
            xforms.AnchorEntity(medibot);
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.GetComponent<TransformComponent>(medibot).Anchored);
            Assert.That(ctx.EntMan.HasComponent<PulsarLinkedComponent>(medibot), Is.False, "anchoring unlinks");
            Assert.That(units.TryLink(medibot, ctx.Core, out var fail), Is.False);
            Assert.That(fail, Is.EqualTo("robotics-console-pulsar-fail-anchored"));
            xforms.Unanchor(medibot);
        });
        await pair.RunTicksSync(2);
        await Link(ctx, medibot);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RequestsWithCountsAndPriority()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain", "ChiefEngineer", "MedicalDoctor", "Janitor", "Passenger");
        var server = pair.Server;

        var medibots = new List<EntityUid>();
        for (var i = 0; i < 3; i++)
            medibots.Add(await Spawn(ctx, "MobMedibot", 8, 6 + i));
        var cleanbot = await Spawn(ctx, "MobCleanBot", 8, 10);
        foreach (var bot in medibots.Append(cleanbot))
            await Link(ctx, bot);

        var medic = await Person(ctx, "MedicalIDCard", 0, 2);
        var passenger = await Person(ctx, "PassengerIDCard", 1, 2);
        var ce = await Person(ctx, "CEIDCard", -1, 2);
        var janitor = await Person(ctx, "JanitorIDCard", 2, 2);

        var d = await Say(ctx, medic, "Пульсар, пришли двух медиботов сюда");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsSent), d.Reply);
        var ordered = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            ordered = medibots.Where(b => Linked(ctx, b).Comp.Order != null).ToList();
            Assert.That(ordered, Has.Count.EqualTo(2), "exactly two");
            Assert.That(ordered.All(b => Linked(ctx, b).Comp.Order!.Rank == PulsarOrderRank.Department));
        });

        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            var htn = ctx.EntMan.GetComponent<HTNComponent>(ordered[0]);
            Assert.That(htn.Blackboard.ContainsKey(PulsarUnitsSystem.GotoKey), "the HTN got the AI's target");
            Assert.That(htn.Blackboard.TryGetValue<bool>(Content.Server.NPC.NPCBlackboard.NavAccess, out var access, ctx.EntMan) && access,
                "paths through access doors (the AI opens them)");
        });

        // More than there are: the ones available go and the reply says so.
        d = await Say(ctx, ce, "Пульсар, пришли пять медиботов в каюту капитана");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsSent), d.Reply);
        Assert.That(d.Reply, Does.Contain("Доступно только 3"));

        // An assistant cannot recall what a head ordered.
        d = await Say(ctx, passenger, "Пульсар, отзови медиботов");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsDenied), d.Reply);
        Assert.That(d.Reply, Does.Contain("Отменить его не могу"));

        d = await Say(ctx, medic, "Пульсар, пришли медибота сюда");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsDenied), "staff below a head: refused");

        d = await Say(ctx, ce, "Пульсар, отзови всех медиботов");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsReleased), d.Reply);

        // Assignment.
        d = await Say(ctx, janitor, "Пульсар, закрепи чистобота за каютой капитана");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsAssigned), d.Reply);
        await server.WaitAssertion(() => Assert.That(Linked(ctx, cleanbot).Comp.Order!.Assigned));

        // Nothing linked of that kind; no place.
        d = await Say(ctx, medic, "Пульсар, пришли пожарбота сюда");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsDenied));
        Assert.That(d.Reply, Does.Contain("Нет подключённых"));
        d = await Say(ctx, medic, "Пульсар, пришли медибота");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.AskTarget));

        d = await Say(ctx, medic, "Пульсар, какие медиботы подключены");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Answered), d.Reply);
        Assert.That(d.Reply, Does.Contain("Подключено к ЦИИ: 3"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MindlessBorgsOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "MedicalDoctor");
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();
        var minds = server.System<SharedMindSystem>();
        var damage = server.System<DamageableSystem>();

        var borg = await Spawn(ctx, "BorgChassisMedical", 5, 0);
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(units.GetKind(borg), Is.EqualTo(PulsarUnitKind.BorgMedical));
            var chassis = ctx.EntMan.GetComponent<BorgChassisComponent>(borg);
            Assert.That(units.CanHeal((borg, chassis)), "first aid module");
            Assert.That(chassis.Active, Is.False, "a mindless chassis is off");
        });

        await Link(ctx, borg);
        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.GetComponent<BorgChassisComponent>(borg).Active, "the AI switches it on");
            Assert.That(ctx.EntMan.HasComponent<ActiveNPCComponent>(borg), "driven by NPC steering");
        });

        // An injured person next to it: it goes to heal them.
        var patient = await Person(ctx, "PassengerIDCard", 7, 0);
        await server.WaitPost(() =>
        {
            var brute = new DamageSpecifier(server.ResolveDependency<IPrototypeManager>().Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt"), 40);
            damage.TryChangeDamage(patient, brute, ignoreResistances: true, canMiss: false);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var link = Linked(ctx, borg);
            Assert.That(units.NeedsHealing(patient), "patient is hurt");
            Assert.That(link.Comp.Task?.Kind, Is.EqualTo(PulsarBorgTaskKind.Heal), "heal task");
            Assert.That(link.Comp.Task!.Target, Is.EqualTo(patient));
        });

        // A person takes the borg: the AI lets go at once.
        await server.WaitPost(() =>
        {
            var mind = minds.CreateMind(null, "test");
            minds.TransferTo(mind, borg);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var link = Linked(ctx, borg);
            Assert.That(units.HasMind(borg));
            Assert.That(units.Controllable(link), Is.False);
            Assert.That(link.Comp.Task, Is.Null);
            Assert.That(link.Comp.Driving, Is.False);
            Assert.That(ctx.EntMan.HasComponent<ActiveNPCComponent>(borg), Is.False);
        });

        // Ordered by speech: only asked on the radio, never driven.
        var medic = await Person(ctx, "MedicalIDCard", 0, 2);
        var d = await Say(ctx, medic, "Пульсар, пришли медицинского юнита сюда");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.UnitsSent), d.Reply);
        await server.WaitAssertion(() => Assert.That(Linked(ctx, borg).Comp.Order, Is.Null, "a borg with a mind gets no order"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoboticsConsoleData()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var server = pair.Server;
        var units = server.System<PulsarUnitsSystem>();

        var console = await Spawn(ctx, "ComputerRoboticsControl", -5, -5);
        var medibot = await Spawn(ctx, "MobMedibot", 5, 5);
        var cleanbot = await Spawn(ctx, "MobCleanBot", 6, 5);
        await Link(ctx, medibot);

        await server.WaitAssertion(() =>
        {
            var state = new RoboticsConsoleState(new Dictionary<string, CyborgControlData>(), true, false);
            units.FillConsoleState(console, state, new Dictionary<string, EntityUid>());
            Assert.That(state.PulsarAvailable);
            Assert.That(state.Minibots, Has.Count.EqualTo(2));
            var m = state.Minibots.Single(b => b.Entity == ctx.EntMan.GetNetEntity(medibot));
            Assert.That(m.Linked);
            Assert.That(m.Kind, Is.EqualTo("медибот"));
            Assert.That(state.Minibots.Single(b => b.Entity == ctx.EntMan.GetNetEntity(cleanbot)).Linked, Is.False);

            Assert.That(units.ConsoleLink(console, cleanbot, true, out _));
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.HasComponent<PulsarLinkedComponent>(cleanbot), "linked from the console");
            Assert.That(units.ConsoleLink(console, cleanbot, false, out _));
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(ctx.EntMan.HasComponent<PulsarLinkedComponent>(cleanbot), Is.False, "unlinked from the console"));

        await pair.CleanReturnAsync();
    }
}
