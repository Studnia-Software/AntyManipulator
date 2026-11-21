using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Text;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Caching.Memory;

namespace AntyManipulator.Integrations.Sources;

/// <summary>
/// Ogólne wyszukiwanie w internecie (Tavily). Każdy wynik dostaje opis źródła wyliczony z domeny:
/// - domena z rejestru (np. demagog.org.pl) → parametry tego źródła, żeby nie liczyć go podwójnie,
/// - domena z listy zaufanych (np. gov.pl) → jej waga i wydawca,
/// - pozostałe → niska waga domyślna i osobna grupa dla każdej domeny.
/// Treść stron to niezaufane dane; trafia do modelu tak samo jak inne fragmenty.
/// </summary>
public sealed class TavilyWebSearchSource(
    SourceDefinition definition,
    IReadOnlyList<SourceDefinition> registered,
    WebSearchOptions web,
    PublisherGroupsOptions publisherGroups,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    SourcesOptions options) : EvidenceSourceBase(definition, httpFactory, cache, options)
{
    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly IMemoryCache _cache = cache;
    private readonly SourcesOptions _options = options;

    protected override async Task<IReadOnlyList<SourceDocument>> SearchOneAsync(string q, EvidenceQuery query, CancellationToken ct)
    {
        var excluded = web.ExcludedDomains.Concat(query.ExcludedDomains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var body = new JsonObject
        {
            ["query"] = q,
            ["search_depth"] = web.SearchDepth,
            ["max_results"] = web.MaxResults,
            ["chunks_per_source"] = web.ChunksPerSource,
            ["include_published_date"] = true,
            ["include_answer"] = false,
            ["include_raw_content"] = false,
        };
        if (!string.IsNullOrWhiteSpace(web.Country))
        {
            body["country"] = web.Country;
        }

        if (excluded.Count > 0)
        {
            body["exclude_domains"] = new JsonArray(excluded.Take(150).Select(d => (JsonNode)d).ToArray());
        }

        var cacheKey = "web:" + body.ToJsonString();
        if (!_cache.TryGetValue(cacheKey, out JsonNode? root))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, web.Endpoint) { Content = JsonContent.Create(body) };
            if (string.IsNullOrWhiteSpace(web.ApiKey))
            {
                request.Headers.Add("X-Tavily-Access-Mode", "keyless");
            }
            else
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", web.ApiKey);
            }

            using var response = await _httpFactory.CreateClient(ClientName).SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            _cache.Set(cacheKey, root, TimeSpan.FromMinutes(_options.CacheMinutes));
        }

        if (root?["results"] is not JsonArray results)
        {
            return [];
        }

        var docs = new List<SourceDocument>();
        foreach (var r in results)
        {
            var url = r?["url"]?.GetValue<string>();
            var title = TextTools.CollapseWhitespace(r?["title"]?.GetValue<string>() ?? string.Empty);
            var content = TextTools.CollapseWhitespace(r?["content"]?.GetValue<string>() ?? string.Empty);
            var host = Domains.HostOf(url);
            if (host is null || title.Length == 0 || content.Length == 0
                || !url!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || excluded.Any(d => Domains.Matches(host, d)))
            {
                continue;
            }

            DateTimeOffset? date = DateTimeOffset.TryParse(r?["published_date"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var d2) ? d2 : null;

            docs.Add(new SourceDocument
            {
                Source = DescribeDomain(host),
                Title = TextTools.Truncate(title, 300),
                Url = url,
                PublishedAt = date,
                Snippet = content,
            });
        }

        return docs;
    }

    private SourceDescriptor DescribeDomain(string host)
    {
        var known = registered.FirstOrDefault(d => d.Adapter is not (SourceAdapter.TavilyWebSearch or SourceAdapter.GoogleFactCheck)
                                                    && Domains.HostOf(d.BaseUrl) is { } h && Domains.Matches(host, h));
        if (known is not null)
        {
            // Ten sam wydawca co źródło z rejestru: te same parametry i ta sama grupa właścicielska.
            return known.ToDescriptor() with { Id = Descriptor.Id, Enabled = true, DisabledReason = null };
        }

        var trusted = web.TrustedDomains
            .Where(t => Domains.Matches(host, t.Domain))
            .OrderByDescending(t => t.Domain.Length)
            .FirstOrDefault();

        return Descriptor with
        {
            Name = host,
            Publisher = trusted?.Publisher ?? host,
            Type = trusted?.Type ?? SourceType.Web,
            BaseUrl = "https://" + host + "/",
            Reliability = trusted?.Reliability ?? web.DefaultReliability,
            OwnershipGroup = trusted?.OwnershipGroup
                             ?? publisherGroups.GroupOf(host)?.Id
                             ?? "web:" + (trusted?.Domain.ToLowerInvariant() ?? host),
            Description = trusted is null ? "Strona spoza listy zaufanych domen." : "Domena z listy zaufanych.",
        };
    }
}
