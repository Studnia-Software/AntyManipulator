using AntyManipulator.Core.Alerts;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Pipeline;
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

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ICheckResultStore, InMemoryCheckResultStore>();
        services.AddSingleton<IAlertService, AlertService>();
        services.AddScoped<IClaimExtractor, ClaimExtractor>();
        services.AddScoped<IEvidenceRetriever, EvidenceRetriever>();
        services.AddScoped<IEvidenceAssessor, EvidenceAssessor>();
        services.AddScoped<IFactCheckPipeline, FactCheckPipeline>();
        return services;
    }
}
