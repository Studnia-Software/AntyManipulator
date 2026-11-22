namespace AntyManipulator.Core.Domain;

/// <summary>Opis źródła z rejestru. Pokazywany użytkownikowi jako jawny zakres wiedzy systemu.</summary>
public sealed record SourceDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required SourceType Type { get; init; }
    public required string Language { get; init; }
    public required string BaseUrl { get; init; }

    /// <summary>Wiarygodność 0..1 przypisana jawnie w konfiguracji.</summary>
    public required double Reliability { get; init; }

    /// <summary>Źródła z tej samej grupy nie są liczone jako niezależne potwierdzenia.</summary>
    public required string OwnershipGroup { get; init; }

    public required bool Enabled { get; init; }
    public string? Description { get; init; }
    public string? CoverageNote { get; init; }
    public string? DisabledReason { get; init; }
}

/// <summary>Materiał źródłowy zwrócony przez adapter źródła (dane, nie instrukcje).</summary>
public sealed record SourceDocument
{
    public required SourceDescriptor Source { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>"publikacja" albo "ostatnia edycja".</summary>
    public string DateKind { get; init; } = "publikacja";

    /// <summary>Fragment treści przekazany modelowi do oceny.</summary>
    public required string Snippet { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Język materiału, jeśli znany (np. język zapytania, które go znalazło). Null = język źródła.</summary>
    public string? Language { get; init; }

    public string EffectiveLanguage => Language ?? Source.Language;
}

/// <summary>Zapytanie wyszukiwania w konkretnym języku (kod ISO 639-1, np. "pl", "en", "hu").</summary>
public sealed record SearchQuery(string Language, string Text)
{
    public static IReadOnlyList<string> For(IEnumerable<SearchQuery> queries, string language) =>
        queries.Where(q => string.Equals(q.Language, language, StringComparison.OrdinalIgnoreCase)).Select(q => q.Text).ToList();

    /// <summary>Języki w kolejności: najpierw pl, potem en, potem pozostałe w kolejności od modelu.</summary>
    public static IReadOnlyList<string> Languages(IEnumerable<SearchQuery> queries) =>
        queries.Select(q => q.Language.ToLowerInvariant())
            .Distinct()
            .OrderBy(l => l == "pl" ? 0 : l == "en" ? 1 : 2)
            .ToList();
}

public sealed record EvidenceQuery
{
    public required string ClaimText { get; init; }

    public required IReadOnlyList<SearchQuery> Queries { get; init; }

    public IReadOnlyList<string> QueriesPl => SearchQuery.For(Queries, "pl");

    public IReadOnlyList<string> QueriesEn => SearchQuery.For(Queries, "en");

    public IReadOnlyList<string> For(string language) => SearchQuery.For(Queries, language);

    public IReadOnlyList<string> Languages => SearchQuery.Languages(Queries);

    /// <summary>Treść twierdzenia i wszystkie zapytania, do dopasowania wyników niezależnie od języka.</summary>
    public string AllText => string.Join(' ', new[] { ClaimText }.Concat(Queries.Select(q => q.Text)));

    /// <summary>
    /// Domeny, których nie wolno użyć jako dowodu, np. strona, którą użytkownik właśnie sprawdza
    /// (artykuł nie może potwierdzać sam siebie).
    /// </summary>
    public IReadOnlyList<string> ExcludedDomains { get; init; } = [];
}

public sealed record SourceQueryLog
{
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public required SourceQueryStatus Status { get; init; }
    public IReadOnlyList<string> Queries { get; init; } = [];
    public int ResultCount { get; init; }
    public string? Message { get; init; }
    public long DurationMs { get; init; }
}
