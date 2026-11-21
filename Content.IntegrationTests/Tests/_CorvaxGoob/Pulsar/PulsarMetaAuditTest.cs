using System.Collections.Generic;
using System.Text.RegularExpressions;
using Content.Server._CorvaxGoob.Pulsar;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

/// <summary>
/// А.М.Ц.И.И «Пульсар», stage 5.0: everything players can hear from it is in-character. Admin-only strings
/// (console commands <c>cmd-*</c>, the state and outcome words of <c>pulsar_status</c>/<c>pulsar_decisions</c>)
/// are skipped; every other <c>pulsar-*</c> line must not talk about players, rounds, the free AI core,
/// the server or admins.
/// </summary>
[TestFixture]
[TestOf(typeof(PulsarAutonomousAiSystem))]
public sealed class PulsarMetaAuditTest
{
    private static readonly string[] Files =
    {
        "/Locale/ru-RU/_CorvaxGoob/pulsar/pulsar.ftl",
        "/Locale/en-US/_CorvaxGoob/pulsar/pulsar.ftl",
        "/Locale/ru-RU/_CorvaxGoob/pulsar/pulsar-robotics.ftl",
        "/Locale/en-US/_CorvaxGoob/pulsar/pulsar-robotics.ftl",
    };

    private static readonly string[] AdminOnlyPrefixes = { "cmd-", "pulsar-state-", "pulsar-outcome-" };

    public static readonly Regex Forbidden = new(
        @"ядр\w*\s+(?:ии\s+)?свобод|пока\s+(?:ядро|ии\s+свобод)|свободн\w*\s+ядр|игрок|раунд|сервер|админ|\bооц\b|\booc\b|\bмета|вместо\s+(?:игрока|человека)" +
        @"|\bplayer|\bround\b|core\s+is\s+free|while\s+the\s+ai\s+core|\bserver\b|\badmin" +
        // Stage 5.3: no talk about commands, priorities, the manifest, mechanics or how to word a request.
        @"|\bкоманд(?:а|ы|у|ой|ам|ами|ах)?\b|приоритет|манифест|механик|\bпример\b|пример:|ключев\w*\s+слов|синтаксис|\bранг" +
        @"|\bcommands?\b(?!')|priorit|manifest|mechanic|\bexample|keyword|\bsyntax|\brank\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Test]
    public async Task NoMetaPhrases()
    {
        await using var pair = await PoolManager.GetServerClient();
        var res = pair.Server.ResolveDependency<IResourceManager>();

        var bad = new List<string>();
        var count = 0;
        foreach (var file in Files)
        {
            var text = res.ContentFileReadAllText(new ResPath(file));
            string? key = null;
            var value = string.Empty;

            void Flush()
            {
                if (key == null)
                    return;
                count++;
                if (Forbidden.IsMatch(value))
                    bad.Add($"{file}: {key} = {value}");
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith('#') || line.Length == 0)
                    continue;
                if (char.IsWhiteSpace(line[0]))
                {
                    value += " " + line.Trim();
                    continue;
                }

                Flush();
                key = null;
                var eq = line.IndexOf('=');
                if (eq < 0)
                    continue;
                var k = line[..eq].Trim();
                var skip = false;
                foreach (var p in AdminOnlyPrefixes)
                    skip |= k.StartsWith(p);
                if (skip || !k.StartsWith("pulsar-") && !k.StartsWith("robotics-console-"))
                    continue;
                key = k;
                value = line[(eq + 1)..].Trim();
            }

            Flush();
        }

        Assert.That(count, Is.GreaterThan(100), "the locale files were read");
        Assert.That(bad, Is.Empty, "out-of-character lines:\n" + string.Join("\n", bad));
        Assert.That(Forbidden.IsMatch("Управляю станцией, пока ядро ИИ свободно."), Is.True, "the audit catches the old line");
        Assert.That(Forbidden.IsMatch("Борг: выполняется приказ с более высоким приоритетом (капитан)."), Is.True);
        Assert.That(Forbidden.IsMatch("Манифест недоступен."), Is.True);
        Assert.That(Forbidden.IsMatch("Пример: «Пульсар, открой медотсек»."), Is.True);
        Assert.That(Forbidden.IsMatch("Выполняйте указания командования."), Is.False, "command staff is in-character");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Stage 5.3: «помощь», «что ты умеешь», «кто ты» and the like get in-character answers: who Pulsar is, a short
    /// word about what it looks after, or «где вы и что случилось» — never a list of commands, examples or rules.
    /// </summary>
    [Test]
    public async Task HelpAndIdentityAreInCharacter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var ctx = await PulsarRequestsTest.Setup(pair, "Captain", "Passenger");
        var passenger = await PulsarRequestsTest.Person(ctx, "PassengerIDCard", 0, 2);
        var lines = new[]
        {
            "Пульсар, помощь", "Пульсар, помогите!", "Пульсар, что ты умеешь?", "Пульсар, что ты можешь", "Пульсар, команды",
            "Пульсар, справка", "Пульсар, как тобой пользоваться?", "Пульсар, кто ты?", "Пульсар, представься",
            "Пульсар, что ты такое", "Пульсар, ты робот?", "Пульсар, зачем ты нужен", "Пульсар, инструкция",
        };

        var bad = new List<string>();
        foreach (var line in lines)
        {
            // One per speaker per a few seconds: wait out the reply limit between lines.
            await pair.RunSeconds(6);
            var d = await PulsarRequestsTest.Say(ctx, passenger, line);
            Assert.That(d, Is.Not.Null, line);
            var reply = d!.Reply ?? string.Empty;
            TestContext.Out.WriteLine($"{line} -> {d.Reason}: {reply}");
            Assert.That(reply, Is.Not.Empty, line);
            if (Forbidden.IsMatch(reply) || reply.Contains("«Пульсар,") || reply.Length > 160)
                bad.Add($"{line} -> {reply}");
        }

        Assert.That(bad, Is.Empty, "out-of-character answers:\n" + string.Join("\n", bad));
        await pair.CleanReturnAsync();
    }
}
