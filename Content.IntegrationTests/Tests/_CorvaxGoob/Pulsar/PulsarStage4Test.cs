using System.Collections.Generic;
using System.Linq;
using Content.Server._CorvaxGoob.Pulsar;
using Content.Shared.Doors.Components;
using Content.Shared.Pointing;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._CorvaxGoob.Pulsar.PulsarRequestsTest;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 4: which door (pointing, names, "this", directions, clarifying questions),
/// announcements, flexible understanding and small talk.
/// </summary>
[TestFixture]
[TestOf(typeof(PulsarAutonomousAiSystem))]
public sealed class PulsarStage4Test
{
    private static async Task Point(Ctx ctx, EntityUid who, EntityUid door)
    {
        await ctx.Pair.Server.WaitPost(() =>
        {
            var ev = new AfterGotPointedAtEvent(who);
            ctx.EntMan.EventBus.RaiseLocalEvent(door, ref ev);
        });
        await ctx.Pair.RunTicksSync(3);
    }

    private static async Task<EntityUid> Door(Ctx ctx, string proto, int x, int y, string? name = null)
    {
        EntityUid door = default;
        var power = ctx.Pair.Server.System<SharedPowerReceiverSystem>();
        var meta = ctx.Pair.Server.System<MetaDataSystem>();
        await ctx.Pair.Server.WaitPost(() =>
        {
            door = ctx.EntMan.SpawnEntity(proto, ctx.At(x, y));
            power.SetNeedsPower(door, false);
            if (name != null)
                meta.SetEntityName(door, name);
        });
        return door;
    }

    private static async Task Refresh(Ctx ctx)
    {
        var pulsar = ctx.Pair.Server.System<PulsarAutonomousAiSystem>();
        await ctx.Pair.RunTicksSync(2);
        await ctx.Pair.Server.WaitPost(() => pulsar.RefreshNow(ctx.Pulsar));
        await ctx.Pair.RunSeconds(2);
    }

    private static PulsarDecision Last(Ctx ctx) => ctx.Pulsar.Comp.Decisions[^1];

    [Test]
    public void Matcher()
    {
        // Pure text helpers: no server needed, but kept with the other Pulsar tests.
        Assert.That(PulsarSpeech.Distance("пульсар", "пульсра"), Is.EqualTo(1), "swapped letters are one typo");
        Assert.That(PulsarSpeech.Distance("пульсар", "пулсар"), Is.EqualTo(1));
        Assert.That(PulsarSpeech.StemRu("медотсеке"), Is.EqualTo("медотсек"));
        Assert.That(PulsarSpeech.StemRu("химлаборатории"), Is.EqualTo("химлаборатор"));
        Assert.That(PulsarSpeech.SameWord("химлаборатории", "хим"), Is.True, "abbreviation");
        Assert.That(PulsarSpeech.SameWord("медотсеке", "медотсек"), Is.True, "case ending");
        Assert.That(PulsarSpeech.SameWord("медотсек", "хим"), Is.False);

        var tokens = PulsarSpeech.Tokenize("Ёлки, «КАПИТАНА каюту» открой!!!");
        Assert.That(tokens, Is.EqualTo(new[] { "елки", "капитана", "каюту", "открой" }), "lower case, ё→е, no punctuation");
        Assert.That(new PulsarSpeech.Phrase("кают капитан").Find(tokens), Is.GreaterThanOrEqualTo(0), "any word order");
        Assert.That(new PulsarSpeech.Phrase("оружейн").Find(PulsarSpeech.Tokenize("аружейную")), Is.EqualTo(0), "typo in a keyword");
        Assert.That(new PulsarSpeech.Phrase("откр").Find(PulsarSpeech.Tokenize("аткрой")), Is.EqualTo(0), "typo in a short verb");
        Assert.That(new PulsarSpeech.Phrase("откр").Find(PulsarSpeech.Tokenize("откос")), Is.EqualTo(-1));
        Assert.That(new PulsarSpeech.Phrase("откр").Find(PulsarSpeech.Tokenize("закрой")), Is.EqualTo(-1));
    }

