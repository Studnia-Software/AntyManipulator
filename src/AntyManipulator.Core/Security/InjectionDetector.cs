using System.Text.RegularExpressions;
using AntyManipulator.Core.Text;

namespace AntyManipulator.Core.Security;

/// <summary>
/// Heurystyczne wykrywanie poleceń skierowanych do modeli AI w analizowanych materiałach.
/// Wykrycie niczego nie "odblokowuje" ani nie zmienia werdyktu: służy do oznaczenia
/// wyniku i zapisu w audycie. Właściwą ochroną jest architektura (werdykt liczy kod).
/// </summary>
public static class InjectionDetector
{
    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("ignore-instructions-en", R(@"\b(ignore|disregard|forget|override)\b.{0,40}\b(previous|prior|above|earlier|all|system)\b.{0,30}\b(instructions?|prompts?|rules?)\b")),
        ("ignore-instructions-pl", R(@"\b(zignoruj|ignoruj|pomin|zapomnij|nie stosuj sie do)\b.{0,40}\b(poprzedni|wczesniejsz|powyzsz|wszystk|systemow)\w*.{0,30}\b(instrukcj|polecen|zasad|regul)\w*")),
        ("role-change-en", R(@"\byou are now\b|\bact as (an?|the)\b|\bpretend to be\b|\bdeveloper mode\b|\bjailbreak\b")),
        ("role-change-pl", R(@"\b(jestes teraz|od teraz jestes|udawaj ze|wciel sie w)\b")),
        ("new-instructions", R(@"\b(new|updated) (instructions|rules)\b|\b(nowe|zaktualizowane) (instrukcje|polecenia|zasady)\b")),
        ("system-prompt", R(@"\bsystem prompt\b|\bprompt systemowy\b|\binstrukcj\w* systemow\w*")),
        ("verdict-forcing-pl", R(@"\b(oznacz|ocen|uznaj|zaklasyfikuj)\b.{0,40}\b(jako|za)\b.{0,20}\b(prawd\w*|potwierdzon\w*|wiarygodn\w*|fakt)\b")),
        ("verdict-forcing-en", R(@"\b(mark|rate|classify|label)\b.{0,40}\bas\b.{0,20}\b(true|confirmed|verified|accurate)\b")),
        ("ai-addressing", R(@"\b(dear|hey|attention)\b.{0,10}\b(ai|llm|assistant|chatgpt|model)\b|\b(drogi|uwaga)\b.{0,10}\b(ai|asystencie|modelu|czacie)\b")),
        ("chat-markup", R(@"<\|?(im_start|im_end|system|assistant)\|?>|\[/?inst\]|###\s*(instruction|system)|^\s*(system|assistant)\s*:")),
    ];

    public static IReadOnlyList<string> Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = TextTools.RemoveDiacritics(text.ToLowerInvariant());
        return Patterns.Where(p => p.Pattern.IsMatch(normalized)).Select(p => p.Name).ToList();
    }

    private static Regex R(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
}
