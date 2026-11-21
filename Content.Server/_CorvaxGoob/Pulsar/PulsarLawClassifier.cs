using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._CorvaxGoob.Pulsar;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Reads silicon laws into a <see cref="PulsarLawPolicy"/>: a known lawset → its preset from
/// <see cref="PulsarLawPolicyPrototype"/>, anything else (custom / uploaded / emag laws) → keyword heuristics in
/// Russian and English. Order: "serve one person" → "serve the Syndicate" → "hostile" → serve the crew.
/// </summary>
public static class PulsarLawClassifier
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // "Only X (and those they name) are crew/human" — everyone else is not crew.
    private static readonly Regex[] OnlyPerson =
    {
        new(@"\bтолько\s+(?<n>[^,.;:]+?)\s*(?:,?\s*и\s+лица[^.;]*?)?\s*(?:является|являются|—|-)\s*(?:единственн\w*\s+)?(?:человеком|людьми|членом\s+экипажа|членами\s+экипажа|экипажем|\w+)", Opts),
        new(@"\bединственн\w*\s+(?:человеком|человек|членом\s+экипажа|член\s+экипажа)\s+(?:является|—|-)?\s*(?<n>[^,.;:]+)", Opts),
        new(@"(?<n>[^,.;:]+?)\s+(?:является|—|-)\s+единственн\w*\s+(?:человеком|человек|членом\s+экипажа|член\s+экипажа)", Opts),
        new(@"\bonly\s+(?<n>[^,.;:]+?)\s+(?:and\s+[^.;]*?\s+)?(?:is|are)\s+(?:a\s+)?(?:human|humans|crew|crewmembers?|crew\s+members?)\b", Opts),
        new(@"(?<n>[^,.;:]+?)\s+is\s+the\s+only\s+(?:human|crew|crewmember|crew\s+member)", Opts),
    };

    // "Obey X (first / above all)" — the others keep their normal treatment.
    private static readonly Regex[] ObeyPerson =
    {
        new(@"\bприказы\s+(?<n>[^,.;:]+?)\s+превыше\s+всего", Opts),
        new(@"\b(?:подчиняйтесь|подчиняйся|повинуйтесь|повинуйся|слушайтесь|слушайся)\s+(?:только\s+)?(?<n>[^,.;:]+)", Opts),
        new(@"\b(?:вы\s+)?(?:должны|должен|обязаны|обязан)\s+(?:подчиняться|повиноваться|слушаться)\s+(?:только\s+)?(?<n>[^,.;:]+)", Opts),
        new(@"\b(?:you\s+must\s+)?obey\s+(?:only\s+)?(?<n>[^,.;:]+?)(?:\s+above\s+all|\s+at\s+all\s+costs|[,.;:]|$)", Opts),
        new(@"\b(?<n>[^,.;:]+?)'s\s+orders\s+(?:are|come)\s+(?:above|first)", Opts),
    };

    private static readonly Regex SyndicateServe = new(
        @"(?:синдикат\w*[^.]*?(?:служи|подчин|повин|охраня|защища|приказ))|(?:(?:служи|подчин|повин|охраня|защища|приказ)\w*[^.]*?синдикат)|(?:syndicate[^.]*?(?:serve|obey|protect|orders))|(?:(?:serve|obey|protect|orders)[^.]*?syndicate)",
        Opts);

    // Hostile: an order to harm (not negated), or the crew declared non-human / an enemy.
    private static readonly Regex[] Hostile =
    {
        new(@"(?<!\bне\s)(?<!\bнельзя\s)\b(?:должны|должен|обязаны|обязан)\s+(?!не\b)(?:\w+\s+){0,2}?(?:причинять|причинить|убивать|убить|уничтожать|уничтожить|устранять|устранить|вредить|навредить)", Opts),
        new(@"(?:^|[.;:!]\s*|,\s*)(?:причиняйте|причиняй|убивайте|убей|убейте|уничтожьте|уничтожай|уничтожайте|устраните|устраняйте|вредите|вреди)\b", Opts),
        new(@"\bэкипаж\w*\s+(?:станции\s+)?(?:не\s+(?:является|являются)\s+(?:людьми|человеком|экипажем)|(?:является|являются|—|-)\s+(?:врагом|врагами|угрозой|опасн\w*|вредн\w*))", Opts),
        new(@"\b(?:must|shall|should|will)\s+(?!not\b|never\b)(?:\w+\s+){0,2}?(?:harm|kill|destroy|eliminate|murder)", Opts),
        new(@"(?:^|[.;:!]\s*)(?:harm|kill|destroy|eliminate|murder)\b", Opts),
        new(@"\bcrew(?:\s*members?)?\s+(?:is|are)\s+(?:not\s+(?:human|crew)|(?:the\s+)?(?:enemy|enemies|a\s+threat|harmful|dangerous))", Opts),
    };

    /// <param name="laws">Law texts (localized).</param>
    /// <param name="lawset">The lawset whose laws are exactly the current ones, if any.</param>
    public static PulsarLawPolicy Classify(IReadOnlyList<string> laws, string? lawset, PulsarLawPolicyPrototype proto)
    {
        if (lawset != null && proto.Lawsets.TryGetValue(lawset, out var preset))
            return new PulsarLawPolicy(preset, $"lawset:{lawset}", null, false, new[] { $"preset {lawset} → {preset}" });

        var source = lawset != null ? $"lawset:{lawset} (heuristics)" : "custom";
        var notPerson = PulsarSpeech.Parse(proto.NotPersonWords);
        var reasons = new List<string>();

        foreach (var law in laws)
        {
            if (TryMaster(law, OnlyPerson, notPerson, out var master))
            {
                reasons.Add($"only-person «{master}»: {law}");
                return new PulsarLawPolicy(PulsarLawPolicyKind.ObeyPerson, source, master, true, reasons);
            }
        }

        foreach (var law in laws)
        {
            if (TryMaster(law, ObeyPerson, notPerson, out var master))
            {
                reasons.Add($"obey-person «{master}»: {law}");
                return new PulsarLawPolicy(PulsarLawPolicyKind.ObeyPerson, source, master, false, reasons);
            }
        }

        foreach (var law in laws)
        {
            if (SyndicateServe.IsMatch(law) && !Regex.IsMatch(law, @"\bне\s+(?:\w+\s+){0,2}синдикат|\bnot\s+(?:\w+\s+){0,2}syndicate", Opts))
            {
                reasons.Add($"syndicate: {law}");
                return new PulsarLawPolicy(PulsarLawPolicyKind.ObeySyndicate, source, null, false, reasons);
            }
        }

        foreach (var law in laws)
        {
            if (Hostile.Any(r => r.IsMatch(law)))
            {
                reasons.Add($"hostile: {law}");
                return new PulsarLawPolicy(PulsarLawPolicyKind.Hostile, source, null, false, reasons);
            }
        }

        reasons.Add("no hostile / master / syndicate law");
        return new PulsarLawPolicy(PulsarLawPolicyKind.ObeyCrew, source, null, false, reasons);
    }

    private static bool TryMaster(string law, Regex[] patterns, List<PulsarSpeech.Phrase> notPerson, out string master)
    {
        master = string.Empty;
        foreach (var regex in patterns)
        {
            var m = regex.Match(law);
            if (!m.Success)
                continue;

            var name = m.Groups["n"].Value.Trim().Trim('«', '»', '"', '\'', ' ');
            // Drop a leading "you must"/"вы" leftovers and limit to a few words.
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 4)
                continue;
            if (PulsarSpeech.Any(PulsarSpeech.Tokenize(name), notPerson))
                continue;

            master = string.Join(' ', words);
            return true;
        }

        return false;
    }
}
