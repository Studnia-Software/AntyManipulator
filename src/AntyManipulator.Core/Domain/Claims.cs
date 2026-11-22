namespace AntyManipulator.Core.Domain;

public sealed record Claim
{
    public required int Index { get; init; }
    public required string Text { get; init; }
    public required ClaimKind Kind { get; init; }
    public required RiskLevel Risk { get; init; }
    public string? RiskReason { get; init; }
    public bool NeedsClarification { get; init; }
    public string? ClarificationQuestion { get; init; }
    /// <summary>Zapytania wyszukiwania w różnych językach (zawsze pl, zwykle en, czasem język kraju, którego sprawa dotyczy).</summary>
    public IReadOnlyList<SearchQuery> Queries { get; init; } = [];

    public IReadOnlyList<string> QueriesPl => SearchQuery.For(Queries, "pl");

    public IReadOnlyList<string> QueriesEn => SearchQuery.For(Queries, "en");

    public bool IsCheckable => Kind == ClaimKind.Factual;
}

public sealed record ClaimExtraction
{
    public required IReadOnlyList<Claim> Claims { get; init; }
    public bool InjectionSuspected { get; init; }
}

/// <summary>Ocena pojedynczego fragmentu źródła względem twierdzenia.</summary>
public sealed record AssessedEvidence
{
    public required SourceDocument Document { get; init; }
    public required Stance Stance { get; init; }
    public required Relevance Relevance { get; init; }

    /// <summary>Cytat zweryfikowany dosłownie w treści fragmentu, albo null.</summary>
    public string? Quote { get; init; }

    public bool QuoteVerified { get; init; }
    public string? SourceRating { get; init; }

    /// <summary>Fragment nie dotyczy twierdzenia, ale opisuje tło (to samo wydarzenie, osoby). Nie wpływa na werdykt.</summary>
    public bool IsContext { get; init; }

    /// <summary>Polskie tłumaczenie cytatu wykonane przez model (tylko dla cytatów w innym języku). Nie jest weryfikowane.</summary>
    public string? QuoteTranslationPl { get; init; }
}

public sealed record EvidenceAssessment
{
    public required IReadOnlyList<AssessedEvidence> Evidence { get; init; }
    public string? Explanation { get; init; }
    public IReadOnlyList<string> Unknowns { get; init; } = [];
    public bool InjectionSuspected { get; init; }
}
