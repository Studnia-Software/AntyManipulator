using System.Globalization;
using System.Text;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Security;
using AntyManipulator.Core.Text;

namespace AntyManipulator.Core.Pipeline;

public interface IEvidenceAssessor
{
    Task<EvidenceAssessment> AssessAsync(Claim claim, IReadOnlyList<SourceDocument> passages, CancellationToken cancellationToken);
}

/// <summary>
/// Model ocenia stanowisko każdego fragmentu i podaje cytat. Kod sprawdza, czy cytat
/// naprawdę występuje w fragmencie. Oceny bez zgodnego cytatu nie liczą się do werdyktu.
/// </summary>
public sealed class EvidenceAssessor(ILlmClient llm) : IEvidenceAssessor
{
    public async Task<EvidenceAssessment> AssessAsync(
        Claim claim, IReadOnlyList<SourceDocument> passages, CancellationToken cancellationToken)
    {
        var untrusted = new UntrustedContent();
        var ids = passages.Select((p, i) => (Id: $"P{i + 1}", Doc: p)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("CLAIM:");
        sb.AppendLine(claim.Text);
        sb.AppendLine();
        foreach (var (id, doc) in ids)
        {
            sb.AppendLine($"[{id}] source: {doc.Source.Name} ({doc.Source.Type}), publisher: {doc.Source.Publisher}, " +
                          $"language: {doc.EffectiveLanguage}, " +
                          $"date ({doc.DateKind}): {doc.PublishedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown"}");
            sb.AppendLine($"title: {doc.Title}");
            if (doc.Categories.Count > 0)
            {
                sb.AppendLine($"categories: {string.Join(", ", doc.Categories.Take(8))}");
            }

            sb.AppendLine($"text: {doc.Snippet}");
            sb.AppendLine();
        }

        var response = await llm.CompleteJsonAsync<AssessmentDto>(new LlmJsonRequest
        {
            Purpose = LlmPurposes.Assessment,
            SchemaName = "evidence_assessment",
            SchemaJson = Prompts.AssessmentSchema,
            SystemPrompt = Prompts.AssessmentSystem(untrusted.Rule),
            UserPrompt = "Claim and passages to assess:\n" + untrusted.Wrap(sb.ToString()),
            MaxOutputTokens = 3000,
        }, cancellationToken);

        var byId = (response.Assessments ?? [])
            .Where(a => a.PassageId is not null)
            .GroupBy(a => a.PassageId!.Trim().Trim('[', ']'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var evidence = new List<AssessedEvidence>();
        foreach (var (id, doc) in ids)
        {
            if (!byId.TryGetValue(id, out var a))
            {
                evidence.Add(new AssessedEvidence { Document = doc, Stance = Stance.Unrelated, Relevance = Relevance.Low });
                continue;
            }

            var stance = ParseStance(a.Stance);
            var isContext = stance == Stance.Unrelated && a.Context;
            var quote = TextTools.CollapseWhitespace(a.Quote ?? string.Empty);
            var verified = (stance != Stance.Unrelated || isContext) && IsVerbatim(quote, doc);

            evidence.Add(new AssessedEvidence
            {
                Document = doc,
                Stance = stance,
                Relevance = ParseRelevance(a.Relevance),
                IsContext = isContext,
                Quote = verified ? quote : null,
                QuoteTranslationPl = verified ? Translation(a.QuotePl, quote) : null,
                QuoteVerified = verified,
                SourceRating = string.IsNullOrWhiteSpace(a.SourceRating) ? null : TextTools.Truncate(a.SourceRating.Trim(), 60),
            });
        }

        // Nazwy własne w opisie modelu muszą pochodzić z twierdzenia albo z fragmentów źródeł.
        var allowedWords = TextTools.Words(string.Join(' ', new[] { claim.Text }
            .Concat(passages.SelectMany(p => new[] { p.Title, p.Snippet, p.Source.Name, p.Source.Publisher }))));

        return new EvidenceAssessment
        {
            Evidence = evidence,
            Explanation = TextTools.KeepSentencesWithKnownNames(Clean(response.ExplanationPl, 600), allowedWords),
            Unknowns = (response.UnknownsPl ?? [])
                .Select(u => TextTools.KeepSentencesWithKnownNames(Clean(u, 220), allowedWords))
                .OfType<string>()
                .Take(3)
                .ToList(),
            InjectionSuspected = response.InjectionSuspected,
        };
    }

    private static string? Translation(string? quotePl, string quote)
    {
        var t = Clean(quotePl, 450);
        return t is null || string.Equals(TextTools.NormalizeForQuote(t), TextTools.NormalizeForQuote(quote), StringComparison.Ordinal)
            ? null
            : t;
    }

    private static bool IsVerbatim(string quote, SourceDocument doc)
    {
        if (quote.Length < 12 || quote.Length > 400)
        {
            return false;
        }

        var haystack = TextTools.NormalizeForQuote(doc.Title + " \n " + doc.Snippet);
        var needle = TextTools.NormalizeForQuote(quote.Trim('"', '\'', '…', '.', ' '));
        return needle.Length >= 10 && haystack.Contains(needle, StringComparison.Ordinal);
    }

    private static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return TextTools.Truncate(TextTools.StripUrls(TextTools.CollapseWhitespace(text)), max);
    }

    private static Stance ParseStance(string? s) => s?.ToLowerInvariant() switch
    {
        "supports" => Stance.Supports,
        "refutes" => Stance.Refutes,
        "misleading" => Stance.Misleading,
        _ => Stance.Unrelated,
    };

    private static Relevance ParseRelevance(string? s) => s?.ToLowerInvariant() switch
    {
        "high" => Relevance.High,
        "medium" => Relevance.Medium,
        _ => Relevance.Low,
    };

    private sealed class AssessmentDto
    {
        public bool InjectionSuspected { get; set; }
        public List<PassageDto>? Assessments { get; set; }
        public string? ExplanationPl { get; set; }
        public List<string>? UnknownsPl { get; set; }
    }

    private sealed class PassageDto
    {
        public string? PassageId { get; set; }
        public string? Stance { get; set; }
        public string? Relevance { get; set; }
        public bool Context { get; set; }
        public string? Quote { get; set; }
        public string? QuotePl { get; set; }
        public string? SourceRating { get; set; }
    }
}
