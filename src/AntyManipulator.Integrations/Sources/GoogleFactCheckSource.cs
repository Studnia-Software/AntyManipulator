using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Text;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Caching.Memory;

namespace AntyManipulator.Integrations.Sources;

public sealed class GoogleFactCheckSource(
    SourceDefinition definition,
    IReadOnlyList<SourceDefinition> registered,
    GoogleFactCheckOptions google,
    PublisherGroupsOptions publisherGroups,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    SourcesOptions options) : EvidenceSourceBase(definition, httpFactory, cache, options)
{
    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly IMemoryCache _cache = cache;
    private readonly SourcesOptions _options = options;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public override async Task<SourceSearchResult> SearchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        var used = new List<string>();
        var docs = new List<SourceDocument>();

        foreach (var (q, lang) in new[] { (query.QueriesPl.FirstOrDefault(), "pl"), (query.QueriesEn.FirstOrDefault(), "en") })
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                continue;
            }

            used.Add($"{q} [{lang}]");
            docs.AddRange(await SearchLanguageAsync(q, lang, cancellationToken));
        }

        if (docs.Count == 0)
        {
            var q = TextTools.Truncate(query.ClaimText, 120);
            used.Add($"{q} [pl]");
            docs.AddRange(await SearchLanguageAsync(q, "pl", cancellationToken));
        }

        var result = docs
            .Where(d => !query.ExcludedDomains.Any(x => Domains.Matches(Domains.HostOf(d.Url), x)))
            .DistinctBy(d => d.Url, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults * 2)
            .ToList();

        return new SourceSearchResult { Documents = result, QueriesUsed = used };
    }

    protected override Task<IReadOnlyList<SourceDocument>> SearchOneAsync(string q, EvidenceQuery query, CancellationToken ct) =>
        SearchLanguageAsync(q, "pl", ct);

    private async Task<IReadOnlyList<SourceDocument>> SearchLanguageAsync(string q, string languageCode, CancellationToken ct)
    {
        var url = new StringBuilder(google.Endpoint)
            .Append("?query=").Append(Uri.EscapeDataString(q))
            .Append("&languageCode=").Append(languageCode)
            .Append("&pageSize=").Append(google.PageSize);
        if (google.MaxAgeDays is { } age)
        {
            url.Append("&maxAgeDays=").Append(age);
        }

        var requestUrl = url.ToString();
        if (!_cache.TryGetValue("gfc:" + requestUrl, out JsonNode? root))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            // Klucz w nagłówku, nie w adresie, żeby nie trafiał do logów z adresami URL.
            request.Headers.Add("X-Goog-Api-Key", google.ApiKey);

            using var response = await _httpFactory.CreateClient(ClientName).SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            _cache.Set("gfc:" + requestUrl, root, TimeSpan.FromMinutes(_options.CacheMinutes));
        }

        if (root?["claims"] is not JsonArray claims)
        {
            return [];
        }

        var docs = new List<SourceDocument>();
        foreach (var claim in claims)
        {
            var claimText = TextTools.CollapseWhitespace(claim?["text"]?.GetValue<string>() ?? string.Empty);
            var claimant = claim?["claimant"]?.GetValue<string>();
            var claimDate = ParseDate(claim?["claimDate"]?.GetValue<string>());
            if (claimText.Length == 0 || claim?["claimReview"] is not JsonArray reviews)
            {
                continue;
            }

            foreach (var review in reviews)
            {
                var reviewUrl = review?["url"]?.GetValue<string>();
                var host = Domains.HostOf(reviewUrl);
                if (host is null || !reviewUrl!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var publisherName = review?["publisher"]?["name"]?.GetValue<string>();
                var publisherSite = review?["publisher"]?["site"]?.GetValue<string>();
                var rating = TextTools.CollapseWhitespace(review?["textualRating"]?.GetValue<string>() ?? string.Empty);
                var title = TextTools.CollapseWhitespace(review?["title"]?.GetValue<string>() ?? string.Empty);
                var reviewDate = ParseDate(review?["reviewDate"]?.GetValue<string>());

                docs.Add(new SourceDocument
                {
                    Source = DescribePublisher(host, publisherName, publisherSite),
                    Title = TextTools.Truncate(title.Length > 0 ? title : claimText, 300),
                    Url = reviewUrl,
                    PublishedAt = reviewDate ?? claimDate,
                    DateKind = reviewDate is not null ? "data weryfikacji" : "data twierdzenia",
                    Snippet = BuildSnippet(claimText, claimant, claimDate, publisherName ?? host, rating),
                    Categories = rating.Length > 0 ? [rating] : [],
                });
            }
        }

        return docs;
    }

    private static string BuildSnippet(string claimText, string? claimant, DateTimeOffset? claimDate, string publisher, string rating)
    {
        var sb = new StringBuilder("Weryfikowane twierdzenie: ").Append(claimText);
        if (!string.IsNullOrWhiteSpace(claimant))
        {
            sb.Append(" Autor twierdzenia: ").Append(claimant.Trim()).Append('.');
        }

        if (claimDate is not null)
        {
            sb.Append(" Data twierdzenia: ").Append(claimDate.Value.ToString("dd.MM.yyyy", Pl)).Append('.');
        }

        if (rating.Length > 0)
        {
            sb.Append(" Ocena ").Append(publisher).Append(": ").Append(rating).Append('.');
        }

        return sb.ToString();
    }

    private SourceDescriptor DescribePublisher(string host, string? publisherName, string? publisherSite)
    {
        var siteHost = Domains.HostOf("https://" + (publisherSite ?? host).Trim().TrimStart('/')) ?? host;

        var known = registered.FirstOrDefault(d =>
            d.Adapter is not (SourceAdapter.GoogleFactCheck or SourceAdapter.TavilyWebSearch)
            && Domains.HostOf(d.BaseUrl) is { } h
            && (Domains.Matches(host, h) || Domains.Matches(siteHost, h)));
        if (known is not null)
        {
            return known.ToDescriptor() with { Id = Descriptor.Id, Enabled = true, DisabledReason = null };
        }

        var name = string.IsNullOrWhiteSpace(publisherName) ? siteHost : publisherName.Trim();
        return Descriptor with
        {
            Name = name,
            Publisher = name,
            Type = SourceType.FactChecker,
            BaseUrl = "https://" + siteHost + "/",
            OwnershipGroup = publisherGroups.GroupOf(siteHost)?.Id ?? "gfc:" + siteHost,
            Description = "Weryfikacja znaleziona przez Google Fact Check Tools.",
            CoverageNote = null,
        };
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
