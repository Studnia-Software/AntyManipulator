using System.Collections.Concurrent;
using AntyManipulator.Core.Channels;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Queue;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Channels.Discord;

/// <summary>
/// Bot Discorda. Obsługuje:
/// - komendę <c>/sprawdz</c> z polami "tresc" i "link",
/// - komendę kontekstową wiadomości "Sprawdź informację" (prawy przycisk na wiadomości → Aplikacje).
/// Discord wymaga odpowiedzi w 3 sekundy, więc bot od razu odracza odpowiedź, wrzuca zadanie do
/// kolejki i podmienia "Sprawdzam…" na wynik, gdy worker skończy (token interakcji żyje 15 minut).
/// Potrzebny jest tylko intent Guilds; treść wiadomości z komendy kontekstowej Discord przekazuje w interakcji.
/// </summary>
public sealed class DiscordBotService(
    DiscordSocketClient client,
    ICheckQueue queue,
    IUserPseudonymizer pseudonymizer,
    IOptions<DiscordOptions> options,
    TimeProvider time,
    ILogger<DiscordBotService> logger) : BackgroundService
{
    public const string SlashCommandName = "sprawdz";
    public const string MessageCommandName = "Sprawdź informację";

    private readonly ConcurrentDictionary<ulong, Queue<DateTimeOffset>> _userRequests = new();
    private int _commandsRegistered;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Discord channel disabled (Discord:Enabled=false)");
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            logger.LogWarning("Discord channel enabled, but Discord:BotToken is empty. Set it with: dotnet user-secrets set \"Discord:BotToken\" \"...\"");
            return;
        }

        client.Log += OnLogAsync;
        client.Ready += OnReadyAsync;
        client.SlashCommandExecuted += cmd => GuardAsync(() => OnSlashCommandAsync(cmd), cmd);
        client.MessageCommandExecuted += cmd => GuardAsync(() => OnMessageCommandAsync(cmd), cmd);

        try
        {
            await client.LoginAsync(TokenType.Bot, opts.BotToken);
            await client.StartAsync();
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Błąd Discorda (np. zły token) nie może zatrzymać całego API.
            logger.LogError(ex, "Discord bot failed to start");
            return;
        }

        await client.StopAsync();
        await client.LogoutAsync();
    }

    private async Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _commandsRegistered, 1) == 1)
        {
            return;
        }

        var commands = BuildCommands();
        try
        {
            var guilds = options.Value.GuildIds;
            if (guilds.Count == 0)
            {
                await ((IDiscordClient)client).BulkOverwriteGlobalApplicationCommand(commands, null);
                logger.LogInformation("Discord: registered global commands");
            }
            else
            {
                foreach (var id in guilds)
                {
                    var guild = client.GetGuild(id);
                    if (guild is null)
                    {
                        logger.LogWarning("Discord: bot is not a member of guild {GuildId}", id);
                        continue;
                    }

                    await guild.BulkOverwriteApplicationCommandAsync(commands, null);
                    logger.LogInformation("Discord: registered commands in guild {GuildId}", id);
                }
            }
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _commandsRegistered, 0);
            logger.LogError(ex, "Discord: command registration failed");
        }
    }

    private ApplicationCommandProperties[] BuildCommands()
    {
        var max = options.Value.MaxTextLength;
        var contexts = new[] { InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel };
        var integrations = new[] { ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall };

        var slash = new SlashCommandBuilder()
            .WithName(SlashCommandName)
            .WithDescription("Sprawdź pogłoskę lub doniesienie w źródłach")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("tresc")
                .WithDescription("Treść wiadomości do sprawdzenia")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(false)
                .WithMaxLength(Math.Min(max, 6000)))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("link")
                .WithDescription("Adres artykułu lub wpisu (http/https)")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(false)
                .WithMaxLength(2048))
            .WithContextTypes(contexts)
            .WithIntegrationTypes(integrations);

        var message = new MessageCommandBuilder()
            .WithName(MessageCommandName)
            .WithContextTypes(contexts)
            .WithIntegrationTypes(integrations);

        return [slash.Build(), message.Build()];
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand command)
    {
        if (command.Data.Name != SlashCommandName)
        {
            return;
        }

        var text = command.Data.Options.FirstOrDefault(o => o.Name == "tresc")?.Value as string;
        var link = command.Data.Options.FirstOrDefault(o => o.Name == "link")?.Value as string;
        await AcceptAsync(command, text, link);
    }

    private async Task OnMessageCommandAsync(SocketMessageCommand command)
    {
        if (command.Data.Name != MessageCommandName)
        {
            return;
        }

        // Treść wiadomości plus linki z osadzeń, bo sama wiadomość bywa tylko linkiem z podglądem.
        var message = command.Data.Message;
        var text = message.Content;
        var embedUrl = message.Embeds.Select(e => e.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (string.IsNullOrWhiteSpace(text) && embedUrl is not null)
        {
            text = embedUrl;
        }

        await AcceptAsync(command, text, null);
    }

    private async Task AcceptAsync(SocketInteraction interaction, string? text, string? link)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(link))
        {
            await interaction.RespondAsync("Podaj treść wiadomości albo link do sprawdzenia. Obrazy i filmy nie są jeszcze analizowane.", ephemeral: true);
            return;
        }

        if (text?.Length > opts.MaxTextLength)
        {
            text = text[..opts.MaxTextLength];
        }

        if (!TryConsumeRateLimit(interaction.User.Id))
        {
            await interaction.RespondAsync($"Możesz zlecić najwyżej {opts.ChecksPerUserPerMinute} sprawdzenia na minutę. Spróbuj za chwilę.", ephemeral: true);
            return;
        }

        // Odpowiedź odroczona: Discord pokazuje "myśli…", a my mamy 15 minut na wynik.
        await interaction.DeferAsync(ephemeral: opts.EphemeralReplies);

        var now = time.GetUtcNow();
        var job = new CheckJob
        {
            Request = new CheckRequest
            {
                Channel = DiscordResponder.ChannelName,
                UserHash = pseudonymizer.Pseudonymize(DiscordResponder.ChannelName, interaction.User.Id.ToString()),
                Text = text,
                Url = string.IsNullOrWhiteSpace(link) ? null : link.Trim(),
            },
            EnqueuedAt = now,
            Deadline = now.AddMinutes(opts.MaxQueueWaitMinutes),
            ReplyState = interaction,
        };

        if (!queue.TryEnqueue(job, out var position))
        {
            await interaction.ModifyOriginalResponseAsync(p => p.Content =
                "🛠️ **Usługa jest teraz przeciążona.** To nie jest ocena treści. Spróbuj ponownie za kilka minut.");
            return;
        }

        var waiting = position > 1 ? $" W kolejce przed Tobą: {position - 1}." : string.Empty;
        await interaction.ModifyOriginalResponseAsync(p =>
        {
            p.Content = $"⏳ Sprawdzam w źródłach. Zwykle trwa to do minuty.{waiting}";
            p.AllowedMentions = AllowedMentions.None;
        });
    }

    /// <summary>Okno przesuwne: najwyżej N zgłoszeń na użytkownika w ciągu ostatniej minuty.</summary>
    private bool TryConsumeRateLimit(ulong userId)
    {
        var now = time.GetUtcNow();
        var window = _userRequests.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (window)
        {
            while (window.Count > 0 && now - window.Peek() > TimeSpan.FromMinutes(1))
            {
                window.Dequeue();
            }

            if (window.Count >= options.Value.ChecksPerUserPerMinute)
            {
                return false;
            }

            window.Enqueue(now);
            return true;
        }
    }

    private async Task GuardAsync(Func<Task> handler, SocketInteraction interaction)
    {
        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discord interaction {InteractionId} failed", interaction.Id);
            try
            {
                const string message = "🛠️ Wystąpił błąd techniczny. To nie jest ocena treści. Spróbuj ponownie.";
                if (interaction.HasResponded)
                {
                    await interaction.ModifyOriginalResponseAsync(p => p.Content = message);
                }
                else
                {
                    await interaction.RespondAsync(message, ephemeral: true);
                }
            }
            catch (Exception inner)
            {
                logger.LogDebug(inner, "Could not report failure to Discord");
            }
        }
    }

    private Task OnLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
        logger.Log(level, message.Exception, "Discord {Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
