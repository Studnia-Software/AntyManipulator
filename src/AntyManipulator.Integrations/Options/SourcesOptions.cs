using System.ComponentModel.DataAnnotations;
using AntyManipulator.Core.Domain;

namespace AntyManipulator.Integrations.Options;

/// <summary>Jawny rejestr źródeł, z których system korzysta.</summary>
public sealed class SourcesOptions
{
    public const string Section = "Sources";

    [Range(1, 1440)]
    public int CacheMinutes { get; set; } = 30;

    [Range(1, 10)]
    public int MaxResultsPerSource { get; set; } = 3;

    public List<SourceDefinition> Definitions { get; set; } = [];
}

/// <summary>
/// Nazwy wbudowanych adapterów. Pole "Adapter" w konfiguracji to zwykły tekst, więc nowy adapter
/// dodaje się przez rejestrację fabryki (<c>services.AddEvidenceSourceAdapter&lt;T&gt;()</c>), bez zmian tutaj.
/// </summary>
public static class SourceAdapters
{
    /// <summary>Wyszukiwarka WordPressa z wynikami jako RSS: {BaseUrl}?s=zapytanie&amp;feed=rss2.</summary>
    public const string WordPressSearchFeed = "WordPressSearchFeed";

    /// <summary>WordPress REST API: {BaseUrl}wp-json/wp/v2/posts?search=zapytanie.</summary>
    public const string WordPressRest = "WordPressRest";

    /// <summary>Kanał RSS z najnowszymi artykułami, filtrowany lokalnie po słowach kluczowych.</summary>
    public const string RssLatest = "RssLatest";

    /// <summary>API MediaWiki (Wikipedia w dowolnym języku).</summary>
    public const string MediaWiki = "MediaWiki";

    /// <summary>Ogólne wyszukiwanie w internecie przez Tavily. Ustawienia w sekcji "WebSearch".</summary>
    public const string TavilyWebSearch = "TavilyWebSearch";

    /// <summary>Google Fact Check Tools API. Ustawienia w sekcji "GoogleFactCheck".</summary>
    public const string GoogleFactCheck = "GoogleFactCheck";

    /// <summary>Adaptery, które zwracają materiały wielu wydawców (agregatory), a nie jednego.</summary>
    public static bool IsAggregator(string adapter) =>
        string.Equals(adapter, TavilyWebSearch, StringComparison.OrdinalIgnoreCase)
        || string.Equals(adapter, GoogleFactCheck, StringComparison.OrdinalIgnoreCase);
}

public sealed class SourceDefinition
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Publisher { get; set; } = string.Empty;

    public SourceType Type { get; set; }

    /// <summary>Nazwa adaptera (zob. <see cref="SourceAdapters"/> lub własna fabryka).</summary>
    [Required]
    public string Adapter { get; set; } = string.Empty;

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Dla adaptera RssLatest.</summary>
    public string? FeedUrl { get; set; }

    /// <summary>Główny język źródła (ISO 639-1).</summary>
    [Required]
    public string Language { get; set; } = "pl";

    /// <summary>
    /// Dla źródeł wielojęzycznych (wyszukiwarka internetowa, Google Fact Check): języki odpytywane zawsze.
    /// Do nich dochodzą języki dodatkowe, które model dobrał do twierdzenia (np. "hu" dla spraw węgierskich).
    /// Puste = tylko <see cref="Language"/>.
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>Czy dokładać języki dodatkowe dobrane przez model (dotyczy źródeł wielojęzycznych).</summary>
    public bool UseClaimLanguages { get; set; }

    /// <summary>Najwięcej języków odpytywanych dla jednego twierdzenia (każdy to osobne zapytanie).</summary>
    [Range(1, 6)]
    public int MaxLanguages { get; set; } = 3;

    [Range(0.0, 1.0)]
    public double Reliability { get; set; } = 0.5;

    [Required]
    public string OwnershipGroup { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? Description { get; set; }

    public string? CoverageNote { get; set; }

    public string? DisabledReason { get; set; }

    public SourceDescriptor ToDescriptor() => new()
    {
        Id = Id,
        Name = Name,
        Publisher = Publisher,
        Type = Type,
        Language = Language,
        BaseUrl = BaseUrl,
        Reliability = Reliability,
        OwnershipGroup = OwnershipGroup,
        Enabled = Enabled,
        Description = Description,
        CoverageNote = CoverageNote,
        DisabledReason = DisabledReason,
    };
}
