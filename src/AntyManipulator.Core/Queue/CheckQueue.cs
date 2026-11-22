using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using AntyManipulator.Core.Channels;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Core.Queue;

public sealed class QueueOptions
{
    public const string Section = "Queue";

    /// <summary>Ile zadań może czekać. Gdy kolejka jest pełna, kanał od razu informuje o przeciążeniu.</summary>
    [Range(1, 10_000)]
    public int Capacity { get; set; } = 100;

    /// <summary>Ile sprawdzeń wykonuje się równolegle (ogranicza obciążenie modelu i źródeł).</summary>
    [Range(1, 32)]
    public int Workers { get; set; } = 2;
}

public interface ICheckQueue
{
    /// <summary>Dodaje zadanie. Zwraca false, gdy kolejka jest pełna.</summary>
    bool TryEnqueue(CheckJob job, out int position);

    int Pending { get; }
}

/// <summary>
/// Kolejka w pamięci procesu. Zadania czekające w chwili restartu aplikacji przepadają
/// (świadomy kompromis prototypu bez bazy danych).
/// </summary>
public sealed class InMemoryCheckQueue : ICheckQueue
{
    private readonly Channel<CheckJob> _channel;
    private int _pending;

    public InMemoryCheckQueue(IOptions<QueueOptions> options)
    {
        _channel = Channel.CreateBounded<CheckJob>(new BoundedChannelOptions(options.Value.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public int Pending => Volatile.Read(ref _pending);

    public bool TryEnqueue(CheckJob job, out int position)
    {
        if (!_channel.Writer.TryWrite(job))
        {
            position = 0;
            return false;
        }

        position = Interlocked.Increment(ref _pending);
        return true;
    }

    internal async ValueTask<CheckJob> DequeueAsync(CancellationToken ct)
    {
        var job = await _channel.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        return job;
    }
}

/// <summary>
/// Pobiera zadania z kolejki, uruchamia ten sam pipeline co endpoint WWW (każde zadanie w osobnym
/// scope DI) i przekazuje wynik do kanału, z którego przyszło zgłoszenie.
/// </summary>
public sealed class CheckQueueWorker(
    InMemoryCheckQueue queue,
    IServiceScopeFactory scopes,
    IEnumerable<IChannelResponder> responders,
    IOptions<QueueOptions> options,
    TimeProvider time,
    ILogger<CheckQueueWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, IChannelResponder> _responders =
        responders.GroupBy(r => r.Channel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, options.Value.Workers).Select(i => RunWorkerAsync(i, stoppingToken)));

    private async Task RunWorkerAsync(int index, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CheckJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await ProcessAsync(job, index, stoppingToken);
        }
    }

    private async Task ProcessAsync(CheckJob job, int worker, CancellationToken stoppingToken)
    {
        if (!_responders.TryGetValue(job.Request.Channel, out var responder))
        {
            logger.LogError("No responder for channel {Channel}, dropping job {JobId}", job.Request.Channel, job.JobId);
            return;
        }

        if (job.Deadline is { } deadline && time.GetUtcNow() > deadline)
        {
            await SafeAsync(() => responder.DeliverStatusAsync(job, StatusInfo.Of(ServiceStatus.CannotCheck,
                "Zgłoszenie czekało w kolejce zbyt długo, bo usługa jest przeciążona. To nie jest ocena treści. Spróbuj ponownie."),
                CancellationToken.None), job);
            return;
        }

        logger.LogInformation("Worker {Worker} processing job {JobId} from {Channel} (waited {Wait:0.0}s)",
            worker, job.JobId, job.Request.Channel, (time.GetUtcNow() - job.EnqueuedAt).TotalSeconds);

        CheckResult result;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IFactCheckPipeline>();
            result = await pipeline.RunAsync(job.Request, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await SafeAsync(() => responder.DeliverStatusAsync(job, StatusInfo.Of(ServiceStatus.TechnicalFailure,
                "Usługa została zatrzymana w trakcie sprawdzania. To nie jest ocena treści. Spróbuj ponownie za chwilę."),
                CancellationToken.None), job);
            return;
        }
        catch (Exception ex)
        {
            // Pipeline sam zamienia błędy na stan usługi; tu trafia tylko coś nieprzewidzianego (np. błąd DI).
            logger.LogError(ex, "Job {JobId} failed", job.JobId);
            await SafeAsync(() => responder.DeliverStatusAsync(job, StatusInfo.Of(ServiceStatus.TechnicalFailure,
                "Wystąpił błąd techniczny po naszej stronie. To nie jest ocena treści. Spróbuj ponownie później."),
                CancellationToken.None), job);
            return;
        }

        await SafeAsync(() => responder.DeliverResultAsync(job, result, CancellationToken.None), job);
    }

    private async Task SafeAsync(Func<Task> action, CheckJob job)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delivering job {JobId} to {Channel} failed", job.JobId, job.Request.Channel);
        }
    }
}
