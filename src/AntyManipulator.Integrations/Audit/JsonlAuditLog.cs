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
public sealed class JsonlAuditLog(IOptions<AuditOptions> options, TimeProvider time) : IAuditLog, IAuditReader
{
    /// <summary>
    /// Czyta pliki dzień po dniu (UTC). Plik otwierany z FileShare.ReadWrite, żeby nie blokować
    /// bieżącego zapisu; ewentualnie niedokończona ostatnia linia jest odrzucana przez eksport.
    /// </summary>
    public async IAsyncEnumerable<string> ReadLinesAsync(
        AuditKind kind,
        DateOnly from,
        DateOnly to,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prefix = kind == AuditKind.Alerts ? "alerts" : "checks";
        var dir = Path.GetFullPath(options.Value.Directory);

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var path = Path.Combine(dir, $"{prefix}-{day:yyyy-MM-dd}.jsonl");
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length > 0)
                {
                    yield return line;
                }
            }
        }
    }

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
