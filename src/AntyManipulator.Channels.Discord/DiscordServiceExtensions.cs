using AntyManipulator.Core;
using AntyManipulator.Core.Channels;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Channels.Discord;

public static class DiscordServiceExtensions
{
    /// <summary>
    /// Kanał Discord: bot (usługa w tle), odbiorca wyników z kolejki i sama kolejka.
    /// Bot startuje tylko przy Discord:Enabled=true i ustawionym Discord:BotToken.
    /// </summary>
    public static IServiceCollection AddDiscordChannel(this IServiceCollection services)
    {
        services.AddOptions<DiscordOptions>().BindConfiguration(DiscordOptions.Section).ValidateDataAnnotations();
        services.AddCheckQueue();

        services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            // Komendy (slash i kontekstowe) nie wymagają uprzywilejowanych intencji.
            GatewayIntents = GatewayIntents.Guilds,
            LogLevel = LogSeverity.Info,
        }));
        services.AddSingleton<IChannelResponder, DiscordResponder>();
        services.AddHostedService<DiscordBotService>();
        return services;
    }
}
