using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Integrations.Options;

public sealed class FetchOptions
{
    public const string Section = "Fetch";

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;

    [Range(10_000, 20_000_000)]
    public int MaxBytes { get; set; } = 3_000_000;

    [Range(0, 10)]
    public int MaxRedirects { get; set; } = 3;

    /// <summary>Nazwa i wersja klienta w nagłówku User-Agent.</summary>
    [Required]
    public string UserAgent { get; set; } = "AntyManipulator/0.1";

    /// <summary>
    /// Kontakt do operatora (e-mail lub URL projektu) dopisywany do User-Agent.
    /// Wikimedia bez kontaktu traktuje klienta jako niezidentyfikowanego: 10 zapytań/min zamiast 200.
    /// </summary>
    public string? Contact { get; set; }

    /// <summary>Maksymalna liczba równoległych zapytań do jednego hosta źródła (Wikimedia zaleca ≤ 3).</summary>
    [Range(1, 10)]
    public int MaxConcurrentPerHost { get; set; } = 2;

    /// <summary>Najdłuższy czas, przez jaki czekamy na ponowienie po 429/503 w trakcie jednego sprawdzenia.</summary>
    [Range(0, 30)]
    public int MaxRetryWaitSeconds { get; set; } = 5;

    public string FullUserAgent =>
        string.IsNullOrWhiteSpace(Contact) ? UserAgent : $"{UserAgent} ({Contact.Trim()})";
}
