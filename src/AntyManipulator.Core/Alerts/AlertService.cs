using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Core.Alerts;

public sealed record UnansweredCluster
{
    public required string Representative { get; init; }
    public required int DistinctUsers { get; init; }
    public required int Occurrences { get; init; }
    public required DateTimeOffset LastSeen { get; init; }
    public required IReadOnlyList<string> Examples { get; init; }
    public bool Alerted { get; init; }
}

public interface IAlertService
{
    Task EvaluateAsync(Guid checkId, string userHash, ClaimResult claim, CancellationToken cancellationToken);

    IReadOnlyList<Alert> RecentAlerts();

    IReadOnlyList<UnansweredCluster> UnansweredClusters();
}

/// <summary>
/// Powiadamia odbiorców wskazanych przez administratora o sprawach wysokiego ryzyka bez
/// rozstrzygnięcia oraz o powtarzających się podobnych pytaniach, na które brakuje źródeł.
/// Stan trzymany w pamięci (prototyp bez bazy danych).
/// </summary>
public sealed class AlertService(
    IAlertNotifier notifier,
    IAuditLog auditLog,
    IOptions<AlertOptions> options,
    TimeProvider time,
    ILogger<AlertService> logger) : IAlertService
{
    private const int MaxEntries = 5000;
    private const int MaxAlerts = 200;

    private readonly Lock _lock = new();
    private readonly List<Entry> _entries = [];
    private readonly LinkedList<Alert> _alerts = new();

    public async Task EvaluateAsync(Guid checkId, string userHash, ClaimResult claim, CancellationToken cancellationToken)
    {
        var unanswered = claim.Outcome.Verdict == Verdict.InsufficientEvidence
                         || claim.Outcome.Status == ServiceStatus.CannotCheck;
        if (!unanswered)
        {
            return;
        }

        var opts = options.Value;
        var now = time.GetUtcNow();
        var toSend = new List<Alert>();

        if (claim.Risk == RiskLevel.High)
        {
            toSend.Add(new Alert
            {
                Id = Guid.NewGuid(),
                CreatedAt = now,
                Type = "HighRisk",
                Title = "Sprawa wysokiego ryzyka bez rozstrzygnięcia",
                Message = $"Twierdzenie wysokiego ryzyka otrzymało wynik \"{claim.Outcome.Label}\". Brakuje źródeł do odpowiedzi.",
                ClaimText = claim.Text,
                CheckId = checkId,
                DistinctUsers = 1,
            });
        }

        var tokens = TextTools.Tokens(claim.Text);
        lock (_lock)
        {
            _entries.RemoveAll(e => now - e.At > TimeSpan.FromHours(opts.RepeatedWindowHours));
            if (_entries.Count >= MaxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaxEntries + 1);
            }

            var entry = new Entry(tokens, claim.Text, userHash, now);
            _entries.Add(entry);

            var similar = _entries.Where(e => TextTools.Jaccard(e.Tokens, tokens) >= opts.SimilarityThreshold).ToList();
            var users = similar.Select(e => e.UserHash).Distinct().Count();
            var alreadyAlerted = similar.Any(e => e.Alerted);

            if (users >= opts.RepeatedThreshold && !alreadyAlerted)
            {
                foreach (var e in similar)
                {
                    e.Alerted = true;
                }

                toSend.Add(new Alert
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = now,
                    Type = "Repeated",
                    Title = "Powtarzające się pytanie bez odpowiedzi",
                    Message = $"{users} różne osoby w ciągu {opts.RepeatedWindowHours} h pytały o podobne twierdzenie, a system nie miał źródeł do odpowiedzi.",
                    ClaimText = claim.Text,
                    CheckId = checkId,
                    DistinctUsers = users,
                    Examples = similar.Select(e => e.Text).Distinct().Take(5).ToList(),
                });
            }
        }

        foreach (var alert in toSend)
        {
            IReadOnlyList<string> notified = [];
            try
            {
                notified = await notifier.NotifyAsync(alert, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Alert notification failed for {AlertId}", alert.Id);
            }

            var stored = alert with { NotifiedRecipients = notified };
            logger.LogWarning("ALERT {Type}: {Title} | {Claim} | powiadomieni: {Recipients}",
                stored.Type, stored.Title, stored.ClaimText, string.Join(", ", notified));

            lock (_lock)
            {
                _alerts.AddFirst(stored);
                while (_alerts.Count > MaxAlerts)
                {
                    _alerts.RemoveLast();
                }
            }

            try
            {
                await auditLog.WriteAlertAsync(stored, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Writing alert {AlertId} failed", stored.Id);
            }
        }
    }

    public IReadOnlyList<Alert> RecentAlerts()
    {
        lock (_lock)
        {
            return _alerts.ToList();
        }
    }

    public IReadOnlyList<UnansweredCluster> UnansweredClusters()
    {
        var opts = options.Value;
        List<Entry> snapshot;
        lock (_lock)
        {
            var now = time.GetUtcNow();
            snapshot = _entries.Where(e => now - e.At <= TimeSpan.FromHours(opts.RepeatedWindowHours)).ToList();
        }

        var clusters = new List<List<Entry>>();
        foreach (var entry in snapshot.OrderByDescending(e => e.At))
        {
            var cluster = clusters.FirstOrDefault(c => TextTools.Jaccard(c[0].Tokens, entry.Tokens) >= opts.SimilarityThreshold);
            if (cluster is null)
            {
                clusters.Add([entry]);
            }
            else
            {
                cluster.Add(entry);
            }
        }

        return clusters
            .Select(c => new UnansweredCluster
            {
                Representative = c[0].Text,
                DistinctUsers = c.Select(e => e.UserHash).Distinct().Count(),
                Occurrences = c.Count,
                LastSeen = c.Max(e => e.At),
                Examples = c.Select(e => e.Text).Distinct().Take(5).ToList(),
                Alerted = c.Any(e => e.Alerted),
            })
            .OrderByDescending(c => c.DistinctUsers)
            .ThenByDescending(c => c.LastSeen)
            .ToList();
    }

    private sealed class Entry(HashSet<string> tokens, string text, string userHash, DateTimeOffset at)
    {
        public HashSet<string> Tokens { get; } = tokens;
        public string Text { get; } = text;
        public string UserHash { get; } = userHash;
        public DateTimeOffset At { get; } = at;
        public bool Alerted { get; set; }
    }
}
