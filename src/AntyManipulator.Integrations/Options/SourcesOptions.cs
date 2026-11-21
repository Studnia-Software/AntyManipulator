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

public enum SourceAdapter
{
    /// <summary>Wyszukiwarka WordPressa z wynikami jako RSS: {BaseUrl}?s=zapytanie&amp;feed=rss2.</summary>
    WordPressSearchFeed,

    /// <summary>WordPress REST API: {BaseUrl}wp-json/wp/v2/posts?search=zapytanie.</summary>
    WordPressRest,

    /// <summary>Kanał RSS z najnowszymi artykułami, filtrowany lokalnie po słowach kluczowych.</summary>
    RssLatest,

    /// <summary>API MediaWiki (Wikipedia).</summary>
    MediaWiki,

    /// <summary>Ogólne wyszukiwanie w internecie przez Tavily. Ustawienia w sekcji "WebSearch".</summary>
    TavilyWebSearch,

    /// <summary>Google Fact Check Tools API. Ustawienia w sekcji "GoogleFactCheck".</summary>
    GoogleFactCheck,
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

    public SourceAdapter Adapter { get; set; }

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Dla adaptera RssLatest.</summary>
    public string? FeedUrl { get; set; }

    [Required]
    public string Language { get; set; } = "pl";

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
