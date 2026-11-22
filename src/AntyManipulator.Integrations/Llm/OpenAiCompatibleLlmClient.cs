using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Llm;

/// <summary>
/// Klient Chat Completions wymuszający odpowiedź JSON zgodną ze schematem.
/// Model nie dostaje narzędzi, więc nawet zmanipulowany może zwrócić tylko dane,
/// które kod i tak waliduje.
/// </summary>
public sealed class OpenAiCompatibleLlmClient(
    HttpClient http,
    IOptions<LlmOptions> options,
    ILogger<OpenAiCompatibleLlmClient> logger) : ILlmClient
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public string ModelFor(string purpose) => options.Value.Resolve(purpose).Model;

    public async Task<T> CompleteJsonAsync<T>(LlmJsonRequest request, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var profile = opts.Resolve(request.Purpose);
        var body = BuildBody(request, profile);

        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, profile.Uri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);

            HttpResponseMessage response;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(opts.TimeoutSeconds));
            try
            {
                response = await http.SendAsync(message, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LlmException(LlmFailureKind.Unavailable, "LLM request timed out");
            }
            catch (HttpRequestException ex)
            {
                throw new LlmException(LlmFailureKind.Unavailable, "LLM request failed: " + ex.Message, ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var delay = RetryDelay(response);
                    if (attempt < opts.MaxRateLimitRetries && delay <= TimeSpan.FromSeconds(opts.MaxRetryDelaySeconds))
                    {
                        logger.LogInformation("LLM rate limited, retrying in {Delay}", delay);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    throw new LlmException(LlmFailureKind.RateLimited, "LLM rate limit exceeded");
                }

                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("LLM returned {Status}: {Body}", (int)response.StatusCode, Truncate(raw, 500));
                    var kind = response.StatusCode == HttpStatusCode.BadRequest ? LlmFailureKind.InvalidOutput : LlmFailureKind.Unavailable;
                    throw new LlmException(kind, $"LLM HTTP {(int)response.StatusCode}");
                }

                return Parse<T>(raw);
            }
        }
    }

    private static string BuildBody(LlmJsonRequest request, ResolvedLlmProfile opts)
    {
        var system = request.SystemPrompt;
        JsonObject responseFormat;
        if (string.Equals(opts.ResponseFormat, "JsonObject", StringComparison.OrdinalIgnoreCase))
        {
            system += "\n\nJSON schema of the response:\n" + request.SchemaJson;
            responseFormat = new JsonObject { ["type"] = "json_object" };
        }
        else
        {
            responseFormat = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = request.SchemaName,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(request.SchemaJson),
                },
            };
        }

        var body = new JsonObject
        {
            ["model"] = opts.Model,
            ["temperature"] = 0,
            ["max_completion_tokens"] = request.MaxOutputTokens,
            ["response_format"] = responseFormat,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt },
            },
        };

        if (!string.IsNullOrWhiteSpace(opts.ReasoningEffort))
        {
            body["reasoning_effort"] = opts.ReasoningEffort;
        }

        return body.ToJsonString();
    }

    private T Parse<T>(string raw)
    {
        try
        {
            var root = JsonNode.Parse(raw);
            var content = root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new LlmException(LlmFailureKind.InvalidOutput, "LLM returned empty content");
            }

            content = StripCodeFence(content);
            return JsonSerializer.Deserialize<T>(content, ReadOptions)
                   ?? throw new LlmException(LlmFailureKind.InvalidOutput, "LLM returned null JSON");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "LLM returned invalid JSON: {Body}", Truncate(raw, 500));
            throw new LlmException(LlmFailureKind.InvalidOutput, "LLM returned invalid JSON", ex);
        }
    }

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? trimmed[(firstNewline + 1)..lastFence].Trim() : trimmed;
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            return date - DateTimeOffset.UtcNow;
        }

        return TimeSpan.FromSeconds(5);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
