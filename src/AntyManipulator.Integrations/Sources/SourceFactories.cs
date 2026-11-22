using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Options;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Sources;

/// <summary>
/// Fabryka adaptera źródła. Nowe źródło = nowa klasa <see cref="IEvidenceSource"/> + fabryka
/// zarejestrowana przez <see cref="SourceFactoryServiceExtensions.AddEvidenceSourceAdapter{TFactory}"/>
/// + wpis w "Sources:Definitions" z polem "Adapter" równym <see cref="Adapter"/>. Rejestr źródeł się nie zmienia.
/// </summary>
public interface IEvidenceSourceFactory
{
    /// <summary>Nazwa adaptera używana w konfiguracji.</summary>
    string Adapter { get; }

    /// <summary>Powód, dla którego źródła nie da się teraz użyć (np. brak klucza API), albo null.</summary>
    string? UnavailableReason(SourceDefinition definition) => null;

    IEvidenceSource Create(SourceDefinition definition, SourceFactoryContext context);
}

/// <summary>Wspólne dane dostępne przy tworzeniu adaptera.</summary>
public sealed record SourceFactoryContext(IReadOnlyList<SourceDefinition> AllDefinitions, SourcesOptions Options);

public static class SourceFactoryServiceExtensions
{
    public static IServiceCollection AddEvidenceSourceAdapter<TFactory>(this IServiceCollection services)
        where TFactory : class, IEvidenceSourceFactory
    {
        services.AddSingleton<IEvidenceSourceFactory, TFactory>();
        return services;
    }
}

public sealed class WordPressSearchFeedFactory(IHttpClientFactory http, IMemoryCache cache) : IEvidenceSourceFactory
{
    public string Adapter => SourceAdapters.WordPressSearchFeed;

    public IEvidenceSource Create(SourceDefinition definition, SourceFactoryContext context) =>
        new WordPressSearchFeedSource(definition, http, cache, context.Options);
}

public sealed class WordPressRestFactory(IHttpClientFactory http, IMemoryCache cache) : IEvidenceSourceFactory
{
    public string Adapter => SourceAdapters.WordPressRest;

    public IEvidenceSource Create(SourceDefinition definition, SourceFactoryContext context) =>
        new WordPressRestSource(definition, http, cache, context.Options);
}

public sealed class RssLatestFactory(IHttpClientFactory http, IMemoryCache cache) : IEvidenceSourceFactory
{
    public string Adapter => SourceAdapters.RssLatest;

    public string? UnavailableReason(SourceDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.FeedUrl) && string.IsNullOrWhiteSpace(definition.BaseUrl) ? "brak adresu kanału RSS" : null;

    public IEvidenceSource Create(SourceDefinition definition, SourceFactoryContext context) =>
        new RssLatestSource(definition, http, cache, context.Options);
}

public sealed class MediaWikiFactory(IHttpClientFactory http, IMemoryCache cache) : IEvidenceSourceFactory
{
    public string Adapter => SourceAdapters.MediaWiki;

    public IEvidenceSource Create(SourceDefinition definition, SourceFactoryContext context) =>
        new MediaWikiSource(definition, http, cache, context.Options);
}

public sealed class TavilyWebSearchFactory(
    IHttpClientFactory http,
    IMemoryCache cache,
    IOptions<WebSearchOptions> web,
    IOptions<PublisherGroupsOptions> groups) : IEvidenceSourceFactory
{
    public string Adapter => SourceAdapters.TavilyWebSearch;

    public IEvidenceSource Create(SourceDefinition definition, SourceFactoryContext context) =>
        new TavilyWebSearchSource(definition, context.AllDefinitions, web.Value, groups.Value, http, cache, context.Options);
}

public sealed class GoogleFactCheckFactory(
    IHttpClientFactory http,
    IMemoryCache cache,
    IOptions<GoogleFactCheckOptions> google,
    IOptions<PublisherGroupsOptions> groups) : IEvidenceSourceFactory
{
    public string Adapter => SourceAdapters.GoogleFactCheck;

    public string? UnavailableReason(SourceDefinition definition) =>
        string.IsNullOrWhiteSpace(google.Value.ApiKey) ? "brak klucza API (GoogleFactCheck:ApiKey)" : null;

    public IEvidenceSource Create(SourceDefinition definition, SourceFactoryContext context) =>
        new GoogleFactCheckSource(definition, context.AllDefinitions, google.Value, groups.Value, http, cache, context.Options);
}
