using System.Globalization;
using System.Text.Json.Nodes;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Text;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Caching.Memory;

namespace AntyManipulator.Integrations.Sources;

/// <summary>Wspólna logika adapterów: wybór zapytań wg języka, cache, limit wyników.</summary>
public abstract class EvidenceSourceBase(
    SourceDefinition definition,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    SourcesOptions options) : IEvidenceSource
{
    public const string ClientName = "sources";

    public SourceDescriptor Descriptor { get; } = definition.ToDescriptor();

    protected SourceDefinition Definition { get; } = definition;

    protected int MaxResults => options.MaxResultsPerSource;

    /// <summary>
    /// Próbuje kolejnych zapytań i zostawia wyniki najlepiej pasujące do twierdzenia.
    /// Kończy, gdy najlepszy wynik ma co najmniej 2 wspólne słowa kluczowe z twierdzeniem.
    /// </summary>
    public virtual async Task<SourceSearchResult> SearchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        var claimTokens = TextTools.Tokens(query.AllText);
        var used = new List<string>();
        List<(SourceDocument Doc, int Overlap)> best = [];

        foreach (var q in CandidateQueries(query))
        {
            used.Add(q);
            var docs = await SearchOneAsync(q, query, cancellationToken);
            var ranked = docs
                .Select(d => (Doc: d, Overlap: TextTools.Overlap(claimTokens, TextTools.Tokens(d.Title + " " + d.Snippet))))
                .OrderByDescending(x => x.Overlap)
                .ToList();

            if (ranked.Count > 0 && (best.Count == 0 || ranked[0].Overlap > best[0].Overlap))
            {
                best = ranked;
            }

            if (best.Count > 0 && best[0].Overlap >= 2)
            {
                break;
            }
        }

        return new SourceSearchResult { Documents = best.Take(MaxResults).Select(x => x.Doc).ToList(), QueriesUsed = used };
    }

    protected abstract Task<IReadOnlyList<SourceDocument>> SearchOneAsync(string q, EvidenceQuery query, CancellationToken ct);

    /// <summary>Zapytania wysyłane do źródła, po kolei. Domyślnie dwa pierwsze od modelu.</summary>
    protected virtual IEnumerable<string> CandidateQueries(EvidenceQuery query) => QueriesFor(query).Take(2);

    /// <summary>
    /// Dla wyszukiwarek wymagających wszystkich słów naraz (np. WordPress): po pełnym zapytaniu
    /// próbujemy coraz krótszych, opartych na nazwach własnych z twierdzenia.
    /// Przykład: "Jakubiak Braun Rozwój Plus" → 0 wyników, "Braun" → 26 wyników.
    /// </summary>
    protected IEnumerable<string> RelaxedQueries(EvidenceQuery query, int max)
    {
        var primary = QueriesFor(query);
        var names = TextTools.ProperNouns(query.ClaimText);
        var list = new List<string>();

        if (primary.Count > 0)
        {
            list.Add(primary[0]);
        }

        if (names.Count >= 2)
        {
            list.Add(names[0] + " " + names[1]);
        }

        foreach (var name in names.Take(2))
        {
            list.Add(name);
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).Take(max);
    }

    /// <summary>Zapytania w języku źródła; gdy model nie dał zapytań w tym języku, treść twierdzenia.</summary>
    protected IReadOnlyList<string> QueriesFor(EvidenceQuery query)
    {
        var list = query.For(Descriptor.Language.ToLowerInvariant());
        return list.Count > 0 ? list : [TextTools.Truncate(query.ClaimText, 80)];
    }

    /// <summary>
    /// Języki odpytywane przez źródło wielojęzyczne: stałe z konfiguracji ("Languages") plus,
    /// jeśli włączone, języki dodatkowe dobrane przez model do twierdzenia. Tylko te, dla których są zapytania.
    /// </summary>
    protected IReadOnlyList<string> SearchLanguages(EvidenceQuery query)
    {
        IEnumerable<string> configured = Definition.Languages.Count > 0 ? Definition.Languages : new List<string> { Definition.Language };
        var languages = configured.Select(l => l.Trim().ToLowerInvariant()).ToList();
        if (Definition.UseClaimLanguages)
        {
            languages.AddRange(query.Languages);
        }

        return languages
            .Distinct()
            .Where(l => query.For(l).Count > 0)
            .Take(Definition.MaxLanguages)
            .ToList();
    }

    /// <summary>
    /// Jedno zapytanie na każdy język, wyniki scalone i uszeregowane według zgodności
    /// z twierdzeniem i zapytaniami we wszystkich językach.
    /// </summary>
    protected async Task<SourceSearchResult> SearchPerLanguageAsync(
        EvidenceQuery query,
        Func<string, string, CancellationToken, Task<IReadOnlyList<SourceDocument>>> searchInLanguage,
        int take,
        CancellationToken ct)
    {
        var tokens = TextTools.Tokens(query.AllText);
        var used = new List<string>();
        var docs = new List<SourceDocument>();

        foreach (var language in SearchLanguages(query))
        {
            var q = query.For(language)[0];
            used.Add($"{q} [{language}]");
            var found = await searchInLanguage(q, language, ct);
            docs.AddRange(found.Select(d => d.Language is null ? d with { Language = language } : d));
        }

        var ranked = docs
            .DistinctBy(d => d.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Select(d => (Doc: d, Overlap: TextTools.Overlap(tokens, TextTools.Tokens(d.Title + " " + d.Snippet))))
            .OrderByDescending(x => x.Overlap)
            .Take(take)
            .Select(x => x.Doc)
            .ToList();

        return new SourceSearchResult { Documents = ranked, QueriesUsed = used };
    }

    /// <param name="isValid">
    /// Sprawdza, czy odpowiedź to oczekiwany format. Strony ochrony przed botami (np. Cloudflare)
    /// potrafią zwrócić HTML ze statusem 200; takiej odpowiedzi nie zapisujemy w cache.
    /// </param>
    protected async Task<string> GetStringCachedAsync(string url, CancellationToken ct, Func<string, bool>? isValid = null)
    {
        var key = "src:" + url;
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        var http = httpFactory.CreateClient(ClientName);
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        if (isValid is not null && !isValid(body))
        {
            throw new HttpRequestException("Source returned unexpected content (bot protection?)", null, System.Net.HttpStatusCode.Forbidden);
        }

        cache.Set(key, body, TimeSpan.FromMinutes(options.CacheMinutes));
        return body;
    }

    protected static bool LooksLikeFeed(string body) =>
        body.Contains("<rss", StringComparison.OrdinalIgnoreCase) || body.Contains("<channel", StringComparison.OrdinalIgnoreCase);

    protected static bool LooksLikeJsonArray(string body) => body.TrimStart().StartsWith('[');

    protected SourceDocument Doc(string title, string url, DateTimeOffset? date, string snippet, IReadOnlyList<string>? categories = null, string dateKind = "publikacja") => new()
    {
        Source = Descriptor,
        Title = title,
        Url = url,
        PublishedAt = date,
        DateKind = dateKind,
        Snippet = snippet,
        Categories = categories ?? [],
    };

    protected string BaseUrl => Definition.BaseUrl.EndsWith('/') ? Definition.BaseUrl : Definition.BaseUrl + "/";
}

