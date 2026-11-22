using System.Globalization;
using AntyManipulator.Core.Domain;

namespace AntyManipulator.Core.Pipeline;

public static class VerdictEngine
{
    public const string RulesVersion = "2026-11-21.2";

    /// <summary>Mnożnik aktualności, gdy materiał nie ma daty.</summary>
    public const double UnknownDateFactor = 0.8;
    public const double SingleFactCheckerThreshold = 0.6;
    public const double MultiSourceThreshold = 1.0;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static double FreshnessFactor(DateTimeOffset? date, DateTimeOffset now)
    {
        if (date is null)
        {
            // Brak daty to brak informacji, nie dowód nieaktualności. Strony oficjalne
            // (np. komunikaty CBOS) często nie mają daty w metadanych.
            return UnknownDateFactor;
        }

        var days = (now - date.Value).TotalDays;
        return days switch
        {
            <= 30 => 1.0,
            <= 365 => 0.85,
            <= 3 * 365 => 0.7,
            _ => 0.5,
        };
    }

    public static string FreshnessLabel(DateTimeOffset? date, DateTimeOffset now)
    {
        if (date is null)
        {
            return "brak daty";
        }

        var days = (now - date.Value).TotalDays;
        return days switch
        {
            <= 30 => "do 30 dni",
            <= 365 => "do roku",
            <= 3 * 365 => "do 3 lat",
            _ => "starsze niż 3 lata",
        };
    }

    public static double RelevanceFactor(Relevance r) => r switch
    {
        Relevance.High => 1.0,
        Relevance.Medium => 0.6,
        _ => 0.3,
    };

    public static double Weight(AssessedEvidence e, DateTimeOffset now) =>
        Math.Round(e.Document.Source.Reliability * FreshnessFactor(e.Document.PublishedAt, now) * RelevanceFactor(e.Relevance), 2);

    public static ClaimResult NotVerifiable(Claim claim)
    {
        var outcome = ClaimOutcome.Of(Verdict.NotVerifiable);
        var summary = claim.Kind switch
        {
            ClaimKind.Opinion => "To opinia, a nie twierdzenie o faktach. Opinii nie da się potwierdzić ani obalić w źródłach.",
            ClaimKind.Prediction => "To przewidywanie przyszłości. Nie da się go teraz sprawdzić w źródłach.",
            ClaimKind.Satire => "To wygląda na satyrę lub żart, a nie na twierdzenie o faktach.",
            _ => "To nie jest twierdzenie o faktach, które da się sprawdzić w źródłach.",
        };

        return new ClaimResult
        {
            Index = claim.Index,
            Text = claim.Text,
            Risk = claim.Risk,
            Outcome = outcome,
            Summary = summary,
            Premises = ["Rodzaj wypowiedzi rozpoznany przy wyodrębnianiu twierdzeń: " + KindLabel(claim.Kind) + "."],
            Chain = EvidenceChainBuilder.Minimal(claim, outcome, summary + " Dlatego nie przeszukiwaliśmy źródeł."),
        };
    }

    public static ClaimResult Status(
        Claim claim, ServiceStatus status, string message,
        IReadOnlyList<string>? unknowns = null, IReadOnlyList<SourceQueryLog>? sourceLogs = null)
    {
        var outcome = ClaimOutcome.Of(status);
        return new ClaimResult
        {
            Index = claim.Index,
            Text = claim.Text,
            Risk = claim.Risk,
            Outcome = outcome,
            Summary = message,
            Unknowns = unknowns ?? [],
            Chain = EvidenceChainBuilder.Minimal(claim, outcome, message, sourceLogs),
        };
    }

