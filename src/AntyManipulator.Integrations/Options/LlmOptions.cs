using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Integrations.Options;

/// <summary>Dowolny dostawca zgodny z OpenAI Chat Completions (Groq, Scaleway, ...).</summary>
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
}
