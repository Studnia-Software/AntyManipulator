using System.Globalization;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Text;
using Discord;

namespace AntyManipulator.Channels.Discord;

/// <summary>
/// Zamienia wynik sprawdzenia na wiadomość Discorda w granicach limitów platformy
/// (do 10 embedów, łącznie do 6000 znaków, pole do 1024 znaków). Wszystkie teksty z zewnątrz
/// (twierdzenia, tytuły, cytaty, opis modelu) są oczyszczane z formatowania Markdown,
/// żeby nie dało się nimi wstrzyknąć linków ani wzmianek.
/// </summary>
public static class DiscordFormatter
{
    private const int MaxTotal = 5800;
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string Header(CheckResult result)
    {
        var header = result.Claims.Count switch
        {
            0 => "**Wynik sprawdzenia**",
            1 => "**Wynik sprawdzenia** (1 twierdzenie)",
            var n => $"**Wynik sprawdzenia** ({n} twierdzenia)",
        };

        if (result.InjectionSuspected)
        {
            header += "\n⚠️ Materiał zawierał polecenia skierowane do AI. Zignorowaliśmy je.";
        }

        return header;
    }

    public static string StatusMessage(StatusInfo status, string? reportUrl = null) =>
        $"{Icon(status: status.Status)} **{status.Label}**\n{Clean(status.Message, 1500)}" +
        (reportUrl is null ? string.Empty : $"\nSzczegóły: <{reportUrl}>");

    public static Embed[] Build(CheckResult result, string? reportUrl)
    {
        var claims = result.Claims.Take(9).ToList();
        var footer = FooterEmbed(result, reportUrl);
        var budget = Math.Max(800, (MaxTotal - footer.Length) / Math.Max(1, claims.Count));

        var embeds = claims.Select(c => ClaimEmbed(c, budget, includeChain: true)).ToList();
        if (embeds.Sum(e => e.Length) + footer.Length > MaxTotal)
        {
            // Za długo: najpierw rezygnujemy z opisu łańcucha (jest w pełnym raporcie).
            embeds = claims.Select(c => ClaimEmbed(c, budget, includeChain: false)).ToList();
        }

        embeds.Add(footer);
        return embeds.ToArray();
    }

    private static Embed ClaimEmbed(ClaimResult claim, int budget, bool includeChain)
    {
        var outcome = claim.Outcome;
        var builder = new EmbedBuilder()
            .WithColor(ColorOf(outcome))
            .WithTitle(Clean($"{Icon(outcome.Verdict, outcome.Status)} {claim.Index}. {outcome.Label}", 250));

        var description = $"**Twierdzenie:** {Clean(claim.Text, 300)}\n\n{Clean(claim.Summary, 400)}";
        if (claim.StrengthLabel is not null)
        {
            description += $"\n**Siła podstawy:** {claim.StrengthLabel}";
        }

        if (claim.ModelInterpretation is not null)
        {
            description += $"\n\n*Interpretacja modelu (może się mylić):* {Clean(claim.ModelInterpretation, 500)}";
        }

        builder.WithDescription(Truncate(description, (int)(budget * 0.4)));

        var fieldLimit = Math.Min(EmbedFieldBuilder.MaxFieldValueLength, (int)(budget * 0.25));
        if (claim.Evidence.Count > 0)
        {
            builder.AddField("Materiały źródłowe", JoinLines(claim.Evidence.Take(4).Select(EvidenceLine), fieldLimit));
        }

        if (claim.Context.Count > 0 && budget >= 1500)
        {
            builder.AddField("Tło sprawy (nie wpływa na wynik)", JoinLines(claim.Context.Take(2).Select(EvidenceLine), fieldLimit / 2));
        }

        if (claim.Unknowns.Count > 0)
        {
            builder.AddField("Czego nie ustaliliśmy", JoinLines(claim.Unknowns.Take(3).Select(u => "• " + Clean(u, 220)), fieldLimit / 2));
        }

        if (includeChain && claim.Chain is not null)
        {
            builder.AddField("Łańcuch dowodowy", Truncate(Clean(claim.Chain.Narrative, 1000), fieldLimit));
        }

        return builder.Build();
    }

    private static Embed FooterEmbed(CheckResult result, string? reportUrl)
    {
        var limitations = result.Limitations.Take(2).Select(l => "• " + Clean(l, 350));
        var text = string.Join('\n', limitations);
        if (reportUrl is not null)
        {
            text += $"\n\n[Pełny raport ze źródłami i łańcuchem dowodowym]({reportUrl})";
        }

        return new EmbedBuilder()
            .WithColor(new Color(0x546E7A))
            .WithTitle("Ograniczenia")
            .WithDescription(Truncate(text, 1500))
            .WithFooter($"ID sprawdzenia: {result.Id} • {result.CompletedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl)}")
            .Build();
    }

    private static string EvidenceLine(EvidenceItem e)
    {
        var date = e.Date is null ? "bez daty" : e.Date.Value.ToString("dd.MM.yyyy", Pl);
        var title = Clean(e.Title, 90).Replace("[", "(").Replace("]", ")");
        var link = SafeUrl(e.Url) is { } url ? $"[{title}]({url})" : title;
        var line = $"• {link} — {Clean(e.Publisher, 60)}, {date} — **{e.StanceLabel}**";
        if (e.Quote is not null)
        {
            var quote = e.QuoteTranslationPl is not null
                ? $"„{Clean(e.QuoteTranslationPl, 160)}” (tłum. z {e.Language})"
                : $"„{Clean(e.Quote, 160)}”";
            line += $"\n  > {quote}";
        }

        return line;
    }

    /// <summary>Tylko http/https; nawiasy kodowane, żeby nie zamknęły linku Markdown.</summary>
    private static string? SafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u.AbsoluteUri.Replace("(", "%28").Replace(")", "%29")
            : null;

    private static string JoinLines(IEnumerable<string> lines, int max)
    {
        var result = string.Empty;
        foreach (var line in lines)
        {
            var next = result.Length == 0 ? line : result + "\n" + line;
            if (next.Length > max)
            {
                break;
            }

            result = next;
        }

        return result.Length == 0 ? "—" : result;
    }

    private static string Clean(string? text, int max) =>
        Format.Sanitize(TextTools.Truncate(TextTools.CollapseWhitespace(text ?? string.Empty), max))
            .Replace("@", "@\u200B");

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "…";

    private static Color ColorOf(ClaimOutcome outcome) => outcome.Verdict switch
    {
        Verdict.Confirmed => new Color(0x2E7D32),
        Verdict.False => new Color(0xC62828),
        Verdict.Misleading => new Color(0xEF6C00),
        Verdict.InsufficientEvidence => new Color(0x757575),
        Verdict.NotVerifiable => new Color(0x546E7A),
        _ => new Color(0xF9A825),
    };

    private static string Icon(Verdict? verdict = null, ServiceStatus? status = null) => verdict switch
    {
        Verdict.Confirmed => "✅",
        Verdict.False => "❌",
        Verdict.Misleading => "⚠️",
        Verdict.InsufficientEvidence => "❔",
        Verdict.NotVerifiable => "💬",
        _ => status == ServiceStatus.NeedsClarification ? "✏️" : "🛠️",
    };
}