/// <summary>Wyszukiwarka WordPressa zwracająca wyniki w RSS (np. Demagog).</summary>
public sealed class WordPressSearchFeedSource(SourceDefinition d, IHttpClientFactory f, IMemoryCache c, SourcesOptions o)
    : EvidenceSourceBase(d, f, c, o)
{
    protected override IEnumerable<string> CandidateQueries(EvidenceQuery query) => RelaxedQueries(query, 4);

    protected override async Task<IReadOnlyList<SourceDocument>> SearchOneAsync(string q, EvidenceQuery query, CancellationToken ct)
    {
        var xml = await GetStringCachedAsync($"{BaseUrl}?s={Uri.EscapeDataString(q)}&feed=rss2", ct, LooksLikeFeed);
        // Bierzemy więcej niż MaxResults, bo przy krótkim zapytaniu (np. samo nazwisko)
        // najlepiej pasujące artykuły wybiera ranking w SearchAsync, a nie kolejność w kanale.
        return FeedParser.Parse(xml)
            .Take(30)
            .Select(i => Doc(i.Title, i.Link, i.PublishedAt, i.Description, i.Categories))
            .ToList();
    }
}

/// <summary>WordPress REST API (np. FactCheck.org).</summary>
public sealed class WordPressRestSource(SourceDefinition d, IHttpClientFactory f, IMemoryCache c, SourcesOptions o)
    : EvidenceSourceBase(d, f, c, o)
{
    protected override IEnumerable<string> CandidateQueries(EvidenceQuery query) => RelaxedQueries(query, 3);

    protected override async Task<IReadOnlyList<SourceDocument>> SearchOneAsync(string q, EvidenceQuery query, CancellationToken ct)
    {
        var json = await GetStringCachedAsync(
            $"{BaseUrl}wp-json/wp/v2/posts?search={Uri.EscapeDataString(q)}&per_page=15&_fields=date_gmt,link,title,excerpt", ct, LooksLikeJsonArray);

        var docs = new List<SourceDocument>();
        if (JsonNode.Parse(json) is not JsonArray arr)
        {
            return docs;
        }

        foreach (var item in arr)
        {
            var link = item?["link"]?.GetValue<string>();
            var title = FeedParser.HtmlToText(item?["title"]?["rendered"]?.GetValue<string>(), 400);
            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(title) || !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTimeOffset? date = DateTimeOffset.TryParse(item?["date_gmt"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var d2) ? d2 : null;
            docs.Add(Doc(title, link, date, FeedParser.HtmlToText(item?["excerpt"]?["rendered"]?.GetValue<string>(), 1500)));
        }

        return docs;
    }
}

/// <summary>
/// Kanał RSS z najnowszymi artykułami (np. Konkret24, PolitiFact). Serwis nie ma publicznej
/// wyszukiwarki, więc filtrujemy lokalnie po wspólnych słowach kluczowych.
/// </summary>
public sealed class RssLatestSource(SourceDefinition d, IHttpClientFactory f, IMemoryCache c, SourcesOptions o)
    : EvidenceSourceBase(d, f, c, o)
{
    public override async Task<SourceSearchResult> SearchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        var docs = await SearchOneAsync(string.Empty, query, cancellationToken);
        return new SourceSearchResult { Documents = docs, QueriesUsed = ["(filtrowanie najnowszych artykułów po słowach kluczowych)"] };
    }

    protected override async Task<IReadOnlyList<SourceDocument>> SearchOneAsync(string q, EvidenceQuery query, CancellationToken ct)
    {
        var xml = await GetStringCachedAsync(Definition.FeedUrl ?? Definition.BaseUrl, ct, LooksLikeFeed);
        var claimTokens = TextTools.Tokens(query.AllText);

        return FeedParser.Parse(xml)
            .Select(i => (Item: i, Overlap: TextTools.Overlap(claimTokens, TextTools.Tokens(i.Title + " " + i.Description))))
            .Where(x => x.Overlap >= 2)
            .OrderByDescending(x => x.Overlap)
            .Take(MaxResults)
            .Select(x => Doc(x.Item.Title, x.Item.Link, x.Item.PublishedAt, x.Item.Description, x.Item.Categories))
            .ToList();
    }
}