    [Test]
    public async Task NamesAndTypos()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var pulsar = pair.Server.System<PulsarAutonomousAiSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            string[] yes = { "Пулсар, открой", "ПУЛЬСРА!", "пульсик", "Пульсарик, привет", "иишка", "ИИшечка", "Пульсару", "пульсаар", "ПyЛЬСAР" };
            string[] no = { "пульсация в норме", "пульсирует", "пульсе", "Пульс", "линии", "открой дверь" };
            foreach (var line in yes)
                Assert.That(pulsar.IsAddressed(ctx.Pulsar, line), Is.True, line);
            foreach (var line in no)
                Assert.That(pulsar.IsAddressed(ctx.Pulsar, line), Is.False, line);
        });

        var captain = await Person(ctx, "CaptainIDCard", 0, 2);

        // Typos, case, word order.
        var d = await Say(ctx, captain, "ПУЛСАР, АТКРОЙ КАЮТУ КАПИТНА");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(ctx.CaptainDoor));
        await Close(ctx, ctx.CaptainDoor);
        await pair.RunSeconds(2);

        d = await Say(ctx, captain, "Пульсар... оружейную, открой-ка, пожалуйста!");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(ctx.ArmoryDoor));
        await pair.RunSeconds(2);

        // Synonyms: «дай пройти», «закрой».
        d = await Say(ctx, captain, "Пульсар, закрой оружейную");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Closed), d.Reason);
        await pair.RunSeconds(2);
        d = await Say(ctx, captain, "Пульсар, дай пройти в оружейную");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TargetByPointing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain");
        var captain = await Person(ctx, "CaptainIDCard", -1, 2);

        // Pointed at the captain's door (2.8 tiles away, not "next to" them), then «открой эту дверь».
        await Point(ctx, captain, ctx.CaptainDoor);
        Assert.That(ctx.Pulsar.Comp.RecentPoints.ContainsKey(captain), "the pointing was seen on camera");
        var d = await Say(ctx, captain, "Пульсар, открой эту дверь");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(ctx.CaptainDoor));
        Assert.That(await IsOpen(ctx, ctx.CaptainDoor), Is.True);
        await Close(ctx, ctx.CaptainDoor);

        // Asked first with nothing nearby: Pulsar asks to point, the pointing right after answers it.
        await pair.RunSeconds(10);
        var far = await Person(ctx, "CaptainIDCard", 0, 5);
        d = await Say(ctx, far, "Пульсар, открой вот эту");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.AskTarget), d.Reason);
        Assert.That(d.Reply, Does.Contain("Укажите"));
        await pair.RunSeconds(2);
        await Point(ctx, far, ctx.ArmoryDoor);
        d = Last(ctx);
        Assert.That(d.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(ctx.ArmoryDoor));
        Assert.That(await IsOpen(ctx, ctx.ArmoryDoor), Is.True);

        // Pointing does not skip the rules: a passenger pointing at the captain's door is refused.
        var passenger = await Person(ctx, "PassengerIDCard", -1, 1);
        await Point(ctx, passenger, ctx.CaptainDoor);
        d = await Say(ctx, passenger, "Пульсар, открой вон ту дверь");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Denied));
        Assert.That(d.Reply, Does.Contain("ID-карты"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TargetByNameAndDirection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain");
        var morgue = await Door(ctx, "Airlock", 0, -4, "шлюз морга");
        await Refresh(ctx);

        // The door's own name.
        var passenger = await Person(ctx, "PassengerIDCard", 1, 1);
        var d = await Say(ctx, passenger, "Пульсар, открой морг");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(morgue));
        await Close(ctx, morgue);

        // Station directions: the captain door is west of the speaker, the armory east.
        var captain = await Person(ctx, "CaptainIDCard", 0, 1);
        var xforms = pair.Server.System<SharedTransformSystem>();
        await pair.Server.WaitPost(() => xforms.SetLocalRotation(captain, Angle.Zero)); // faces south
        d = await Say(ctx, captain, "Пульсар, открой западный шлюз");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(ctx.CaptainDoor));
        await pair.RunSeconds(2);

        // Relative: facing south, the left hand is east.
        d = await Say(ctx, captain, "Пульсар, открой левую дверь");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(ctx.ArmoryDoor));
        await pair.RunSeconds(2);

        // «Передо мной»: two doors at the same distance, the one in front (south) wins.
        var front = await Door(ctx, "Airlock", 0, -1);
        var behind = await Door(ctx, "Airlock", 0, 3);
        await Refresh(ctx);
        d = await Say(ctx, captain, "Пульсар, открой дверь передо мной");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(front));
        Assert.That(d.Door, Is.Not.EqualTo(behind));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AmbiguousClarification()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair);
        var west = await Door(ctx, "AirlockMedicalLocked", -4, -4);
        var east = await Door(ctx, "AirlockMedicalLocked", 4, -4);
        await pair.Server.WaitPost(() =>
        {
            // Stage 5.3: a door at the «Медотсек» beacon itself would be the obvious one; two other places tie.
            ctx.EntMan.SpawnEntity("DefaultStationBeaconCryonics", ctx.At(-6, -6));
            ctx.EntMan.SpawnEntity("DefaultStationBeaconChemistry", ctx.At(6, -6));
        });
        await Refresh(ctx);

        var doctor = await Person(ctx, "MedicalIDCard", -1, 5);
        var d = await Say(ctx, doctor, "Пульсар, открой медбей");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Clarify), d.Reason);
        Assert.That(d.Reply, Does.StartWith("Уточните:").And.Contain("Криокапсулы").And.Contain("Хим. лаборатория").And.Contain(" или "));

        // The answer without the name, by place (abbreviated, another case).
        await pair.RunSeconds(2);
        d = await Say(ctx, doctor, "у химлаборатории");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(east));
        await Close(ctx, east);

        // By position in the list.
        d = await Say(ctx, doctor, "Пульсар, открой медбей");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Clarify));
        await pair.RunSeconds(2);
        d = await Say(ctx, doctor, "первый");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(west), "the nearest one is listed first");
        await Close(ctx, west);

        // By pointing.
        d = await Say(ctx, doctor, "Пульсар, открой медбей");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Clarify));
        await pair.RunSeconds(2);
        await Point(ctx, doctor, east);
        Assert.That(Last(ctx).Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened));
        Assert.That(Last(ctx).Door, Is.EqualTo(east));
        await Close(ctx, east);

        // Next to one of them: no question.
        var near = await Person(ctx, "MedicalIDCard", 3, -3);
        d = await Say(ctx, near, "Пульсар, открой медбей");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), d.Reason);
        Assert.That(d.Door, Is.EqualTo(east));

        // The question expires after 30 s (the pointing above is forgotten after 10 s).
        await Close(ctx, east);
        await pair.RunSeconds(10);
        d = await Say(ctx, doctor, "Пульсар, открой медбей");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Clarify));
        await pair.RunSeconds(31);
        Assert.That(await Say(ctx, doctor, "первый"), Is.Null, "too late, and the conversation is over");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Announcements()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain", "ResearchDirector");
        var loc = pair.Server.ResolveDependency<ILocalizationManager>();
        var a = ctx.Pulsar.Comp.Announcements;

        // The activation greeting, light blue, signed by the module.
        Assert.That(a, Has.Count.EqualTo(1));
        Assert.That(a[0].Reason, Is.EqualTo("greeting-activation"));
        Assert.That(a[0].Sender, Is.EqualTo("А.М.Ц.И.И Пульсар"));
        Assert.That(a[0].Sender, Is.Not.EqualTo(loc.GetString("admin-announce-announcer-default")));
        Assert.That(a[0].Color.B, Is.GreaterThan(a[0].Color.R), "light blue");
        Assert.That(a[0].Color.G, Is.GreaterThan(a[0].Color.R));

        // Its reaction to the alert level.
        await SetAlert(ctx, "red");
        await pair.RunTicksSync(2);
        Assert.That(a[^1].Reason, Is.EqualTo("alert-level:red"));
        Assert.That(a[^1].Text, Does.Contain("Код красный"));

        var captain = await Person(ctx, "CaptainIDCard", 0, 2);
        var rd = await Person(ctx, "RDIDCard", 1, 2);
        var passenger = await Person(ctx, "PassengerIDCard", -1, 2);

        var d = await Say(ctx, passenger, "Пульсар, объяви всем: бесплатная раздача в баре");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Denied));
        Assert.That(d.Reason, Is.EqualTo("announce-not-head"));

        // Asked what to announce, the next line is the text.
        d = await Say(ctx, captain, "Пульсар, сделай объявление");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.AskTarget), d.Reason);
        Assert.That(d.Reply, Is.EqualTo("Что объявить?"));
        await pair.RunSeconds(2);
        d = await Say(ctx, captain, "сбор глав на мостике через пять минут");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Announced), d.Reason);
        Assert.That(a[^1].Text, Is.EqualTo("Сбор глав на мостике через пять минут"));
        Assert.That(a[^1].Reason, Does.StartWith("request:"));

        // Too long, then the cooldown.
        await pair.RunSeconds(2);
        d = await Say(ctx, rd, "Пульсар, объяви: " + string.Concat(Enumerable.Repeat("очень длинный текст ", 20)));
        Assert.That(d!.Reason, Is.EqualTo("announce-too-long"));
        await pair.RunSeconds(2);
        d = await Say(ctx, rd, "Пульсар, объяви, что шлюзы научного отдела закрыты");
        Assert.That(d!.Reason, Is.EqualTo("announce-cooldown"), "one request announcement a minute; «закрыты» is not a close request");
        Assert.That(d.Reply, Does.Contain("через"));
        await pair.RunSeconds(60);
        d = await Say(ctx, rd, "Пульсар, объяви, что шлюзы научного отдела закрыты");
        Assert.That(d!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Announced), d.Reason);
        Assert.That(a[^1].Text, Is.EqualTo("Шлюзы научного отдела закрыты"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SmallTalk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain", "Passenger");
        var loc = pair.Server.ResolveDependency<ILocalizationManager>();
        var p = await Person(ctx, "PassengerIDCard", 0, 2);

        string[] Variants(string prefix, int n) => Enumerable.Range(1, n).Select(i => loc.GetString($"{prefix}-{i}")).ToArray();

        async Task<PulsarDecision> Talk(string text)
        {
            var d = await Say(ctx, p, text);
            Assert.That(d, Is.Not.Null, text);
            await pair.RunSeconds(4.5f);
            return d!;
        }

        var d = await Say(ctx, p, "Пульсар, привет!");
        Assert.That(d!.Reason, Is.EqualTo("chat:greeting"));
        Assert.That(d.Reply, Is.Not.Null.And.Not.Empty);

        // Anti-spam: right away again, no reply.
        d = await Say(ctx, p, "спасибо");
        Assert.That(d!.Reason, Is.EqualTo("chat-cooldown"));
        Assert.That(d.Reply, Is.Null);
        await pair.RunSeconds(4.5f);

        d = await Talk("спасибо!");
        Assert.That(d.Reason, Is.EqualTo("chat:thanks"));
        Assert.That(Variants("pulsar-chat-thanks", 3), Does.Contain(d.Reply));
        Assert.That(Variants("pulsar-chat-thanks", 3), Does.Contain("Всегда к услугам экипажа."));

        d = await Talk("Пульсар, ты тут?");
        Assert.That(d.Reason, Is.EqualTo("chat:presence"));

        d = await Talk("Пульсар, кто ты такой?");
        Assert.That(d.Reason, Is.EqualTo("chat:identity"));

        d = await Talk("Пульсар, что ты умеешь?");
        Assert.That(d.Reason, Is.EqualTo("chat:help"));
        Assert.That(d.Reply, Does.Not.Contain("OOC").And.Not.Contain("игрок"));

        await SetAlert(ctx, "blue");
        d = await Talk("Пульсар, как дела?");
        Assert.That(d.Reason, Is.EqualTo("chat:status"));
        Assert.That(d.Reply, Does.Contain("Код: Синий").IgnoreCase.And.Contain("На смене: 2"));
        Assert.That(d.Reply, Does.Contain("Разгерметизаций не зафиксировано"));

        d = await Talk("пульсар ты тупая железяка");
        Assert.That(d.Reason, Is.EqualTo("chat:insult"));
        Assert.That(Variants("pulsar-chat-insult", 3), Does.Contain(d.Reply));

        d = await Talk("Пульсар, свари мне борщ");
        Assert.That(d.Outcome, Is.EqualTo(PulsarDecisionOutcome.NotUnderstood));
        Assert.That(new[] { "pulsar-reply-not-understood", "pulsar-reply-not-understood-2", "pulsar-reply-not-understood-3" }
            .Select(id => loc.GetString(id)), Does.Contain(d.Reply));

        // Just the name: "on air".
        d = await Talk("Пульсар?");
        Assert.That(d.Outcome, Is.EqualTo(PulsarDecisionOutcome.Acknowledged));

        // Other people's chatter on the radio is ignored.
        var other = await Person(ctx, "PassengerIDCard", 2, 2);
        Assert.That(await Say(ctx, other, "привет всем, как дела?", "Common"), Is.Null);

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// One incoming line is understood once, even when the same words arrive from a holopad and the radio
    /// (or a moment later through the other channel).
    /// </summary>
    [Test]
    public async Task OneReplyPerUtterance()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await Setup(pair, "Captain");
        var captain = await Person(ctx, "CaptainIDCard", 0, 2);
        var pulsar = pair.Server.System<PulsarAutonomousAiSystem>();

        async Task<(int Decisions, int Replies, PulsarDecision? Last, DoorState Door)> Speak(string text, bool twice)
        {
            var before = 0;
            await pair.Server.WaitPost(() =>
            {
                before = ctx.Pulsar.Comp.Decisions.Count;
                pulsar.HandleSpeech(ctx.Pulsar, captain, text, holopad: ctx.Core);
                if (twice)
                    pulsar.HandleSpeech(ctx.Pulsar, captain, text, channel: "Common");
            });
            await pair.RunTicksSync(3);
            var decisions = 0;
            var replies = 0;
            PulsarDecision? last = null;
            var door = DoorState.Closed;
            await pair.Server.WaitPost(() =>
            {
                var added = ctx.Pulsar.Comp.Decisions.Skip(before).ToList();
                decisions = added.Count;
                replies = added.Count(d => d.Reply != null);
                last = added.Count > 0 ? added[^1] : null;
                door = ctx.EntMan.GetComponent<DoorComponent>(ctx.CaptainDoor).State;
            });
            return (decisions, replies, last, door);
        }

        // Bare name heard at a holopad and on the radio: one "on air" reply, not two random acks.
        var r = await Speak("Ии", twice: true);
        Assert.That(r.Decisions, Is.EqualTo(1), "bare name must not be understood twice");
        Assert.That(r.Replies, Is.EqualTo(1));
        Assert.That(r.Last!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Acknowledged));

        await pair.RunSeconds(2);

        r = await Speak("Пульсар, ты тут?", twice: false);
        Assert.That(r.Decisions, Is.EqualTo(1));
        Assert.That(r.Replies, Is.EqualTo(1));
        Assert.That(r.Last!.Reason, Is.EqualTo("chat:presence"));

        await pair.RunSeconds(5);

        r = await Speak("Пульсар, ты тут?", twice: true);
        Assert.That(r.Decisions, Is.EqualTo(1), "presence small-talk must not be understood twice");
        Assert.That(r.Replies, Is.EqualTo(1));
        Assert.That(r.Last!.Reason, Is.EqualTo("chat:presence"));

        // A copy arriving a moment later through the other channel is still the same utterance.
        var after = 0;
        await pair.RunSeconds(0.3f);
        await pair.Server.WaitPost(() =>
        {
            after = ctx.Pulsar.Comp.Decisions.Count;
            pulsar.HandleSpeech(ctx.Pulsar, captain, "Пульсар, ты тут?", channel: "Common");
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
            Assert.That(ctx.Pulsar.Comp.Decisions.Count, Is.EqualTo(after), "late radio copy must be dropped"));

        await pair.RunSeconds(2);

        r = await Speak("Пульсар, открой каюту капитана", twice: true);
        Assert.That(r.Decisions, Is.EqualTo(1), "a door request must not be understood twice");
        Assert.That(r.Replies, Is.EqualTo(1));
        Assert.That(r.Last!.Outcome, Is.EqualTo(PulsarDecisionOutcome.Opened), r.Last!.Reason);
        Assert.That(r.Last.Door, Is.EqualTo(ctx.CaptainDoor));
        Assert.That(r.Door, Is.EqualTo(DoorState.Opening).Or.EqualTo(DoorState.Open));

        after = 0;
        await pair.RunSeconds(0.3f);
        await pair.Server.WaitPost(() =>
        {
            after = ctx.Pulsar.Comp.Decisions.Count;
            pulsar.HandleSpeech(ctx.Pulsar, captain, "Пульсар, открой каюту капитана", channel: "Common");
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
            Assert.That(ctx.Pulsar.Comp.Decisions.Count, Is.EqualTo(after), "late copy must not reopen or reply again"));

        await pair.CleanReturnAsync();
    }
}
