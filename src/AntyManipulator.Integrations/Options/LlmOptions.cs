using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Integrations.Options;

public sealed class LlmOptions
{
    public const string Section = "Llm";

    [Required, Url]
    public string Uri { get; set; } = string.Empty;

    /// <summary>Trzymaj w user-secrets lub zmiennej środowiskowej Llm__ApiKey, nie w appsettings.json.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>Np. "low" dla modeli rozumujących (gpt-oss). Puste = nie wysyłaj parametru.</summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>"JsonSchema" (structured outputs, strict) albo "JsonObject".</summary>
    public string ResponseFormat { get; set; } = "JsonSchema";

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Ile razy ponowić po HTTP 429, jeśli dostawca poda krótki czas oczekiwania.</summary>
    [Range(0, 5)]
    public int MaxRateLimitRetries { get; set; } = 2;

    [Range(1, 120)]
    public int MaxRetryDelaySeconds { get; set; } = 20;

    public Dictionary<string, LlmProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ResolvedLlmProfile Resolve(string purpose)
    {
        Profiles.TryGetValue(purpose, out var p);
        return new ResolvedLlmProfile(
            Uri: Pick(p?.Uri, Uri),
            ApiKey: Pick(p?.ApiKey, ApiKey),
            Model: Pick(p?.Model, Model),
            ReasoningEffort: p?.ReasoningEffort ?? ReasoningEffort,
            ResponseFormat: Pick(p?.ResponseFormat, ResponseFormat));
    }

    private static string Pick(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}

public sealed class LlmProfile
{
    public string? Uri { get; set; }
    public string? ApiKey { get; set; }
    public string? Model { get; set; }

    public string? ReasoningEffort { get; set; }

    public string? ResponseFormat { get; set; }
}

public sealed record ResolvedLlmProfile(string Uri, string ApiKey, string Model, string? ReasoningEffort, string ResponseFormat);
