using System.Collections.Concurrent;
using AntyManipulator.Core.Domain;

namespace AntyManipulator.Core.Pipeline;

public interface ICheckResultStore
{
    void Save(CheckResult result);

    CheckResult? Get(Guid id);
}

/// <summary>Ostatnie wyniki w pamięci, żeby dało się je otworzyć po identyfikatorze (do restartu aplikacji).</summary>
public sealed class InMemoryCheckResultStore : ICheckResultStore
{
    private const int Capacity = 1000;
    private readonly ConcurrentDictionary<Guid, CheckResult> _results = new();
    private readonly ConcurrentQueue<Guid> _order = new();

    public void Save(CheckResult result)
    {
        if (_results.TryAdd(result.Id, result))
        {
            _order.Enqueue(result.Id);
        }

        while (_order.Count > Capacity && _order.TryDequeue(out var old))
        {
            _results.TryRemove(old, out _);
        }
    }

    public CheckResult? Get(Guid id) => _results.TryGetValue(id, out var r) ? r : null;
}
