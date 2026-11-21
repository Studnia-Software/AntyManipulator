using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AntyManipulator.Core.Security;

/// <summary>
/// Czyści tekst z niewidocznych znaków (zero-width, sterowanie kierunkiem pisma),
/// którymi da się ukryć instrukcje przed człowiekiem, a pokazać je modelowi.
/// </summary>
public static partial class InputSanitizer
{
    public static string Sanitize(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (ch is '\n' or '\t')
            {
                sb.Append(ch);
            }
            else if (cat is UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.PrivateUse
                     or UnicodeCategory.OtherNotAssigned)
            {
                // pomijamy znaki niewidoczne / sterujące
            }
            else
            {
                sb.Append(ch);
            }
        }

        var result = HorizontalSpaceRegex().Replace(sb.ToString(), " ");
        result = ManyNewlinesRegex().Replace(result, "\n\n").Trim();
        return result.Length > maxLength ? result[..maxLength] : result;
    }

    [GeneratedRegex(@"[ \t\u00A0]+")]
    private static partial Regex HorizontalSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlinesRegex();
}
