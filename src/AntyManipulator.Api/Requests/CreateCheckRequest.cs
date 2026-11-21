namespace AntyManipulator.Api.Requests;

/// <summary>Treść do sprawdzenia: tekst, adres URL albo oba.</summary>
public record CreateCheckRequest
{
    public string? Text { get; init; }

    public string? Url { get; init; }
}
