namespace AntyManipulator.Api.Requests;

public record CheckLinkRequest
{
    public required string LinkToCheck { get; init; }
}