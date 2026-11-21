using System.ComponentModel.DataAnnotations;
using AntyManipulator.Core.Domain;

namespace AntyManipulator.Integrations.Options;

/// <summary>
/// Ustawienia wyszukiwania w internecie. Wiarygodność stron jest jawna: domeny z listy
/// TrustedDomains mają przypisaną wagę, pozostałe dostają DefaultReliability.
/// </summary>
public sealed class WebSearchOptions
{
    public const string Section = "WebSearch";

    [Required, Url]
    public string Endpoint { get; set; } = "https://api.tavily.com/search";

    /// <summary>Klucz Tavily (user-secrets: WebSearch:ApiKey). Pusty = darmowy tryb keyless z niskim limitem.</summary>
    public string? ApiKey { get; set; }

    /// <summary>basic, fast, ultra-fast (1 kredyt) albo advanced (2 kredyty).</summary>
    [Required]
    public string SearchDepth { get; set; } = "basic";

    [Range(1, 20)]
    public int MaxResults { get; set; } = 6;

    [Range(1, 3)]
    public int ChunksPerSource { get; set; } = 2;

    /// <summary>Kraj, którego wyniki są promowane (np. "poland"). Pusty = brak. W testach "poland" zwracał 0 wyników.</summary>
    public string? Country { get; set; }

    /// <summary>Waga stron spoza listy zaufanych. Przy progach werdyktu takie strony same nie rozstrzygają.</summary>
    [Range(0.0, 1.0)]
    public double DefaultReliability { get; set; } = 0.4;

    /// <summary>Domeny pomijane zawsze (np. media społecznościowe, gdzie krążą same pogłoski).</summary>
    public List<string> ExcludedDomains { get; set; } = [];

    public List<TrustedDomain> TrustedDomains { get; set; } = [];
}

public sealed class TrustedDomain
{
    /// <summary>Domena wraz z subdomenami, np. "gov.pl" obejmuje też "pacjent.gov.pl".</summary>
    [Required]
    public string Domain { get; set; } = string.Empty;

    [Required]
    public string Publisher { get; set; } = string.Empty;

    public SourceType Type { get; set; } = SourceType.Official;

    [Range(0.0, 1.0)]
    public double Reliability { get; set; } = 0.8;

    /// <summary>Grupa właścicielska, jeśli domena należy do tego samego wydawcy co inne źródło (np. "tvn-wbd").</summary>
    public string? OwnershipGroup { get; set; }
}
