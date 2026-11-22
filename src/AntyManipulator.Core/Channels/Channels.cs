using System.Security.Cryptography;
using System.Text;
using AntyManipulator.Core.Domain;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Core.Channels;

/// <summary>
/// Zadanie sprawdzenia przyjęte z kanału asynchronicznego (Discord, w przyszłości WhatsApp, SMS...).
/// </summary>
public sealed record CheckJob
{
    public Guid JobId { get; init; } = Guid.NewGuid();

    public required CheckRequest Request { get; init; }

    public required DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>Po tym czasie odpowiedź nie ma już sensu (np. token interakcji Discorda wygasa po 15 min).</summary>
    public DateTimeOffset? Deadline { get; init; }

    /// <summary>
    /// Dane potrzebne kanałowi do odpowiedzi (np. obiekt interakcji Discorda). Kolejka jest w pamięci,
    /// więc może to być dowolny obiekt; trwała kolejka wymagałaby tu danych serializowalnych.
    /// </summary>
    public object? ReplyState { get; init; }
}

/// <summary>
/// Kanał, który odbiera wyniki z kolejki. Nowy kanał = implementacja tego interfejsu z własną
/// nazwą w <see cref="Channel"/> (taką samą jak <see cref="CheckRequest.Channel"/>) + rejestracja w DI.
/// </summary>
public interface IChannelResponder
{
    string Channel { get; }

    /// <summary>Wynik sprawdzenia (werdykty albo stan usługi zapisany w <see cref="CheckResult.Status"/>).</summary>
    Task DeliverResultAsync(CheckJob job, CheckResult result, CancellationToken cancellationToken);

    /// <summary>Komunikat bez wyniku: zadanie wygasło w kolejce albo kolejka padła. To nie jest werdykt.</summary>
    Task DeliverStatusAsync(CheckJob job, StatusInfo status, CancellationToken cancellationToken);
}

public sealed class PrivacyOptions
{
    public const string Section = "Privacy";

    /// <summary>Sól do pseudonimizacji identyfikatorów (user-secrets). Pusta = losowa na czas życia procesu.</summary>
    public string? UserHashSalt { get; set; }
}

/// <summary>Zamienia identyfikator użytkownika z kanału (IP, ID Discorda, numer telefonu) na pseudonim.</summary>
public interface IUserPseudonymizer
{
    string Pseudonymize(string channel, string rawId);
}

public sealed class HmacUserPseudonymizer(IOptions<PrivacyOptions> options) : IUserPseudonymizer
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(
        string.IsNullOrEmpty(options.Value.UserHashSalt)
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            : options.Value.UserHashSalt);

    public string Pseudonymize(string channel, string rawId)
    {
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(channel + ":" + rawId));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
