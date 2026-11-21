namespace AntyManipulator.Core.Domain;

/// <summary>Zgłoszenie znormalizowane niezależnie od kanału (WWW, bot, SMS...).</summary>
public sealed record CheckRequest
{
    public required string Channel { get; init; }

    /// <summary>Pseudonim użytkownika (hash), nigdy surowy identyfikator.</summary>
    public required string UserHash { get; init; }

    public string? Text { get; init; }

    public string? Url { get; init; }
}
