using System.Collections.Concurrent;
using System.Net;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Http;

/// <summary>
/// Stan "grzeczności" wobec hostów źródeł: limit równoległych połączeń i okres
/// wstrzymania po odpowiedzi 429/503. Wspólny dla wszystkich sprawdzeń w procesie.
/// </summary>
public sealed class HostThrottle(IOptions<FetchOptions> options, TimeProvider time)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _coolDownUntil = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> AcquireAsync(string host, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(host, _ => new SemaphoreSlim(options.Value.MaxConcurrentPerHost));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    /// <summary>Ile jeszcze trzeba czekać, zanim wolno znów pytać ten host.</summary>
    public TimeSpan RemainingCoolDown(string host) =>
        _coolDownUntil.TryGetValue(host, out var until) && until > time.GetUtcNow() ? until - time.GetUtcNow() : TimeSpan.Zero;

    public void CoolDown(string host, TimeSpan delay)
    {
        var until = time.GetUtcNow() + delay;
        _coolDownUntil.AddOrUpdate(host, until, (_, existing) => existing > until ? existing : until);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}

/// <summary>
/// Handler dla zapytań do źródeł. Nie obchodzi limitów, tylko się do nich stosuje:
/// - najwyżej N równoległych zapytań do jednego hosta,
/// - po 429/503 respektuje Retry-After (domyślnie 5 s, jak zaleca Wikimedia),
/// - w czasie wstrzymania nie wysyła zapytań do hosta, tylko od razu zgłasza limit,
///   żeby źródło trafiło do ograniczeń wyniku zamiast blokować sprawdzenie.
/// </summary>
public sealed class PoliteHttpHandler(
    HostThrottle throttle,
    IOptions<FetchOptions> options,
    ILogger<PoliteHttpHandler> logger) : DelegatingHandler
{
    private static readonly TimeSpan DefaultBackOff = TimeSpan.FromSeconds(5);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        var maxWait = TimeSpan.FromSeconds(options.Value.MaxRetryWaitSeconds);

        for (var attempt = 0; ; attempt++)
        {
            var remaining = throttle.RemainingCoolDown(host);
            if (remaining > TimeSpan.Zero)
            {
                if (remaining > maxWait)
                {
                    throw RateLimited(host, remaining);
                }

                await Task.Delay(remaining, cancellationToken);
            }

            HttpResponseMessage response;
            using (await throttle.AcquireAsync(host, cancellationToken))
            {
                response = await base.SendAsync(request, cancellationToken);
            }

            if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
            {
                return response;
            }

            var delay = RetryAfter(response) ?? DefaultBackOff * Math.Pow(2, attempt);
            throttle.CoolDown(host, delay);
            logger.LogInformation("Host {Host} returned {Status}; pausing requests for {Delay}", host, (int)response.StatusCode, delay);

            if (attempt >= 1 || delay > maxWait)
            {
                return response;
            }

            response.Dispose();
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var d = date - DateTimeOffset.UtcNow;
            return d > TimeSpan.Zero ? d : TimeSpan.Zero;
        }

        return null;
    }

    private static HttpRequestException RateLimited(string host, TimeSpan remaining) =>
        new($"Rate limit for {host}, retry in {remaining.TotalSeconds:0}s", null, HttpStatusCode.TooManyRequests);
}
