using System.Text.RegularExpressions;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Security;
using AntyManipulator.Core.Text;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Core.Pipeline;

public interface IClaimExtractor
{
    Task<ClaimExtraction> ExtractAsync(string material, CancellationToken cancellationToken);
}

public sealed partial class ClaimExtractor(ILlmClient llm, IOptions<PipelineOptions> options) : IClaimExtractor
{
    public async Task<ClaimExtraction> ExtractAsync(string material, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var max = opts.MaxClaims;
        var languages = opts.AllowedLanguages();
        var untrusted = new UntrustedContent();

        var response = await llm.CompleteJsonAsync<ExtractionDto>(new LlmJsonRequest
        {
            Purpose = LlmPurposes.Extraction,
            SchemaName = "claim_extraction",
            SchemaJson = Prompts.ExtractionSchema(languages),
            SystemPrompt = Prompts.ExtractionSystem(max, opts.MaxExtraLanguages, languages, untrusted.Rule),
            UserPrompt = "Material to analyse:\n" + untrusted.Wrap(material),
            MaxOutputTokens = 3000,
        }, cancellationToken);

        var claims = new List<Claim>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in response.Claims ?? [])
        {
            var text = TextTools.Truncate(TextTools.CollapseWhitespace(dto.Text ?? string.Empty), 300);
            if (text.Length < 8 || !seen.Add(text))
            {
                continue;
            }

            var queries = CleanQueries(dto.Queries, languages, opts.MaxExtraLanguages);
            if (!queries.Any(q => q.Language == "pl"))
            {
                queries.Insert(0, new SearchQuery("pl", TextTools.Truncate(text, 80)));
            }

            claims.Add(new Claim
            {
                Index = claims.Count + 1,
                Text = text,
                Kind = CorrectKind(ParseKind(dto.Kind), text),
                Risk = string.Equals(dto.Risk, "high", StringComparison.OrdinalIgnoreCase) ? RiskLevel.High : RiskLevel.Normal,
                RiskReason = NullIfEmpty(TextTools.Truncate(dto.RiskReason, 120)),
                NeedsClarification = dto.NeedsClarification,
                ClarificationQuestion = NullIfEmpty(TextTools.StripUrls(TextTools.Truncate(dto.ClarificationQuestion, 200))),
                Queries = queries,
            });

            if (claims.Count >= max)
            {
                break;
            }
        }

        return new ClaimExtraction { Claims = claims, InjectionSuspected = response.InjectionSuspected };
    }

    private static ClaimKind ParseKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "factual" => ClaimKind.Factual,
        "opinion" => ClaimKind.Opinion,
        "prediction" => ClaimKind.Prediction,
        "satire" => ClaimKind.Satire,
        _ => ClaimKind.Other,
    };

    private static ClaimKind CorrectKind(ClaimKind kind, string text)
    {
        if (kind != ClaimKind.Factual)
        {
            return kind;
        }

        var normalized = TextTools.RemoveDiacritics(text.ToLowerInvariant());
        return EvaluativeRegex().IsMatch(normalized) && !AttributionRegex().IsMatch(normalized)
            ? ClaimKind.Opinion
            : kind;
    }

    [GeneratedRegex(@"\b(budzi|budza|wzbudza|wzbudzaja|rodzi|rodza)\b.{0,30}\b(watpliwosc|kontrowers|zastrzez|niepokoj|pytani|obaw)|\b(jest|sa|bylo|byl|byla) (skandaliczn|kontrowersyjn|haniebn|kompromitujac|zenujac|oburzajac)")]
    private static partial Regex EvaluativeRegex();

    [GeneratedRegex(@"\b(powiedzial|powiedziala|oswiadczyl|oswiadczyla|stwierdzil|stwierdzila|uznal|uznala|ocenil|ocenila|napisal|napisala|twierdzi|wedlug|zdaniem|przyznal|przyznala)\b")]
    private static partial Regex AttributionRegex();

    private static List<SearchQuery> CleanQueries(IEnumerable<QueryDto>? queries, IReadOnlyList<string> allowed, int maxExtraLanguages)
    {
        var result = new List<SearchQuery>();
        var extraLanguages = new List<string>();
        foreach (var dto in queries ?? [])
        {
            var lang = (dto.Language ?? string.Empty).Trim().ToLowerInvariant();
            var text = TextTools.CollapseWhitespace(QueryCharsRegex().Replace(dto.Query ?? string.Empty, " "));
            text = text.Length > 80 ? text[..80].Trim() : text;
            if (text.Length < 3 || !allowed.Contains(lang)
                || result.Any(r => r.Language == lang && string.Equals(r.Text, text, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var perLanguage = result.Count(r => r.Language == lang);
            var limit = lang switch { "pl" => 3, "en" => 2, _ => 1 };
            if (perLanguage >= limit)
            {
                continue;
            }

            if (lang is not ("pl" or "en"))
            {
                if (!extraLanguages.Contains(lang))
                {
                    if (extraLanguages.Count >= maxExtraLanguages)
                    {
                        continue;
                    }

                    extraLanguages.Add(lang);
                }
            }

            result.Add(new SearchQuery(lang, text));
        }

        return result;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}\s\-]")]
    private static partial Regex QueryCharsRegex();

    private sealed class ExtractionDto
    {
        public bool InjectionSuspected { get; set; }
        public List<ClaimDto>? Claims { get; set; }
    }

    private sealed class ClaimDto
    {
        public string? Text { get; set; }
        public string? Kind { get; set; }
        public bool NeedsClarification { get; set; }
        public string? ClarificationQuestion { get; set; }
        public string? Risk { get; set; }
        public string? RiskReason { get; set; }
        public List<QueryDto>? Queries { get; set; }
    }

    private sealed class QueryDto
    {
        public string? Language { get; set; }
        public string? Query { get; set; }
    }
}