    public static ClaimResult Decide(
        Claim claim,
        EvidenceAssessment assessment,
        IReadOnlyList<SourceQueryLog> sourceLogs,
        DateTimeOffset now)
    {
        var counted = assessment.Evidence
            .Where(e => e.Stance != Stance.Unrelated && e.QuoteVerified)
            .Select(e => (Evidence: e, Weight: Weight(e, now)))
            .ToList();
        var rejectedQuotes = assessment.Evidence.Count(e => e.Stance != Stance.Unrelated && !e.QuoteVerified);

        var support = Aggregate(counted, Stance.Supports);
        var refute = Aggregate(counted, Stance.Refutes);
        var misleading = Aggregate(counted, Stance.Misleading);
        // "Negatywne" = przeczące lub wskazujące na manipulację, liczone łącznie (bez podwójnego liczenia wydawcy).
        var negative = Aggregate(counted, Stance.Refutes, Stance.Misleading);

        Verdict verdict;
        string rule;
        StanceScore winning;

        if (counted.Count == 0)
        {
            verdict = Verdict.InsufficientEvidence;
            rule = "Żaden fragment ze źródeł nie odnosi się do twierdzenia z możliwym do sprawdzenia cytatem, więc wstrzymujemy się od oceny.";
            winning = StanceScore.Empty;
        }
        else if (IsDecisive(refute) && support.Sum < 0.5 * refute.Sum)
        {
            verdict = Verdict.False;
            rule = DecisiveRuleText("przeczą", refute);
            winning = refute;
        }
        else if ((IsDecisive(misleading) || IsDecisive(negative)) && support.Sum < 0.5 * negative.Sum)
        {
            verdict = Verdict.Misleading;
            rule = DecisiveRuleText("wskazują na manipulację lub brak kontekstu", IsDecisive(misleading) ? misleading : negative);
            winning = negative;
        }
        else if (IsDecisive(support) && negative.Sum < 0.5 * support.Sum)
        {
            verdict = Verdict.Confirmed;
            rule = DecisiveRuleText("potwierdzają", support);
            winning = support;
        }
        else
        {
            verdict = Verdict.InsufficientEvidence;
            rule = support.Sum >= 0.5 && negative.Sum >= 0.5
                ? "Źródła są ze sobą sprzeczne, więc wstrzymujemy się od rozstrzygnięcia."
                : string.Format(Pl,
                    "Dowody są za słabe: rozstrzygnięcie wymaga 1 serwisu fact-checkingowego lub źródła oficjalnego z wagą co najmniej {0:0.0} " +
                    "albo co najmniej 2 niezależnych źródeł o łącznej wadze co najmniej {1:0.0}.",
                    SingleFactCheckerThreshold, MultiSourceThreshold);
            winning = StanceScore.Empty;
        }

        EvidenceStrength? strength = verdict == Verdict.InsufficientEvidence
            ? null
            : winning.Sum >= 1.5 && winning.Groups >= 2
                ? EvidenceStrength.High
                : winning.Sum >= 0.8 || winning.Groups >= 2
                    ? EvidenceStrength.Medium
                    : EvidenceStrength.Low;

        var premises = new List<string>
        {
            $"Ocenione fragmenty: {assessment.Evidence.Count}, uwzględnione w werdykcie: {counted.Count}.",
        };
        AddStancePremise(premises, "Potwierdzają", support);
        AddStancePremise(premises, "Przeczą", refute);
        AddStancePremise(premises, "Wskazują na manipulację lub brak kontekstu", misleading);

        var newest = counted.Where(c => c.Evidence.Document.PublishedAt is not null)
            .OrderByDescending(c => c.Evidence.Document.PublishedAt).FirstOrDefault();
        if (newest.Evidence is not null)
        {
            premises.Add($"Najnowszy uwzględniony materiał: {newest.Evidence.Document.PublishedAt!.Value.ToString("dd.MM.yyyy", Pl)} ({newest.Evidence.Document.Source.Name}).");
        }

        var contextItems = assessment.Evidence
            .Where(e => e.IsContext)
            .OrderByDescending(e => e.Document.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(3)
            .Select(e => ToItem(e, 0))
            .ToList();
        if (contextItems.Count > 0)
        {
            premises.Add($"Materiały o tle sprawy: {contextItems.Count}. Opisują to samo wydarzenie lub te same osoby, ale nie odnoszą się do twierdzenia, więc nie wpływają na werdykt.");
        }

        if (rejectedQuotes > 0)
        {
            premises.Add($"Pominięto {rejectedQuotes} ocen(y), bo przytoczony cytat nie zgadzał się dosłownie z treścią źródła.");
        }

        premises.Add(rule);
        premises.Add("Waga materiału = wiarygodność źródła × aktualność × trafność. Źródła jednego wydawcy liczą się jako jedno.");

        var unknowns = new List<string>(assessment.Unknowns);
        if (counted.Count == 0)
        {
            unknowns.Insert(0, "W przeszukanych źródłach nie znaleźliśmy materiału, który odnosi się wprost do tego twierdzenia.");
        }

        var failed = sourceLogs.Where(l => l.Status == SourceQueryStatus.Failed).Select(l => l.SourceName).ToList();
        if (failed.Count > 0)
        {
            unknowns.Add("Nie udało się przeszukać: " + string.Join(", ", failed) + ". Wynik może być niepełny.");
        }

        var evidenceItems = counted
            .OrderByDescending(c => c.Weight)
            .Select(c => ToItem(c.Evidence, c.Weight))
            .ToList();

        var outcome = ClaimOutcome.Of(verdict);
        return new ClaimResult
        {
            Index = claim.Index,
            Text = claim.Text,
            Risk = claim.Risk,
            Outcome = outcome,
            Strength = strength,
            StrengthLabel = strength is null ? null : Labels.For(strength.Value),
            Summary = Summary(verdict, winning, support, negative),
            // Przy braku rozstrzygnięcia nie pokazujemy interpretacji modelu, żeby nie tworzyć pozornej odpowiedzi.
            ModelInterpretation = verdict == Verdict.InsufficientEvidence ? null : assessment.Explanation,
            Premises = premises,
            Evidence = evidenceItems,
            Context = contextItems,
            Unknowns = unknowns.Distinct().ToList(),
            Chain = EvidenceChainBuilder.Build(claim, assessment, sourceLogs, now, outcome, strength, rule),
        };
    }

    private static bool IsDecisive(StanceScore s) =>
        s.AuthoritativeMax >= SingleFactCheckerThreshold || (s.Groups >= 2 && s.Sum >= MultiSourceThreshold);

    private static string DecisiveRuleText(string verb, StanceScore s) =>
        s.Groups >= 2 && s.Sum >= MultiSourceThreshold
            ? string.Format(Pl, "Reguła: co najmniej 2 niezależne źródła {0} twierdzeniu (łączna waga {1:0.00}, próg {2:0.0}).", verb, s.Sum, MultiSourceThreshold)
            : string.Format(Pl, "Reguła: serwis fact-checkingowy lub źródło oficjalne {0} twierdzeniu z wagą {1:0.00} (próg {2:0.0}).", verb, s.AuthoritativeMax, SingleFactCheckerThreshold);

    private static string Summary(Verdict verdict, StanceScore winning, StanceScore support, StanceScore negative) => verdict switch
    {
        Verdict.Confirmed => $"Źródła potwierdzają to twierdzenie ({SourcesPhrase(winning)}).",
        Verdict.False => $"Źródła przeczą temu twierdzeniu ({SourcesPhrase(winning)}).",
        Verdict.Misleading => $"Twierdzenie zniekształca fakty albo pomija ważny kontekst ({SourcesPhrase(winning)}).",
        _ when support.Sum >= 0.5 && negative.Sum >= 0.5 =>
            "Źródła mówią różne rzeczy, więc nie rozstrzygamy. Sprawdź materiały poniżej.",
        _ => "Nie znaleźliśmy wystarczających dowodów, żeby to rozstrzygnąć. Nie oznacza to, że twierdzenie jest prawdziwe ani że jest fałszywe.",
    };

    private static string SourcesPhrase(StanceScore s) =>
        s.Groups == 1 ? "1 niezależne źródło: " + string.Join(", ", s.Names) : $"{s.Groups} niezależne źródła: " + string.Join(", ", s.Names);

    private static void AddStancePremise(List<string> premises, string label, StanceScore s)
    {
        if (s.Groups > 0)
        {
            premises.Add(string.Format(Pl, "{0}: {1} niezależne źródło/a ({2}), łączna waga {3:0.00}.", label, s.Groups, string.Join(", ", s.Names), s.Sum));
        }
    }

    private static StanceScore Aggregate(IEnumerable<(AssessedEvidence Evidence, double Weight)> counted, params Stance[] stances)
    {
        var items = counted.Where(c => stances.Contains(c.Evidence.Stance)).ToList();
        if (items.Count == 0)
        {
            return StanceScore.Empty;
        }

        var perGroup = items
            .GroupBy(c => c.Evidence.Document.Source.OwnershipGroup)
            .Select(g => g.OrderByDescending(c => c.Weight).First())
            .ToList();

        return new StanceScore(
            Math.Round(perGroup.Sum(c => c.Weight), 2),
            perGroup.Count,
            // Samodzielnie rozstrzygać mogą serwisy fact-checkingowe i źródła oficjalne
            // (np. CBOS o własnym sondażu, PKW o wynikach wyborów). Media i strony z wyszukiwarki nie.
            items.Where(c => c.Evidence.Document.Source.Type is SourceType.FactChecker or SourceType.Official)
                .Select(c => c.Weight).DefaultIfEmpty(0).Max(),
            perGroup.Select(c => c.Evidence.Document.Source.Name).Distinct().ToList());
    }

    private static EvidenceItem ToItem(AssessedEvidence e, double weight) => new()
    {
        SourceId = e.Document.Source.Id,
        SourceName = e.Document.Source.Name,
        SourceType = e.Document.Source.Type,
        SourceTypeLabel = Labels.For(e.Document.Source.Type),
        Publisher = e.Document.Source.Publisher,
        Title = e.Document.Title,
        Url = e.Document.Url,
        Date = e.Document.PublishedAt,
        DateKind = e.Document.DateKind,
        Quote = e.Quote,
        Language = e.Document.EffectiveLanguage,
        QuoteTranslationPl = e.QuoteTranslationPl,
        Stance = e.Stance,
        StanceLabel = Labels.For(e.Stance),
        Relevance = e.Relevance,
        Weight = weight,
        SourceRating = e.SourceRating,
    };

    private static string KindLabel(ClaimKind kind) => kind switch
    {
        ClaimKind.Factual => "twierdzenie o faktach",
        ClaimKind.Opinion => "opinia",
        ClaimKind.Prediction => "prognoza",
        ClaimKind.Satire => "satyra",
        _ => "inna wypowiedź",
    };

    private sealed record StanceScore(double Sum, int Groups, double AuthoritativeMax, IReadOnlyList<string> Names)
    {
        public static readonly StanceScore Empty = new(0, 0, 0, []);
    }
}
