using AntyManipulator.Core.Domain;

namespace AntyManipulator.Core.Abstractions;

/// <summary>Model językowy zwracający wyłącznie JSON zgodny ze schematem. Nie ma narzędzi ani uprawnień.</summary>
public interface ILlmClient
{
    string ModelName { get; }

    Task<T> CompleteJsonAsync<T>(LlmJsonRequest request, CancellationToken cancellationToken);
}

public sealed record LlmJsonRequest
{
    public required string SchemaName { get; init; }
    public required string SchemaJson { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public int MaxOutputTokens { get; init; } = 2000;
}

public enum LlmFailureKind
{
    RateLimited,
    Unavailable,
    InvalidOutput,
}

public sealed class LlmException(LlmFailureKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public LlmFailureKind Kind { get; } = kind;
}

public interface IContentFetcher
{
    Task<FetchedContent> FetchAsync(Uri url, CancellationToken cancellationToken);
}

public sealed record FetchedContent
{
    public required Uri RequestedUrl { get; init; }
    public required Uri FinalUrl { get; init; }
    public string? Title { get; init; }
    public string? Publisher { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public required string Text { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
}

public enum FetchFailureKind
{
    InvalidUrl,
    ForbiddenAddress,
    Unreachable,
    HttpError,
    TooLarge,
    UnsupportedContent,
    NoText,
}

public sealed class ContentFetchException(FetchFailureKind kind, string userMessage, Exception? inner = null)
    : Exception(userMessage, inner)
{
    public FetchFailureKind Kind { get; } = kind;

    /// <summary>Komunikat po polsku, bezpieczny do pokazania użytkownikowi.</summary>
    public string UserMessage { get; } = userMessage;
}

/// <summary>Adapter jednego źródła z rejestru.</summary>
public interface IEvidenceSource
{
    SourceDescriptor Descriptor { get; }

    Task<SourceSearchResult> SearchAsync(EvidenceQuery query, CancellationToken cancellationToken);
}

public sealed record SourceSearchResult
{
    public required IReadOnlyList<SourceDocument> Documents { get; init; }
    public required IReadOnlyList<string> QueriesUsed { get; init; }
}

public interface ISourceRegistry
{
    /// <summary>Wszystkie źródła, także wyłączone (z powodem), do pokazania użytkownikowi.</summary>
    IReadOnlyList<SourceDescriptor> All { get; }

    IReadOnlyList<IEvidenceSource> Active { get; }
}

public interface IAuditLog
{
    Task WriteCheckAsync(AuditRecord record, CancellationToken cancellationToken);

    Task WriteAlertAsync(Alert alert, CancellationToken cancellationToken);
}

public interface IAlertNotifier
{
    /// <summary>Wysyła alert do odbiorców wskazanych przez administratora. Zwraca nazwy powiadomionych.</summary>
    Task<IReadOnlyList<string>> NotifyAsync(Alert alert, CancellationToken cancellationToken);
}
