using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Core.Options;

public sealed class PipelineOptions
{
    public const string Section = "Pipeline";

    [Range(100, 20000)]
    public int MaxInputChars { get; set; } = 5000;

    /// <summary>Ile znaków treści (tekst + strona) trafia do ekstrakcji twierdzeń.</summary>
    [Range(500, 30000)]
    public int MaxMaterialChars { get; set; } = 6000;

    [Range(1, 10)]
    public int MaxClaims { get; set; } = 3;

    [Range(1, 20)]
    public int MaxPassagesPerClaim { get; set; } = 6;

    [Range(1, 10)]
    public int MaxPassagesPerSource { get; set; } = 2;

    [Range(100, 3000)]
    public int PassageChars { get; set; } = 600;

    [Range(1, 60)]
    public int SourceTimeoutSeconds { get; set; } = 12;

    [Range(10, 600)]
    public int CheckTimeoutSeconds { get; set; } = 120;

    public List<string> SearchLanguages { get; set; } = ["pl", "en", "de", "fr", "es", "it", "uk", "ru", "cs", "sk", "hu", "lt", "be"];

    [Range(0, 5)]
    public int MaxExtraLanguages { get; set; } = 2;

    public IReadOnlyList<string> AllowedLanguages() =>
        new[] { "pl", "en" }
            .Concat(SearchLanguages.Select(l => l.Trim().ToLowerInvariant()))
            .Where(l => l.Length is >= 2 and <= 3 && l.All(char.IsAsciiLetterLower))
            .Distinct()
            .ToList();
}

public sealed class AlertOptions
{
    public const string Section = "Alerts";

    /// <summary>Ile różnych osób musi zadać podobne pytanie bez odpowiedzi, by wysłać alert.</summary>
    [Range(2, 1000)]
    public int RepeatedThreshold { get; set; } = 3;

    [Range(1, 24 * 30)]
    public int RepeatedWindowHours { get; set; } = 24;

    /// <summary>Próg podobieństwa (Jaccard na tokenach) dla "podobnych" pytań.</summary>
    [Range(0.1, 1.0)]
    public double SimilarityThreshold { get; set; } = 0.5;

    public List<AlertRecipient> Recipients { get; set; } = [];
}

public sealed class AlertRecipient
{
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>Np. "moderator", "analityk".</summary>
    [Required]
    public string Role { get; set; } = string.Empty;

    /// <summary>Adres webhooka (HTTPS). Pusty = tylko zapis w logu alertów.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Typy alertów: "HighRisk", "Repeated". Puste = wszystkie.</summary>
    public List<string> AlertTypes { get; set; } = [];
}

/// <summary>
/// Grupy wydawnicze: domeny jednego właściciela. Służą do dwóch rzeczy:
/// strona z tej samej grupy co sprawdzany artykuł nie może być dla niego dowodem,
/// a kilka stron jednej grupy liczy się jako jedno źródło.
/// </summary>
public sealed class PublisherGroupsOptions
{
    public const string Section = "PublisherGroups";

    public List<PublisherGroup> Groups { get; set; } = [];

    public PublisherGroup? GroupOf(string? host) =>
        host is null
            ? null
            : Groups.FirstOrDefault(g => g.Domains.Any(d => AntyManipulator.Core.Text.Domains.Matches(host, d)));
}

public sealed class PublisherGroup
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    public List<string> Domains { get; set; } = [];
}
