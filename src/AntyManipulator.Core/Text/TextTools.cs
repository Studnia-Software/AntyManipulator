using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AntyManipulator.Core.Text;

/// <summary>Proste narzędzia tekstowe: normalizacja, tokeny, podobieństwo leksykalne.</summary>
public static partial class TextTools
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // pl
        "ale", "albo", "ani", "aby", "bez", "bo", "byl", "byla", "bylo", "byly", "beda", "bedzie", "czy", "dla",
        "do", "gdy", "go", "ich", "ile", "im", "jak", "jako", "jaki", "jest", "jego", "jej", "juz", "kto", "ktora",
        "ktore", "ktory", "lub", "ma", "maja", "mnie", "moze", "na", "nad", "nie", "nic", "od", "oraz", "po", "pod",
        "przez", "przy", "sa", "sie", "sobie", "tak", "takze", "tam", "ten", "ta", "te", "to", "tez", "tego", "tej",
        "tylko", "tym", "uz", "we", "wiec", "wszystko", "za", "ze", "zas", "oni", "ona", "ono", "on", "co", "czym",
        "tych", "temu", "teraz", "podobno", "bardzo", "nawet",
        // en
        "the", "and", "for", "are", "was", "were", "that", "this", "with", "from", "have", "has", "not", "but",
        "you", "they", "their", "will", "would", "can", "about", "its", "into", "than", "been", "also",
    };

    public static string RemoveDiacritics(string text)
    {
        var normalized = text.Replace('ł', 'l').Replace('Ł', 'L').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Tokeny po usunięciu ogonków, stop-słów i przycięciu do 6 znaków (prymitywny stemming).</summary>
    public static HashSet<string> Tokens(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return set;
        }

        foreach (Match m in WordRegex().Matches(RemoveDiacritics(text.ToLowerInvariant())))
        {
            var w = m.Value;
            if (w.Length < 3 || StopWords.Contains(w))
            {
                continue;
            }

            set.Add(w.Length > 6 ? w[..6] : w);
        }

        return set;
    }

    /// <summary>Jaka część tokenów zapytania występuje w dokumencie (0..1).</summary>
    public static double Coverage(IReadOnlySet<string> queryTokens, IReadOnlySet<string> docTokens)
    {
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var hit = queryTokens.Count(docTokens.Contains);
        return (double)hit / queryTokens.Count;
    }

    public static int Overlap(IReadOnlySet<string> a, IReadOnlySet<string> b) => a.Count(b.Contains);

    /// <summary>
    /// Nazwy własne z tekstu: słowa od wielkiej litery (co najmniej 4 znaki), pomijając pierwsze
    /// słowo każdego zdania. Najdłuższe pierwsze, bo zwykle są najbardziej charakterystyczne
    /// (nazwiska, nazwy organizacji). Służą jako krótkie zapytania do prostych wyszukiwarek.
    /// </summary>
    public static IReadOnlyList<string> ProperNouns(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // (słowo, czy poprzedza je inne słowo od wielkiej litery = prawdopodobnie nazwisko, pozycja)
        var names = new List<(string Word, bool Surname, int Position)>();
        var position = 0;
        foreach (var sentence in SentenceSplitRegex().Split(text))
        {
            var words = WordRegex().Matches(sentence).Select(m => m.Value).ToList();
            for (var i = 0; i < words.Count; i++, position++)
            {
                var w = words[i];
                if (i == 0 || w.Length < 4 || !char.IsUpper(w[0]) || w.All(char.IsUpper))
                {
                    continue;
                }

                names.Add((StemName(w), char.IsUpper(words[i - 1][0]), position));
            }
        }

        return names
            .OrderByDescending(n => n.Surname)
            .ThenBy(n => n.Position)
            .Select(n => n.Word)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly string[] NameSuffixes = ["ego", "emu", "owi", "iem", "em", "om", "ie", "a", "u", "y", "i", "e", "ą", "ę"];

    /// <summary>
    /// Ucina typowe końcówki odmiany ("Brauna" → "Braun", "Tuska" → "Tusk", "Morawiecki" → "Morawieck"),
    /// bo proste wyszukiwarki szukają podciągu: rdzeń pasuje do wszystkich form.
    /// </summary>
    private static string StemName(string word)
    {
        foreach (var suffix in NameSuffixes)
        {
            if (word.Length - suffix.Length >= 4 && word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    public static HashSet<string> Words(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (Match m in WordRegex().Matches(RemoveDiacritics(text.ToLowerInvariant())))
            {
                set.Add(m.Value);
            }
        }

        return set;
    }

    public static string? KeepSentencesWithKnownNames(string? text, IReadOnlySet<string> allowedWords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var kept = new List<string>();
        foreach (var sentence in SentenceSplitRegex().Split(text.Trim()))
        {
            var words = WordRegex().Matches(sentence).Select(m => m.Value).ToList();
            var ok = words.Skip(1)
                .Where(w => char.IsUpper(w[0]) || w.Any(char.IsDigit))
                .Select(w => RemoveDiacritics(w.ToLowerInvariant()))
                .All(w => w.Length <= 4
                    ? allowedWords.Contains(w)
                    : allowedWords.Any(a => a.Length >= 3 && a.StartsWith(w[..3], StringComparison.Ordinal)));
            if (ok && sentence.Trim().Length > 0)
            {
                kept.Add(sentence.Trim());
            }
        }

        return kept.Count == 0 ? null : string.Join(' ', kept);
    }

    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 0;
        }

        var inter = a.Count(b.Contains);
        return (double)inter / (a.Count + b.Count - inter);
    }

    /// <summary>Normalizacja do porównywania cytatów: małe litery, jednolite cudzysłowy i spacje.</summary>
    public static string NormalizeForQuote(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
        {
            sb.Append(ch switch
            {
                '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB' or '\u2033' => '"',
                '\u2018' or '\u2019' or '\u201A' or '\u2032' => '\'',
                '\u2013' or '\u2014' or '\u2212' => '-',
                '\u00A0' or '\u202F' or '\u2009' => ' ',
                _ => ch,
            });
        }

        return WhitespaceRegex().Replace(sb.ToString(), " ").Trim();
    }

    public static string CollapseWhitespace(string text) => WhitespaceRegex().Replace(text, " ").Trim();

    public static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text ?? string.Empty;
        }

        var cut = text[..max];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > max * 0.7 ? cut[..lastSpace] : cut) + "…";
    }

    public static string StripUrls(string text) => UrlRegex().Replace(text, "[link usunięty]");

    [GeneratedRegex(@"\p{L}[\p{L}\p{N}]*")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"(https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
