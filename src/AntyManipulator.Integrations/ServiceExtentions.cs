using AntyManipulator.Core.Abstractions;
using AntyManipulator.Integrations.Alerts;
using AntyManipulator.Integrations.Audit;
using AntyManipulator.Integrations.Fetching;
using AntyManipulator.Integrations.Http;
using AntyManipulator.Integrations.Llm;
using AntyManipulator.Integrations.Options;
using AntyManipulator.Integrations.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations;

public static class ServiceExtentions
{
    public static IServiceCollection AddIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LlmOptions>().BindConfiguration(LlmOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<FetchOptions>().BindConfiguration(FetchOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SourcesOptions>().BindConfiguration(SourcesOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<GoogleFactCheckOptions>().BindConfiguration(GoogleFactCheckOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<WebSearchOptions>().BindConfiguration(WebSearchOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AuditOptions>().BindConfiguration(AuditOptions.Section).ValidateDataAnnotations().ValidateOnStart();

        services.AddMemoryCache();

        services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>(c => c.Timeout = Timeout.InfiniteTimeSpan);

        services.AddHttpClient(SafeContentFetcher.ClientName, (sp, c) =>
            {
                var o = sp.GetRequiredService<IOptions<FetchOptions>>().Value;
                c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds + 5);
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", o.FullUserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(sp => SafeContentFetcher.CreateHandler(sp.GetRequiredService<IOptions<FetchOptions>>().Value));

        // Zapytania do źródeł: identyfikujący User-Agent z kontaktem, limit równoległości na host,
        // respektowanie Retry-After po 429/503.
        services.AddSingleton<HostThrottle>();
        services.AddTransient<PoliteHttpHandler>();
        services.AddHttpClient(EvidenceSourceBase.ClientName, (sp, c) =>
            {
                var o = sp.GetRequiredService<IOptions<FetchOptions>>().Value;
                c.Timeout = TimeSpan.FromSeconds(20);
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", o.FullUserAgent);
            })
            .AddHttpMessageHandler<PoliteHttpHandler>();

        services.AddHttpClient(WebhookAlertNotifier.ClientName, c => c.Timeout = TimeSpan.FromSeconds(10));

        services.AddSingleton<IContentFetcher, SafeContentFetcher>();
        services.AddSingleton<IAlertNotifier, WebhookAlertNotifier>();

        // Zapis i odczyt przebiegu (eksport) z tych samych plików JSONL.
        services.AddSingleton<JsonlAuditLog>();
        services.AddSingleton<IAuditLog>(sp => sp.GetRequiredService<JsonlAuditLog>());
        services.AddSingleton<IAuditReader>(sp => sp.GetRequiredService<JsonlAuditLog>());

        // Źródła: rejestr korzysta z fabryk. Własny adapter = services.AddEvidenceSourceAdapter<MojaFabryka>().
        services.AddEvidenceSourceAdapter<WordPressSearchFeedFactory>();
        services.AddEvidenceSourceAdapter<WordPressRestFactory>();
        services.AddEvidenceSourceAdapter<RssLatestFactory>();
        services.AddEvidenceSourceAdapter<MediaWikiFactory>();
        services.AddEvidenceSourceAdapter<TavilyWebSearchFactory>();
        services.AddEvidenceSourceAdapter<GoogleFactCheckFactory>();
        services.AddSingleton<ISourceRegistry, SourceRegistry>();
        return services;
    }
}
