using System.Globalization;
using AntyManipulator.Core.Domain;

namespace AntyManipulator.Core.Pipeline;

/// <summary>
/// Składa łańcuch dowodowy z danych, które pipeline już ma: zapytań, wyników źródeł, ocen
/// fragmentów i reguły werdyktu. Nie korzysta z modelu językowego, więc opis zawsze zgadza
/// się z tym, co naprawdę policzył <see cref="VerdictEngine"/>.
/// </summary>
public static class EvidenceChainBuilder
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static EvidenceChain Build(
        Claim claim,
        EvidenceAssessment assessment,
        IReadOnlyList<SourceQueryLog> sourceLogs,
        DateTimeOffset now,
        ClaimOutcome outcome,
        EvidenceStrength? strength,
        string rule)
    {
        var links = BuildLinks(assessment, now);
        var comparison = Compare(links);

        var steps = new List<ChainStep>
        {
            ClaimStep(claim),
            SearchStep(sourceLogs),
            new()
            {
                Order = 3,
                Stage = "selection",
                Title = "Wybór fragmentów do oceny",
                Details =
                [
                    links.Count == 0
                        ? "Żaden znaleziony materiał nie miał wystarczająco wielu wspólnych słów kluczowych z twierdzeniem."
                        : $"Do oceny trafiło {links.Count} fragment(ów) od {links.Select(l => l.OwnershipGroup).Distinct().Count()} wydawcy(ów). " +
                          "Sprawdzana strona i serwisy jej wydawcy są wykluczone.",
                ],
            },
            new()
            {
                Order = 4,
                Stage = "assessment",
                Title = "Ocena fragmentów",
                Details = links.Count == 0 ? ["Brak fragmentów do oceny."] : links.Select(DescribeLink).ToList(),
            },
            new()
            {
                Order = 5,
                Stage = "comparison",
                Title = "Porównanie źródeł",
                Details = new[] { comparison.Summary }.Concat(comparison.Conflicts).ToList(),
            },
            new()
            {
                Order = 6,
                Stage = "rule",
                Title = "Reguła i wynik",
                Details = [rule, ResultLine(outcome, strength)],
            },
        };

        return new EvidenceChain
        {
            Narrative = Narrative(sourceLogs, claim, links, comparison, outcome, strength),
            Steps = steps,
            Links = links,
            Comparison = comparison,
        };
    }

    /// <summary>Łańcuch dla twierdzeń bez wyszukiwania (opinia, prognoza, doprecyzowanie, awaria).</summary>
    public static EvidenceChain Minimal(Claim claim, ClaimOutcome outcome, string reason, IReadOnlyList<SourceQueryLog>? sourceLogs = null)
    {
        var steps = new List<ChainStep> { ClaimStep(claim) };
        if (sourceLogs is { Count: > 0 })
        {
            steps.Add(SearchStep(sourceLogs));
        }

        steps.Add(new ChainStep { Order = steps.Count + 1, Stage = "rule", Title = "Wynik", Details = [reason, ResultLine(outcome, null)] });
        return new EvidenceChain { Narrative = reason + " " + ResultLine(outcome, null), Steps = steps };
    }

    private static List<ChainLink> BuildLinks(EvidenceAssessment assessment, DateTimeOffset now)
    {
        // Ten sam wybór co w VerdictEngine: na wydawcę i stronę liczy się tylko najmocniejszy fragment.
        var counted = assessment.Evidence
            .Select((e, i) => (Evidence: e, Index: i))
            .Where(x => x.Evidence.Stance != Stance.Unrelated && x.Evidence.QuoteVerified)
            .GroupBy(x => (x.Evidence.Document.Source.OwnershipGroup, x.Evidence.Stance))
            .Select(g => g.OrderByDescending(x => VerdictEngine.Weight(x.Evidence, now)).First().Index)
            .ToHashSet();

        return assessment.Evidence.Select((e, i) =>
        {
            var role = RoleOf(e);
            var weight = role is ChainRole.Rejected or ChainRole.Context ? 0 : VerdictEngine.Weight(e, now);
            var isCounted = counted.Contains(i);
            string? note = role switch
            {
                ChainRole.Rejected when e.Stance == Stance.Unrelated => "Nie dotyczy twierdzenia.",
                ChainRole.Rejected => "Cytat podany przez model nie zgadza się dosłownie z treścią źródła, więc ocena nie jest uwzględniona.",
                ChainRole.Context => "Tło sprawy. Nie potwierdza ani nie obala twierdzenia, nie wpływa na werdykt.",
                _ when !isCounted => "Ten sam wydawca ma mocniejszy fragment o tym samym stanowisku; wydawca liczy się raz.",
                _ => null,
            };

            return new ChainLink
            {
                PassageId = $"P{i + 1}",
                Role = role,
                RoleLabel = RoleLabel(role),
                SourceName = e.Document.Source.Name,
                Publisher = e.Document.Source.Publisher,
                SourceTypeLabel = Labels.For(e.Document.Source.Type),
                OwnershipGroup = e.Document.Source.OwnershipGroup,
                Title = e.Document.Title,
                Url = e.Document.Url,
                Date = e.Document.PublishedAt,
                Language = e.Document.EffectiveLanguage,
                Quote = e.Quote,
                QuoteTranslationPl = e.QuoteTranslationPl,
                Weight = weight,
                WeightFormula = role is ChainRole.Rejected or ChainRole.Context ? null : Formula(e, now),
                CountedInVerdict = isCounted,
                Note = note,
            };
        }).ToList();
    }

    private static SourceComparison Compare(IReadOnlyList<ChainLink> links)
    {
        List<string> Names(ChainRole role) => links
            .Where(l => l.Role == role && l.CountedInVerdict)
            .Select(l => l.SourceName)
            .Distinct()
            .ToList();

        var supporting = Names(ChainRole.Supports);
        var refuting = Names(ChainRole.Refutes);
        var misleading = Names(ChainRole.Misleading);
        var context = links.Where(l => l.Role == ChainRole.Context).Select(l => l.SourceName).Distinct().ToList();
        var negative = refuting.Concat(misleading).Distinct().ToList();

        var conflicts = new List<string>();
        foreach (var s in supporting)
        {
            foreach (var n in negative)
            {
                var verb = refuting.Contains(n) ? "przeczy" : "wskazuje na manipulację lub brak kontekstu";
                conflicts.Add($"{s} potwierdza, a {n} {verb}.");
            }
        }

        var agreement = supporting.Count + negative.Count == 0
            ? Agreement.NoEvidence
            : supporting.Count > 0 && negative.Count > 0 ? Agreement.Conflicting : Agreement.Consistent;

        var summary = agreement switch
        {
            Agreement.NoEvidence => "Żadne źródło nie odnosi się do twierdzenia w sposób, który można uwzględnić.",
            Agreement.Conflicting => $"Źródła są ze sobą sprzeczne: potwierdza {Count(supporting.Count)}, przeczy lub wskazuje na manipulację {Count(negative.Count)}.",
            _ when supporting.Count > 0 => $"Źródła są zgodne: {Count(supporting.Count)} potwierdza ({string.Join(", ", supporting)}), żadne nie przeczy.",
            _ => $"Źródła są zgodne: {Count(negative.Count)} przeczy lub wskazuje na manipulację ({string.Join(", ", negative)}), żadne nie potwierdza.",
        };

        if (context.Count > 0)
        {
            summary += $" Tło sprawy opisuje: {string.Join(", ", context)}.";
        }

        return new SourceComparison
        {
            Agreement = agreement,
            AgreementLabel = agreement switch
            {
                Agreement.Consistent => "zgodne",
                Agreement.Conflicting => "sprzeczne",
                _ => "brak materiałów",
            },
            Summary = summary,
            Supporting = supporting,
            Refuting = refuting,
            Misleading = misleading,
            ContextOnly = context,
            Conflicts = conflicts.Take(6).ToList(),
        };
    }

    private static ChainStep ClaimStep(Claim claim)
    {
        var details = new List<string>
        {
            claim.Text,
            "Rodzaj: " + claim.Kind switch
            {
                ClaimKind.Factual => "twierdzenie o faktach",
                ClaimKind.Opinion => "opinia",
                ClaimKind.Prediction => "prognoza",
                ClaimKind.Satire => "satyra",
                _ => "inna wypowiedź",
            } + (claim.Risk == RiskLevel.High ? $"; wysokie ryzyko ({claim.RiskReason ?? "sprawa wrażliwa"})" : string.Empty) + ".",
        };

        if (claim.Queries.Count > 0)
        {
            details.Add("Zapytania: " + string.Join("; ", claim.Queries
                .GroupBy(q => q.Language)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(q => "„" + q.Text + "”"))}")) + ".");
        }

        return new ChainStep { Order = 1, Stage = "claim", Title = "Twierdzenie", Details = details };
    }

    private static ChainStep SearchStep(IReadOnlyList<SourceQueryLog> logs) => new()
    {
        Order = 2,
        Stage = "search",
        Title = "Wyszukiwanie w źródłach",
        Details = logs.Count == 0
            ? ["Nie przeszukiwano źródeł."]
            : logs.Select(l =>
            {
                var status = l.Status switch
                {
                    SourceQueryStatus.Ok => $"znaleziono {l.ResultCount}",
                    SourceQueryStatus.NoResults => "brak wyników",
                    SourceQueryStatus.Failed => "nie udało się przeszukać" + (l.Message is null ? string.Empty : $" ({l.Message})"),
                    _ => "pominięte",
                };
                var queries = l.Queries.Count > 0 ? $"; zapytania: {string.Join(", ", l.Queries)}" : string.Empty;
                return $"{l.SourceName}: {status}{queries}.";
            }).ToList(),
    };

    private static string DescribeLink(ChainLink l)
    {
        var date = l.Date is null ? "bez daty" : l.Date.Value.ToString("dd.MM.yyyy", Pl);
        var text = $"[{l.PassageId}] {l.SourceName} ({l.SourceTypeLabel}, {date}, język: {l.Language}): {l.RoleLabel}.";
        if (l.Quote is not null)
        {
            text += $" Cytat: „{l.Quote}”";
            if (l.QuoteTranslationPl is not null)
            {
                text += $" (tłumaczenie: „{l.QuoteTranslationPl}”)";
            }

            text += ".";
        }

        if (l.WeightFormula is not null)
        {
            text += $" Waga: {l.WeightFormula}.";
        }

        if (l.Note is not null)
        {
            text += " " + l.Note;
        }

        return text;
    }

    private static string Narrative(
        IReadOnlyList<SourceQueryLog> logs, Claim claim, IReadOnlyList<ChainLink> links, SourceComparison comparison,
        ClaimOutcome outcome, EvidenceStrength? strength)
    {
        var sources = logs.Select(l => l.SourceId).Distinct().Count();
        var languages = claim.Queries.Select(q => q.Language).Distinct().ToList();
        var failed = logs.Where(l => l.Status == SourceQueryStatus.Failed).Select(l => l.SourceName).Distinct().ToList();
        var rejected = links.Count(l => l.Role == ChainRole.Rejected);

        var parts = new List<string>
        {
            $"Przeszukaliśmy {sources} źródeł" + (languages.Count > 1 ? $" w językach: {string.Join(", ", languages)}" : string.Empty) + ".",
        };
        if (failed.Count > 0)
        {
            parts.Add($"Nie udało się przeszukać: {string.Join(", ", failed)}.");
        }

        parts.Add(links.Count == 0
            ? "Nie znaleźliśmy materiałów pasujących do twierdzenia."
            : $"Oceniliśmy {links.Count} fragment(ów)" + (rejected > 0 ? $", z czego {rejected} odrzuciliśmy" : string.Empty) + ".");
        parts.Add(comparison.Summary);
        parts.Add(ResultLine(outcome, strength));
        return string.Join(' ', parts);
    }

    private static string ResultLine(ClaimOutcome outcome, EvidenceStrength? strength) =>
        $"Wynik: {outcome.Label}" + (strength is null ? "." : $", siła podstawy: {Labels.For(strength.Value)}.");

    private static string Formula(AssessedEvidence e, DateTimeOffset now)
    {
        var freshness = VerdictEngine.FreshnessFactor(e.Document.PublishedAt, now);
        var relevance = VerdictEngine.RelevanceFactor(e.Relevance);
        return string.Format(Pl, "{0:0.00} (wiarygodność) × {1:0.00} ({2}) × {3:0.00} (trafność {4}) = {5:0.00}",
            e.Document.Source.Reliability, freshness, VerdictEngine.FreshnessLabel(e.Document.PublishedAt, now),
            relevance, RelevanceLabel(e.Relevance), VerdictEngine.Weight(e, now));
    }

    private static ChainRole RoleOf(AssessedEvidence e) => e switch
    {
        { IsContext: true } => ChainRole.Context,
        { Stance: Stance.Unrelated } => ChainRole.Rejected,
        { QuoteVerified: false } => ChainRole.Rejected,
        { Stance: Stance.Supports } => ChainRole.Supports,
        { Stance: Stance.Refutes } => ChainRole.Refutes,
        _ => ChainRole.Misleading,
    };

    private static string RoleLabel(ChainRole role) => role switch
    {
        ChainRole.Supports => "potwierdza",
        ChainRole.Refutes => "obala",
        ChainRole.Misleading => "wskazuje na manipulację lub brak kontekstu",
        ChainRole.Context => "uzupełnia kontekst",
        _ => "odrzucony",
    };

    private static string RelevanceLabel(Relevance r) => r switch
    {
        Relevance.High => "wysoka",
        Relevance.Medium => "średnia",
        _ => "niska",
    };

    private static string Count(int n) => n == 1 ? "1 źródło" : $"{n} źródła";
}
