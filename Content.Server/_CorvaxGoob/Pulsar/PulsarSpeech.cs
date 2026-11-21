using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Tiny speech matcher for «Пульсар»: normalisation, tokens and stem phrases
/// (see <see cref="Content.Shared._CorvaxGoob.Pulsar.PulsarLexiconPrototype"/> for the syntax).
/// </summary>
public static class PulsarSpeech
{
    private static readonly Dictionary<char, char> Lookalikes = new()
    {
        ['a'] = 'а', ['b'] = 'в', ['c'] = 'с', ['e'] = 'е', ['h'] = 'н', ['k'] = 'к', ['m'] = 'м',
        ['o'] = 'о', ['p'] = 'р', ['t'] = 'т', ['x'] = 'х', ['y'] = 'у', ['i'] = 'и', ['u'] = 'и',
        ['ё'] = 'е', ['э'] = 'е', ['3'] = 'з', ['0'] = 'о',
    };

    /// <summary>
    /// Lower case, Latin lookalikes to Cyrillic, dots between single letters dropped (А.М.Ц.И.И -> амции), then
    /// split into words.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        text = SingleLetterDots.Replace(text, string.Empty);

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var c = char.ToLowerInvariant(ch);
            if (Lookalikes.TryGetValue(c, out var mapped))
                c = mapped;

