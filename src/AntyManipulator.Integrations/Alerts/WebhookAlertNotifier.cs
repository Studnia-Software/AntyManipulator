using System.Net.Http.Json;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Alerts;

/// <summary>
/// Wysyła alert do odbiorców z sekcji "Alerts:Recipients". Treść ma pole "content",
/// więc działa m.in. z webhookami Discorda i Slacka, a pole "alert" niesie pełne dane.
/// Odbiorca bez webhooka jest tylko odnotowany w logu alertów.
/// </summary>
public sealed class WebhookAlertNotifier(
    IHttpClientFactory httpFactory,
    IOptions<AlertOptions> options,
    ILogger<WebhookAlertNotifier> logger) : IAlertNotifier
{
    public const string ClientName = "alerts";

    public async Task<IReadOnlyList<string>> NotifyAsync(Alert alert, CancellationToken cancellationToken)
    {
        var notified = new List<string>();
        var http = httpFactory.CreateClient(ClientName);

        foreach (var r in options.Value.Recipients)
        {
            if (r.AlertTypes.Count > 0 && !r.AlertTypes.Contains(alert.Type, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var label = $"{r.Name} ({r.Role})";
            if (string.IsNullOrWhiteSpace(r.WebhookUrl))
            {
                notified.Add(label + " – log");
                continue;
            }

            if (!Uri.TryCreate(r.WebhookUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                logger.LogWarning("Recipient {Name} has invalid webhook URL (HTTPS required)", r.Name);
                continue;
            }

            var content = $"⚠️ {alert.Title}\n{alert.Message}\nTwierdzenie: {Truncate(alert.ClaimText, 300)}\nSprawdzenie: {alert.CheckId}";
            try
            {
                using var response = await http.PostAsJsonAsync(uri, new { content, alert }, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    notified.Add(label);
                }
                else
                {
                    logger.LogWarning("Webhook for {Name} returned {Status}", r.Name, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Webhook for {Name} failed", r.Name);
            }
        }

        return notified;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
