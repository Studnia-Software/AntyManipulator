using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Audit;

public sealed class AuditOptions
{
    public const string Section = "Audit";

    /// <summary>Katalog na pliki JSON Lines (względny = względem katalogu roboczego aplikacji).</summary>
    [Required]
    public string Directory { get; set; } = "logs";
}

/// <summary>
/// Zapis przebiegu sprawdzeń do plików JSON Lines: jeden rekord na linię, tylko dopisywanie.
/// checks-RRRR-MM-DD.jsonl oraz alerts-RRRR-MM-DD.jsonl.
/// </summary>
public sealed class JsonlAuditLog(IOptions<AuditOptions> options, TimeProvider time) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task WriteCheckAsync(AuditRecord record, CancellationToken cancellationToken) =>
        AppendAsync("checks", JsonSerializer.Serialize(record, Json), cancellationToken);

    public Task WriteAlertAsync(Alert alert, CancellationToken cancellationToken) =>
        AppendAsync("alerts", JsonSerializer.Serialize(alert, Json), cancellationToken);

    private async Task AppendAsync(string prefix, string line, CancellationToken ct)
    {
        var dir = Path.GetFullPath(options.Value.Directory);
        System.IO.Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{prefix}-{time.GetUtcNow():yyyy-MM-dd}.jsonl");

        await _gate.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
