using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Integrations.Options;

/// <summary>Google Fact Check Tools API (claims:search), czyli weryfikacje ClaimReview z wielu serwisów.</summary>
public sealed class GoogleFactCheckOptions
{
    public const string Section = "GoogleFactCheck";

    [Required, Url]
    public string Endpoint { get; set; } = "https://factchecktools.googleapis.com/v1alpha1/claims:search";

    /// <summary>Klucz z Google Cloud (user-secrets: GoogleFactCheck:ApiKey). Pusty = źródło wyłączone.</summary>
    public string? ApiKey { get; set; }

    [Range(1, 50)]
    public int PageSize { get; set; } = 5;

    /// <summary>Maksymalny wiek weryfikacji w dniach. Puste = bez limitu.</summary>
    [Range(1, 36500)]
    public int? MaxAgeDays { get; set; }
}
