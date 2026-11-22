using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Sources;

/// <summary>
/// Buduje źródła z sekcji "Sources:Definitions", korzystając z zarejestrowanych fabryk
/// (<see cref="IEvidenceSourceFactory"/>). Nie zna konkretnych adapterów: nowe źródło dodaje się
/// przez rejestrację fabryki i wpis w konfiguracji. Źródło bez fabryki albo bez wymaganego klucza
/// jest jawnie wyłączone z podanym powodem, a nie zatrzymuje aplikacji.
/// </summary>
public sealed class SourceRegistry : ISourceRegistry
{
    public SourceRegistry(
        IOptions<SourcesOptions> options,
        IEnumerable<IEvidenceSourceFactory> factories,
        ILogger<SourceRegistry> logger)
    {
        var opts = options.Value;
        var byAdapter = factories
            .GroupBy(f => f.Adapter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var context = new SourceFactoryContext(opts.Definitions, opts);

        var all = new List<SourceDescriptor>();
        var active = new List<IEvidenceSource>();

        foreach (var definition in opts.Definitions)
        {
            if (!definition.Enabled)
            {
                all.Add(definition.ToDescriptor());
                continue;
            }

            if (!byAdapter.TryGetValue(definition.Adapter, out var factory))
            {
                logger.LogWarning("Source {SourceId}: no factory registered for adapter {Adapter}", definition.Id, definition.Adapter);
                all.Add(definition.ToDescriptor() with { Enabled = false, DisabledReason = $"nieznany adapter \"{definition.Adapter}\"" });
                continue;
            }

            var reason = factory.UnavailableReason(definition);
            if (reason is not null)
            {
                all.Add(definition.ToDescriptor() with { Enabled = false, DisabledReason = reason });
                continue;
            }

            try
            {
                var source = factory.Create(definition, context);
                active.Add(source);
                all.Add(source.Descriptor);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Source {SourceId}: adapter {Adapter} failed to initialise", definition.Id, definition.Adapter);
                all.Add(definition.ToDescriptor() with { Enabled = false, DisabledReason = "błąd konfiguracji źródła" });
            }
        }

        All = all;
        Active = active;
    }

    public IReadOnlyList<SourceDescriptor> All { get; }

    public IReadOnlyList<IEvidenceSource> Active { get; }
}
