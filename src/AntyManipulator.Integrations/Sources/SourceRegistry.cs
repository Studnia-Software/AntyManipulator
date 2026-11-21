using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Sources;

/// <summary>Buduje adaptery źródeł z sekcji "Sources" (oraz "WebSearch" i "GoogleFactCheck") w appsettings.</summary>
public sealed class SourceRegistry : ISourceRegistry
{
    public SourceRegistry(
        IOptions<SourcesOptions> options,
        IOptions<WebSearchOptions> webOptions,
        IOptions<GoogleFactCheckOptions> googleOptions,
        IOptions<PublisherGroupsOptions> publisherGroups,
        IHttpClientFactory httpFactory,
        IMemoryCache cache)
    {
        var opts = options.Value;
        var google = googleOptions.Value;

        // Źródło wymagające klucza bez skonfigurowanego klucza jest jawnie wyłączone, z powodem.
        var definitions = opts.Definitions.Select(d =>
            d is { Adapter: SourceAdapter.GoogleFactCheck, Enabled: true } && string.IsNullOrWhiteSpace(google.ApiKey)
                ? Disabled(d, "brak klucza API (GoogleFactCheck:ApiKey)")
                : d).ToList();

        All = definitions.Select(d => d.ToDescriptor()).ToList();
        Active = definitions
            .Where(d => d.Enabled)
            .Select<SourceDefinition, IEvidenceSource>(d => d.Adapter switch
            {
                SourceAdapter.WordPressSearchFeed => new WordPressSearchFeedSource(d, httpFactory, cache, opts),
                SourceAdapter.WordPressRest => new WordPressRestSource(d, httpFactory, cache, opts),
                SourceAdapter.RssLatest => new RssLatestSource(d, httpFactory, cache, opts),
                SourceAdapter.MediaWiki => new MediaWikiSource(d, httpFactory, cache, opts),
                SourceAdapter.TavilyWebSearch => new TavilyWebSearchSource(d, opts.Definitions, webOptions.Value, publisherGroups.Value, httpFactory, cache, opts),
                SourceAdapter.GoogleFactCheck => new GoogleFactCheckSource(d, opts.Definitions, google, publisherGroups.Value, httpFactory, cache, opts),
                _ => throw new InvalidOperationException($"Unknown source adapter {d.Adapter} for {d.Id}"),
            })
            .ToList();
    }

    public IReadOnlyList<SourceDescriptor> All { get; }

    public IReadOnlyList<IEvidenceSource> Active { get; }

    private static SourceDefinition Disabled(SourceDefinition d, string reason) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Publisher = d.Publisher,
        Type = d.Type,
        Adapter = d.Adapter,
        BaseUrl = d.BaseUrl,
        FeedUrl = d.FeedUrl,
        Language = d.Language,
        Reliability = d.Reliability,
        OwnershipGroup = d.OwnershipGroup,
        Enabled = false,
        Description = d.Description,
        CoverageNote = d.CoverageNote,
        DisabledReason = reason,
    };
}
