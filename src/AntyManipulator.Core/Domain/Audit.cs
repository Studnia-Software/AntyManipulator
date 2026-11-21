namespace AntyManipulator.Core.Domain;

/// <summary>Zapis przebiegu sprawdzenia (pytanie, czas, źródła, wynik, wersje).</summary>
public sealed record AuditRecord
{
    public required Guid CheckId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required long DurationMs { get; init; }
    public required string Channel { get; init; }
    public required string UserHash { get; init; }
    public string? InputText { get; init; }
    public string? InputUrl { get; init; }
    public string? FetchedTextExcerpt { get; init; }
    public required string Model { get; init; }
    public required string PromptVersion { get; init; }
    public required string RulesVersion { get; init; }
    public IReadOnlyList<string> InjectionSignals { get; init; } = [];
    public IReadOnlyList<ClaimTrace> ClaimTraces { get; init; } = [];
    public IReadOnlyList<StepTrace> Steps { get; init; } = [];
    public required CheckResult Result { get; init; }
}

public sealed record ClaimTrace
{
    public required int ClaimIndex { get; init; }
    public IReadOnlyList<SourceQueryLog> SourceQueries { get; init; } = [];
    public int CandidateCount { get; init; }
    public int PassagesAssessed { get; init; }
    public int QuotesRejected { get; init; }

    /// <summary>Ocena każdego fragmentu, także odrzuconych, żeby dało się zrozumieć werdykt.</summary>
    public IReadOnlyList<PassageTrace> Passages { get; init; } = [];
}

public sealed record PassageTrace
{
    public required string Source { get; init; }
    public required string Url { get; init; }
    public required string OwnershipGroup { get; init; }
    public required double Reliability { get; init; }
    public required Stance Stance { get; init; }
    public required Relevance Relevance { get; init; }
    public bool IsContext { get; init; }
    public bool QuoteVerified { get; init; }
}

public sealed record StepTrace
{
    public required string Step { get; init; }
    public required bool Success { get; init; }
    public required long DurationMs { get; init; }
    public string? Error { get; init; }
}

public sealed record Alert
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>"HighRisk" albo "Repeated".</summary>
    public required string Type { get; init; }

    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string ClaimText { get; init; }
    public required Guid CheckId { get; init; }
    public int DistinctUsers { get; init; }
    public IReadOnlyList<string> Examples { get; init; } = [];
    public IReadOnlyList<string> NotifiedRecipients { get; init; } = [];
}
