using System.Diagnostics;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Core.Pipeline;

public sealed record RetrievalResult
{
    public required IReadOnlyList<SourceDocument> Passages { get; init; }
    public required IReadOnlyList<SourceQueryLog> Logs { get; init; }
    public required int CandidateCount { get; init; }

    public bool AllSourcesFailed => Logs.Count > 0 && Logs.All(l => l.Status == SourceQueryStatus.Failed);
}

public interface IEvidenceRetriever
{
    Task<RetrievalResult> RetrieveAsync(Claim claim, IReadOnlyList<string> excludedDomains, CancellationToken cancellationToken);
}

/// <summary>Odpytuje równolegle wszystkie aktywne źródła i wybiera najbardziej pasujące fragmenty.</summary>
public sealed class EvidenceRetriever(
    ISourceRegistry registry,
    IOptions<PipelineOptions> options,
    ILogger<EvidenceRetriever> logger) : IEvidenceRetriever
{
    public async Task<RetrievalResult> RetrieveAsync(Claim claim, IReadOnlyList<string> excludedDomains, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var query = new EvidenceQuery
        {
            ClaimText = claim.Text,
            Queries = claim.Queries,
            ExcludedDomains = excludedDomains,
        };

        var tasks = registry.Active.Select(source => QuerySourceAsync(source, query, opts, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks);

        // Tokeny ze wszystkich języków zapytań, żeby nie odrzucać wyników np. po węgiersku.
        var claimTokens = TextTools.Tokens(query.AllText);
        var candidates = results
            .SelectMany(r => r.Documents)
            .DistinctBy(d => d.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Where(d => !excludedDomains.Any(x => Domains.Matches(Domains.HostOf(d.Url), x)))
            .Select(d => (Doc: d, Overlap: TextTools.Overlap(claimTokens, TextTools.Tokens(d.Title + " " + d.Snippet))))
            .Where(x => x.Overlap >= 1)
            .ToList();

        var selected = candidates
            .OrderByDescending(x => x.Overlap * x.Doc.Source.Reliability)
            // Limit na wydawcę (grupę właścicielską), nie na adapter: wyszukiwarka internetowa
            // zwraca strony wielu niezależnych wydawców i każdy z nich musi mieć szansę trafić do oceny.
            .GroupBy(x => x.Doc.Source.OwnershipGroup)
            .SelectMany(g => g.Take(opts.MaxPassagesPerSource))
            .OrderByDescending(x => x.Overlap * x.Doc.Source.Reliability)
            .Take(opts.MaxPassagesPerClaim)
            .Select(x => x.Doc with { Snippet = TextTools.Truncate(x.Doc.Snippet, opts.PassageChars) })
            .ToList();

        return new RetrievalResult
        {
            Passages = selected,
            Logs = results.Select(r => r.Log).ToList(),
            CandidateCount = candidates.Count,
        };
    }

    private async Task<(IReadOnlyList<SourceDocument> Documents, SourceQueryLog Log)> QuerySourceAsync(
        IEvidenceSource source, EvidenceQuery query, PipelineOptions opts, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(opts.SourceTimeoutSeconds));
        try
        {
            var result = await source.SearchAsync(query, cts.Token);
            return (result.Documents, new SourceQueryLog
            {
                SourceId = source.Descriptor.Id,
                SourceName = source.Descriptor.Name,
                Status = result.Documents.Count > 0 ? SourceQueryStatus.Ok : SourceQueryStatus.NoResults,
                Queries = result.QueriesUsed,
                ResultCount = result.Documents.Count,
                DurationMs = sw.ElapsedMilliseconds,
            });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var message = ex switch
            {
                OperationCanceledException => "przekroczono czas odpowiedzi",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } =>
                    "źródło chwilowo ogranicza liczbę zapytań, spróbuj za kilka minut",
                _ => "źródło nie odpowiedziało poprawnie",
            };
            if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests })
            {
                logger.LogWarning("Source {SourceId} is rate limited: {Message}", source.Descriptor.Id, ex.Message);
            }
            else
            {
                logger.LogWarning(ex, "Source {SourceId} failed", source.Descriptor.Id);
            }
            return ([], new SourceQueryLog
            {
                SourceId = source.Descriptor.Id,
                SourceName = source.Descriptor.Name,
                Status = SourceQueryStatus.Failed,
                Message = message,
                DurationMs = sw.ElapsedMilliseconds,
            });
        }
    }
}
