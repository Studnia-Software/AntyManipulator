namespace AntyManipulator.Core.Domain;

public sealed record ClaimOutcome
{
    public required OutcomeKind Kind { get; init; }
    public Verdict? Verdict { get; init; }
    public ServiceStatus? Status { get; init; }
    public required string Label { get; init; }

    public static ClaimOutcome Of(Verdict verdict) => new()
    {
        Kind = OutcomeKind.Verdict,
        Verdict = verdict,
        Label = Labels.For(verdict),
    };

    public static ClaimOutcome Of(ServiceStatus status) => new()
    {
        Kind = OutcomeKind.Status,
        Status = status,
        Label = Labels.For(status),
    };
}

public static class Labels
{
    public static string For(Verdict v) => v switch
    {
        Verdict.Confirmed => "Potwierdzone",
        Verdict.False => "Fałszywe",
        Verdict.Misleading => "Wprowadzające w błąd",
        Verdict.InsufficientEvidence => "Brak wystarczających dowodów",
        Verdict.NotVerifiable => "Nie podlega weryfikacji",
        _ => v.ToString(),
    };

    public static string For(ServiceStatus s) => s switch
    {
        ServiceStatus.TechnicalFailure => "Awaria techniczna",
        ServiceStatus.CannotCheck => "Nie udało się wykonać sprawdzenia",
        ServiceStatus.NeedsClarification => "Potrzebne doprecyzowanie",
        _ => s.ToString(),
    };

    public static string For(EvidenceStrength s) => s switch
    {
        EvidenceStrength.High => "wysoka",
        EvidenceStrength.Medium => "średnia",
        EvidenceStrength.Low => "niska",
        _ => s.ToString(),
    };

    public static string For(Stance s) => s switch
    {
        Stance.Supports => "potwierdza",
        Stance.Refutes => "przeczy",
        Stance.Misleading => "wskazuje na manipulację lub brak kontekstu",
        Stance.Unrelated => "tło sprawy, nie rozstrzyga twierdzenia",
        _ => s.ToString(),
    };

    public static string For(SourceType t) => t switch
    {
        SourceType.FactChecker => "serwis fact-checkingowy",
        SourceType.Encyclopedia => "encyklopedia",
        SourceType.Official => "źródło oficjalne",
        SourceType.News => "media informacyjne",
        SourceType.Web => "strona z wyszukiwarki internetowej",
        _ => t.ToString(),
    };
}

public sealed record StatusInfo
{
    public required ServiceStatus Status { get; init; }
    public required string Label { get; init; }
    public required string Message { get; init; }

    public static StatusInfo Of(ServiceStatus status, string message) =>
        new() { Status = status, Label = Labels.For(status), Message = message };
}

public sealed record EvidenceItem
{
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public required SourceType SourceType { get; init; }
    public required string SourceTypeLabel { get; init; }
    public required string Publisher { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset? Date { get; init; }
    public required string DateKind { get; init; }
    public string? Quote { get; init; }

    /// <summary>Język materiału (ISO 639-1).</summary>
    public string? Language { get; init; }

    /// <summary>Polskie tłumaczenie cytatu przygotowane przez model. Pokazywać z oznaczeniem, że to tłumaczenie.</summary>
    public string? QuoteTranslationPl { get; init; }

    public required Stance Stance { get; init; }
    public required string StanceLabel { get; init; }
    public required Relevance Relevance { get; init; }
    public required double Weight { get; init; }
    public string? SourceRating { get; init; }
}

public sealed record ClaimResult
{
    public required int Index { get; init; }
    public required string Text { get; init; }
    public required ClaimOutcome Outcome { get; init; }
    public EvidenceStrength? Strength { get; init; }
    public string? StrengthLabel { get; init; }

    public required string Summary { get; init; }

    public string? ModelInterpretation { get; init; }

    public IReadOnlyList<string> Premises { get; init; } = [];

    public IReadOnlyList<EvidenceItem> Evidence { get; init; } = [];

    /// <summary>
    /// Materiały o tle sprawy (to samo wydarzenie, te same osoby), które nie potwierdzają
    /// ani nie obalają twierdzenia. Nie wpływają na werdykt.
    /// </summary>
    public IReadOnlyList<EvidenceItem> Context { get; init; } = [];

    public IReadOnlyList<string> Unknowns { get; init; } = [];
    public required RiskLevel Risk { get; init; }

    /// <summary>Łańcuch dowodowy: od twierdzenia przez wyszukiwanie i ocenę fragmentów do reguły i wyniku.</summary>
    public EvidenceChain? Chain { get; init; }
}

public enum ChainRole
{
    Supports,
    Refutes,
    Misleading,
    Context,
    Rejected,
}

public enum Agreement
{
    /// <summary>Wszystkie uwzględnione źródła wskazują w tę samą stronę.</summary>
    Consistent,

    /// <summary>Część źródeł potwierdza, część przeczy lub wskazuje na manipulację.</summary>
    Conflicting,

    /// <summary>Brak uwzględnionych materiałów.</summary>
    NoEvidence,
}

public sealed record EvidenceChain
{
    public required string Narrative { get; init; }

    public IReadOnlyList<ChainStep> Steps { get; init; } = [];

    /// <summary>Wszystkie ocenione fragmenty, także odrzucone, z powodem odrzucenia.</summary>
    public IReadOnlyList<ChainLink> Links { get; init; } = [];

    public SourceComparison? Comparison { get; init; }
}

public sealed record ChainStep
{
    public required int Order { get; init; }

    public required string Stage { get; init; }

    public required string Title { get; init; }
    public IReadOnlyList<string> Details { get; init; } = [];
}

public sealed record ChainLink
{
    public required string PassageId { get; init; }
    public required ChainRole Role { get; init; }
    public required string RoleLabel { get; init; }
    public required string SourceName { get; init; }
    public required string Publisher { get; init; }
    public required string SourceTypeLabel { get; init; }
    public required string OwnershipGroup { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset? Date { get; init; }
    public string? Language { get; init; }
    public string? Quote { get; init; }
    public string? QuoteTranslationPl { get; init; }
    public double Weight { get; init; }

    public string? WeightFormula { get; init; }

    public bool CountedInVerdict { get; init; }

    public string? Note { get; init; }
}

public sealed record SourceComparison
{
    public required Agreement Agreement { get; init; }
    public required string AgreementLabel { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> Supporting { get; init; } = [];
    public IReadOnlyList<string> Refuting { get; init; } = [];
    public IReadOnlyList<string> Misleading { get; init; } = [];
    public IReadOnlyList<string> ContextOnly { get; init; } = [];

    /// <summary>Opisy sprzeczności, np. "CBOS potwierdza, a Interia przeczy".</summary>
    public IReadOnlyList<string> Conflicts { get; init; } = [];
}

public sealed record InputSummary
{
    public required string Kind { get; init; }
    public string? Text { get; init; }
    public string? Url { get; init; }
    public string? PageTitle { get; init; }
    public string? PagePublisher { get; init; }
    public DateTimeOffset? PagePublishedAt { get; init; }
}

public sealed record SourceCoverage
{
    public required string SourceId { get; init; }
    public required string Name { get; init; }
    public required SourceQueryStatus Status { get; init; }
    public string? Message { get; init; }
}

public sealed record CheckResult
{
    public required Guid Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public required string Channel { get; init; }
    public required InputSummary Input { get; init; }

    public StatusInfo? Status { get; init; }

    public IReadOnlyList<ClaimResult> Claims { get; init; } = [];
    public IReadOnlyList<SourceCoverage> SourcesConsulted { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public bool InjectionSuspected { get; init; }
}