/// <summary>Wikipedia przez API MediaWiki: wyszukiwanie + wstęp artykułu + data ostatniej edycji.</summary>
public sealed class MediaWikiSource(SourceDefinition d, IHttpClientFactory f, IMemoryCache c, SourcesOptions o)
    : EvidenceSourceBase(d, f, c, o)
{
    protected override async Task<IReadOnlyList<SourceDocument>> SearchOneAsync(string q, EvidenceQuery query, CancellationToken ct)
    {
        var url = $"{BaseUrl}w/api.php?action=query&format=json&formatversion=2&generator=search" +
                  $"&gsrsearch={Uri.EscapeDataString(q)}&gsrlimit={MaxResults}&prop=extracts|info|revisions" +
                  "&exintro=1&explaintext=1&exchars=1200&exlimit=max&inprop=url&rvprop=timestamp";
        var json = await GetStringCachedAsync(url, ct);
        var pages = JsonNode.Parse(json)?["query"]?["pages"] as JsonArray;
        if (pages is null)
        {
            return [];
        }

        return pages
            .Where(p => p is not null)
            .OrderBy(p => p!["index"]?.GetValue<int>() ?? 99)
            .Select(p =>
            {
                var title = p!["title"]?.GetValue<string>() ?? string.Empty;
                var link = p["fullurl"]?.GetValue<string>() ?? string.Empty;
                var extract = p["extract"]?.GetValue<string>() ?? string.Empty;
                var ts = p["revisions"]?[0]?["timestamp"]?.GetValue<string>();
                DateTimeOffset? date = DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
                return Doc(title, link, date, TextTools.CollapseWhitespace(extract), dateKind: "ostatnia edycja");
            })
            .Where(doc => doc.Title.Length > 0 && doc.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && doc.Snippet.Length > 0)
            .ToList();
    }
}