            sb.Append(char.IsLetter(c) ? c : ' ');
        }

        return new List<string>(sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Canonical form of a line for utterance dedupe (same normalisation as matching).</summary>
    public static string Normalize(string text) => string.Join(' ', Tokenize(text));

    /// <summary>A dot between two single letters: the dots of "А.М.Ц.И.И", not of "Пульсар.Открой".</summary>
    private static readonly Regex SingleLetterDots =
        new(@"(?<=(?:^|[^\p{L}\d])[\p{L}\d])\.(?=[\p{L}\d](?:[^\p{L}\d]|$))", RegexOptions.Compiled);

    public sealed class Phrase
    {
        public readonly string[] Words;
        public readonly bool[] Exact;
        public readonly string Source;

        /// <summary>
        /// Names: a typo is only forgiven on the whole word (or the word without its case ending), never on a
        /// prefix, so «пульсация» is not «пульсар». Keywords: also on a prefix of the same length (stems).
        /// </summary>
        public readonly bool Strict;

        /// <summary>All words are 3+ letters: they may come in any order, close to each other.</summary>
        private readonly bool _anyOrder;

        public Phrase(string source, bool strict = false)
        {
            Source = source;
            Strict = strict;
            var words = new List<string>();
            var exact = new List<bool>();
            foreach (var raw in source.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var isExact = raw.EndsWith('$');
                var word = isExact ? raw[..^1] : raw;
                var normal = Tokenize(word);
                if (normal.Count == 0)
                    continue;
                words.Add(string.Concat(normal));
                exact.Add(isExact);
            }

            Words = words.ToArray();
            Exact = exact.ToArray();
            _anyOrder = Words.Length > 1 && Words.All(w => w.Length >= 3);
        }

        /// <summary>Length used to rank competing matches: more and longer words win.</summary>
        public int Weight
        {
            get
            {
                var w = Words.Length * 100;
                foreach (var word in Words)
                    w += word.Length;
                return w;
            }
        }

        public bool WordMatches(int index, string token)
        {
            var word = Words[index];
            if (Exact[index] ? token == word : token.StartsWith(word, StringComparison.Ordinal))
                return true;

            // Typos: none for short words, one for 5-7 letters, two for longer keywords (one for names).
            var k = Typos(word.Length, Strict);
            if (k == 0 && (word.Length != 4 || Strict || Exact[index]))
                return false;

            if (Exact[index])
                return Math.Abs(token.Length - word.Length) <= k && Distance(token, word, k) <= k;

            if (Strict)
            {
                // A typo in the name itself, then only a noun case ending («пулсару», «пульсра»), so that
                // «пульсация» / «пульсирует» are not the name.
                for (var len = word.Length - 1; len <= word.Length + 1; len++)
                {
                    if (len < 3 || len > token.Length || !NameEndings.Contains(token[len..]))
                        continue;
                    if (Distance(token[..len], word, k) <= k)
                        return true;
                }

                return false;
            }

            if (word.Length == 4)
            {
                // Short stems («откр»): one wrong letter, only in a long enough word («аткрой», not «откос»).
                return token.Length >= 6 && Distance(token[..4], word, 1) <= 1;
            }

            for (var len = word.Length - 1; len <= word.Length + 1; len++)
            {
                if (len < 3 || len > token.Length)
                    continue;
                if (Distance(token[..len], word, k) <= k)
                    return true;
            }

            return false;
        }

        /// <summary>Index of the first token of the first match, or -1.</summary>
        public int Find(IReadOnlyList<string> tokens)
        {
            if (Words.Length == 0)
                return -1;

            for (var i = 0; i + Words.Length <= tokens.Count; i++)
            {
                var ok = true;
                for (var w = 0; w < Words.Length && ok; w++)
                    ok = WordMatches(w, tokens[i + w]);
                if (ok)
                    return i;
            }

            if (!_anyOrder)
                return -1;

            // Any order: every word matched by a different token, all within a short span.
            var used = new int[Words.Length];
            for (var w = 0; w < Words.Length; w++)
            {
                used[w] = -1;
                for (var i = 0; i < tokens.Count; i++)
                {
                    if (Array.IndexOf(used, i, 0, w) >= 0 || !WordMatches(w, tokens[i]))
                        continue;
                    used[w] = i;
                    break;
                }

                if (used[w] < 0)
                    return -1;
            }

            var min = used.Min();
            return used.Max() - min <= Words.Length + 2 ? min : -1;
        }
    }

    private static readonly HashSet<string> NameEndings = new()
    {
        "", "а", "у", "ом", "е", "ы", "ов", "ам", "ами", "ах", "ик", "ика", "ику", "чик", "чика", "чику",
    };

    /// <summary>How many typos are forgiven in a word of this length.</summary>
    public static int Typos(int length, bool strict)
    {
        if (length < 5)
            return 0;
        if (length < 8 || strict)
            return 1;
        return 2;
    }

    /// <summary>Optimal string alignment distance (Levenshtein + swapped neighbours), capped at max + 1.</summary>
    public static int Distance(string a, string b, int max = int.MaxValue)
    {
        if (a == b)
            return 0;
        if (Math.Abs(a.Length - b.Length) > max)
            return max + 1;

        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var rowMin = int.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
                rowMin = Math.Min(rowMin, v);
            }

            if (rowMin > max)
                return max + 1;
        }

        return d[a.Length, b.Length];
    }

    private static readonly string[] Endings =
    {
        "иями", "ями", "ами", "иях", "ией", "ого", "его", "ому", "ему", "ыми", "ими", "ых", "их",
        "ой", "ей", "ий", "ый", "ая", "яя", "ое", "ее", "ую", "юю", "ом", "ем", "ах", "ях", "ов", "ев",
        "ам", "ям", "ою", "ею", "ия", "ие", "ии", "ию",
        "а", "я", "о", "е", "ы", "и", "у", "ю", "ь", "й",
    };

    /// <summary>
    /// Russian word without its case / number ending («медотсеке» → «медотсек», «химлаборатории» →
    /// «химлаборатор»). Never shorter than 3 letters.
    /// </summary>
    public static string StemRu(string word)
    {
        foreach (var ending in Endings)
        {
            if (word.Length - ending.Length >= 3 && word.EndsWith(ending, StringComparison.Ordinal))
                return word[..^ending.Length];
        }

        return word;
    }

    /// <summary>
    /// Does the spoken token name the same thing as the word of a name (beacon, door, place)? Same stem, one
    /// is a 3+ letter prefix of the other (abbreviations: «хим» / «химлаборатории») or a typo of the stem.
    /// </summary>
    public static bool SameWord(string spoken, string name)
    {
        if (spoken.Length < 3 || name.Length < 3)
            return spoken == name;

        var a = StemRu(spoken);
        var b = StemRu(name);
        if (a == b || a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))
            return true;

        var k = Typos(Math.Min(a.Length, b.Length), strict: true);
        return k > 0 && Distance(a, b, k) <= k;
    }

    public static List<Phrase> Parse(IEnumerable<string> phrases, bool strict = false)
    {
        var list = new List<Phrase>();
        foreach (var p in phrases)
        {
            var phrase = new Phrase(p, strict);
            if (phrase.Words.Length > 0)
                list.Add(phrase);
        }

        return list;
    }

    public static bool Any(IReadOnlyList<string> tokens, List<Phrase> phrases)
    {
        foreach (var phrase in phrases)
        {
            if (phrase.Find(tokens) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>Is the token matched by any one-word phrase of the list (stop words, names...).</summary>
    public static bool IsWord(string token, List<Phrase> phrases)
    {
        foreach (var phrase in phrases)
        {
            if (phrase.Words.Length == 1 && phrase.WordMatches(0, token))
                return true;
        }

        return false;
    }

    /// <summary>Crude Russian stem for comparing a spoken word with a beacon name word.</summary>
    public static string Stem(string word)
    {
        return word.Length <= 4 ? word : word[..^2];
    }
}
