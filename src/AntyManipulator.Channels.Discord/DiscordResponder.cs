using AntyManipulator.Core.Channels;
using AntyManipulator.Core.Domain;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Channels.Discord;

/// <summary>Odbiera wyniki z kolejki i podmienia odpowiedź "Sprawdzam…" na wynik.</summary>
public sealed class DiscordResponder(IOptions<DiscordOptions> options, ILogger<DiscordResponder> logger) : IChannelResponder
{
    public const string ChannelName = "discord";

    public string Channel => ChannelName;

    public async Task DeliverResultAsync(CheckJob job, CheckResult result, CancellationToken cancellationToken)
    {
        if (job.ReplyState is not SocketInteraction interaction)
        {
            logger.LogError("Job {JobId} has no Discord interaction to reply to", job.JobId);
            return;
        }

        var reportUrl = ReportUrl(result.Id);

        // Brak twierdzeń = stan usługi dla całego zgłoszenia (np. awaria, potrzeba doprecyzowania).
        if (result.Claims.Count == 0)
        {
            var status = result.Status ?? StatusInfo.Of(ServiceStatus.NeedsClarification,
                "Nie znaleźliśmy w treści twierdzenia do sprawdzenia.");
            await ModifyAsync(interaction, DiscordFormatter.StatusMessage(status, reportUrl), null);
            return;
        }

        await ModifyAsync(interaction, DiscordFormatter.Header(result), DiscordFormatter.Build(result, reportUrl));
    }

    public Task DeliverStatusAsync(CheckJob job, StatusInfo status, CancellationToken cancellationToken)
    {
        if (job.ReplyState is not SocketInteraction interaction)
        {
            logger.LogError("Job {JobId} has no Discord interaction to reply to", job.JobId);
            return Task.CompletedTask;
        }

        return ModifyAsync(interaction, DiscordFormatter.StatusMessage(status), null);
    }

    private static Task ModifyAsync(SocketInteraction interaction, string content, Embed[]? embeds) =>
        interaction.ModifyOriginalResponseAsync(p =>
        {
            p.Content = content;
            p.Embeds = embeds ?? [];
            p.AllowedMentions = AllowedMentions.None;
        });

    private string? ReportUrl(Guid id)
    {
        var baseUrl = options.Value.PublicBaseUrl?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl}/api/checks/{id}";
    }
}
