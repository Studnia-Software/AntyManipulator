using AntyManipulator.Core.Alerts;
using AntyManipulator.Core.Channels;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Pipeline;
using AntyManipulator.Core.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AntyManipulator.Core;

public static class CoreServiceExtensions
{
    public static IServiceCollection AddFactCheckCore(this IServiceCollection services)
    {
        services.AddOptions<PipelineOptions>().BindConfiguration(PipelineOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AlertOptions>().BindConfiguration(AlertOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PublisherGroupsOptions>().BindConfiguration(PublisherGroupsOptions.Section);
        services.AddOptions<PrivacyOptions>().BindConfiguration(PrivacyOptions.Section);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IUserPseudonymizer, HmacUserPseudonymizer>();
        services.AddSingleton<ICheckResultStore, InMemoryCheckResultStore>();
        services.AddSingleton<IAlertService, AlertService>();
        services.AddScoped<IClaimExtractor, ClaimExtractor>();
        services.AddScoped<IEvidenceRetriever, EvidenceRetriever>();
        services.AddScoped<IEvidenceAssessor, EvidenceAssessor>();
        services.AddScoped<IFactCheckPipeline, FactCheckPipeline>();
        return services;
    }

    /// <summary>
    /// Kolejka dla kanałów asynchronicznych (Discord, komunikatory). Endpoint WWW działa bez niej, synchronicznie.
    /// Wywołanie wielokrotne jest bezpieczne.
    /// </summary>
    public static IServiceCollection AddCheckQueue(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(InMemoryCheckQueue)))
        {
            return services;
        }

        services.AddOptions<QueueOptions>().BindConfiguration(QueueOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<InMemoryCheckQueue>();
        services.AddSingleton<ICheckQueue>(sp => sp.GetRequiredService<InMemoryCheckQueue>());
        services.AddHostedService<CheckQueueWorker>();
        return services;
    }
}
