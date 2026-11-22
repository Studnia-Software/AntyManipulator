using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Channels.Discord;

public sealed class DiscordOptions
{
    public const string Section = "Discord";

    public bool Enabled { get; set; }

    /// <summary>Token bota z Discord Developer Portal (user-secrets: Discord:BotToken). Nigdy w appsettings.json.</summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Serwery, na których komendy rejestrujemy od razu (przydatne przy testach).
    /// Puste = rejestracja globalna (działa wszędzie, także w prywatnych wiadomościach).
    /// </summary>
    public List<ulong> GuildIds { get; set; } = [];

    /// <summary>Publiczny adres API, np. "https://antymanipulator.example.pl". Pusty = bez linku do pełnego raportu.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Czy odpowiedź widzi tylko osoba pytająca (true) czy cały kanał (false).</summary>
    public bool EphemeralReplies { get; set; } = true;

    /// <summary>Limit sprawdzeń na użytkownika Discorda w ciągu minuty.</summary>
    [Range(1, 60)]
    public int ChecksPerUserPerMinute { get; set; } = 3;

    /// <summary>Token interakcji jest ważny 15 minut; zadanie starsze niż ten czas nie zostanie już wykonane.</summary>
    [Range(1, 14)]
    public int MaxQueueWaitMinutes { get; set; } = 12;

    [Range(10, 6000)]
    public int MaxTextLength { get; set; } = 4000;
}
