using AntyManipulator.Integrations.Options;
using AntyManipulator.Integrations.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AntyManipulator.Integrations;

public static class ServiceExtentions
{
    public static IServiceCollection AddIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ScalewayOptions>()
            .BindConfiguration("Scaleway")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<SourceSettings>()
            .BindConfiguration("Sources")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<MessageSettings>()
            .BindConfiguration("Message")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient();
        
        services.AddScoped<IScalewayService, ScalewayService>();
        
        return services;
    }
}