using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AntyManipulator.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace AntyManipulator.Api.Controllers;

/// <summary>
/// Eksport zapisanych przebiegów sprawdzeń i alertów do narzędzi analitycznych.
/// Dane są strumieniowane prosto z plików JSONL, więc eksport dużego zakresu nie ładuje wszystkiego do pamięci.
/// </summary>
public static class ExportController
{
    private const int MaxRangeDays = 92;

    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static RouteGroupBuilder MapExport(this RouteGroupBuilder admin)
    {
        admin.MapGet("/export", Export)
            .WithSummary("Eksport sprawdzeń lub alertów do JSON / JSON Lines")
            .WithDescription(
                "type: checks | alerts (domyślnie checks). " +
                "shape: full = pełne rekordy audytu z łańcuchem dowodowym, claims = jeden płaski wiersz na twierdzenie (do arkuszy i BI). " +
                "format: json = tablica JSON, jsonl = jeden rekord na linię. " +
                "from, to: daty RRRR-MM-DD (UTC), domyślnie dziś, zakres najwyżej 92 dni. channel: filtr kanału (web, discord).");
        return admin;
    }

    private static IResult Export(
        [FromServices] IAuditReader reader,
        [FromServices] TimeProvider time,
        string? type,
        string? shape,
        string? format,
        string? from,
        string? to,
        string? channel)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var errors = new Dictionary<string, string[]>();

        var kind = (type ?? "checks").ToLowerInvariant() switch
        {
            "checks" => AuditKind.Checks,
            "alerts" => AuditKind.Alerts,
            _ => (AuditKind?)null,
        };
        if (kind is null)
        {
            errors["type"] = ["Dozwolone: checks, alerts."];
        }

        var flat = (shape ?? "full").ToLowerInvariant() switch
        {
            "full" => false,
            "claims" => true,
            _ => (bool?)null,
        };
        if (flat is null)
        {
            errors["shape"] = ["Dozwolone: full, claims."];
        }
        else if (flat == true && kind == AuditKind.Alerts)
        {
            errors["shape"] = ["Kształt \"claims\" dotyczy tylko sprawdzeń (type=checks)."];
        }

        var lines = (format ?? "json").ToLowerInvariant() switch
        {
            "json" => false,
            "jsonl" => true,
            _ => (bool?)null,
        };
        if (lines is null)
        {
            errors["format"] = ["Dozwolone: json, jsonl."];
        }

        var fromDate = ParseDate(from, today, "from", errors);
        var toDate = ParseDate(to, today, "to", errors);
        if (fromDate > toDate)
        {
            errors["from"] = ["Data początkowa jest późniejsza niż końcowa."];
        }
        else if (toDate.DayNumber - fromDate.DayNumber >= MaxRangeDays)
        {
            errors["to"] = [$"Zakres może mieć najwyżej {MaxRangeDays} dni."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var extension = lines == true ? "jsonl" : "json";
        var fileName = $"{(kind == AuditKind.Alerts ? "alerts" : flat == true ? "claims" : "checks")}-{fromDate:yyyy-MM-dd}_{toDate:yyyy-MM-dd}.{extension}";
        var contentType = lines == true ? "application/x-ndjson" : "application/json";

        return Results.Stream(async stream =>
        {
            var ct = CancellationToken.None;
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            if (lines == false)
            {
                await writer.WriteAsync('[');
            }

            var first = true;
            await foreach (var line in reader.ReadLinesAsync(kind!.Value, fromDate, toDate, ct))
            {
                JsonNode? record;
                try
                {
                    record = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue; // np. linia w trakcie zapisu
                }

                if (record is null || (!string.IsNullOrWhiteSpace(channel)
                                       && !string.Equals(Str(record["channel"]), channel, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                IEnumerable<JsonNode> outputs = flat == true ? FlattenClaims(record).Cast<JsonNode>() : new[] { record };
                foreach (var output in outputs)
                {
                    if (lines == false && !first)
                    {
                        await writer.WriteAsync(',');
                    }

                    await writer.WriteAsync(output.ToJsonString(Compact));
                    if (lines == true)
                    {
                        await writer.WriteAsync('\n');
                    }

                    first = false;
                }
            }

            if (lines == false)
            {
                await writer.WriteAsync(']');
            }

            await writer.FlushAsync(ct);
        }, contentType, fileName);
    }

    /// <summary>Jeden płaski wiersz na twierdzenie: najważniejsze pola do arkusza, BI lub dalszej analizy.</summary>
    private static IEnumerable<JsonObject> FlattenClaims(JsonNode record)
    {
        var result = record["result"];
        var claims = result?["claims"] as JsonArray;
        var common = new JsonObject
        {
            ["checkId"] = Str(record["checkId"]),
            ["startedAt"] = Str(record["startedAt"]),
            ["durationMs"] = record["durationMs"]?.DeepClone(),
            ["channel"] = Str(record["channel"]),
            ["userHash"] = Str(record["userHash"]),
            ["inputKind"] = Str(result?["input"]?["kind"]),
            ["inputUrl"] = Str(record["inputUrl"]),
            ["inputText"] = Str(record["inputText"]),
            ["injectionSuspected"] = result?["injectionSuspected"]?.DeepClone() ?? false,
            ["model"] = Str(record["model"]),
            ["promptVersion"] = Str(record["promptVersion"]),
            ["rulesVersion"] = Str(record["rulesVersion"]),
        };

        if (claims is null || claims.Count == 0)
        {
            // Sprawdzenie bez twierdzeń (np. awaria, potrzeba doprecyzowania) też jest wierszem.
            var row = (JsonObject)common.DeepClone();
            row["claimIndex"] = null;
            row["outcomeKind"] = "Status";
            row["outcome"] = Str(result?["status"]?["status"]);
            row["outcomeLabel"] = Str(result?["status"]?["label"]);
            yield return row;
            yield break;
        }

        foreach (var claim in claims)
        {
            var row = (JsonObject)common.DeepClone();
            var outcome = claim?["outcome"];
            var evidence = claim?["evidence"] as JsonArray ?? new JsonArray();
            var comparison = claim?["chain"]?["comparison"];

            row["claimIndex"] = claim?["index"]?.DeepClone();
            row["claimText"] = Str(claim?["text"]);
            row["outcomeKind"] = Str(outcome?["kind"]);
            row["outcome"] = Str(outcome?["verdict"]) ?? Str(outcome?["status"]);
            row["outcomeLabel"] = Str(outcome?["label"]);
            row["strength"] = Str(claim?["strength"]);
            row["risk"] = Str(claim?["risk"]);
            row["evidenceCount"] = evidence.Count;
            row["evidenceUrls"] = new JsonArray(evidence.Select(e => (JsonNode?)Str(e?["url"])).ToArray());
            row["agreement"] = Str(comparison?["agreement"]);
            row["supporting"] = comparison?["supporting"]?.DeepClone() ?? new JsonArray();
            row["refuting"] = comparison?["refuting"]?.DeepClone() ?? new JsonArray();
            row["misleading"] = comparison?["misleading"]?.DeepClone() ?? new JsonArray();
            row["languages"] = new JsonArray(evidence
                .Select(e => Str(e?["language"]))
                .Where(l => l is not null)
                .Distinct()
                .Select(l => (JsonNode?)l)
                .ToArray());
            yield return row;
        }
    }

    private static DateOnly ParseDate(string? value, DateOnly fallback, string name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[name] = ["Podaj datę w formacie RRRR-MM-DD."];
        return fallback;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node?.ToJsonString();
}
