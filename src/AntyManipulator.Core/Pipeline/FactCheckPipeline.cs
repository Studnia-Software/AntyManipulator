using System.Diagnostics;
using System.Globalization;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Alerts;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Options;
using AntyManipulator.Core.Security;
using AntyManipulator.Core.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Core.Pipeline;

public interface IFactCheckPipeline
{
    Task<CheckResult> RunAsync(CheckRequest request, CancellationToken cancellationToken);
}

public sealed class FactCheckPipeline(
    IContentFetcher fetcher,
    IClaimExtractor extractor,
    IEvidenceRetriever retriever,
    IEvidenceAssessor assessor,
    IAlertService alerts,
    IAuditLog auditLog,
    ICheckResultStore store,
    ISourceRegistry registry,
    ILlmClient llm,
    IOptions<PipelineOptions> options,
    IOptions<PublisherGroupsOptions> publisherGroups,
    TimeProvider time,
    ILogger<FactCheckPipeline> logger) : IFactCheckPipeline
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public async Task<CheckResult> RunAsync(CheckRequest request, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var ctx = new RunContext(Guid.NewGuid(), time.GetUtcNow());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(opts.CheckTimeoutSeconds));
        var ct = timeout.Token;

        var text = InputSanitizer.Sanitize(request.Text, opts.MaxInputChars);
        var url = request.Url?.Trim();
        if (string.IsNullOrEmpty(url) && LooksLikeSingleUrl(text))
        {
            url = text;
            text = string.Empty;
        }

        ctx.Input = new InputSummary
        {
            Kind = string.IsNullOrEmpty(url) ? "text" : string.IsNullOrEmpty(text) ? "url" : "text+url",
            Text = string.IsNullOrEmpty(text) ? null : TextTools.Truncate(text, 1000),
            Url = string.IsNullOrEmpty(url) ? null : url,
        };

        try
        {
            await RunCoreAsync(ctx, request, text, url, ct);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ctx.Status = StatusInfo.Of(ServiceStatus.TechnicalFailure,
                "Sprawdzenie trwało zbyt długo i zostało przerwane. Spróbuj ponownie za chwilę.");
            ctx.Step("timeout", false, 0, "check timeout");
        }
        catch (OperationCanceledException)
        {
            ctx.Status = StatusInfo.Of(ServiceStatus.TechnicalFailure, "Sprawdzenie zostało przerwane.");
            ctx.Step("cancelled", false, 0, "client cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Check {CheckId} failed", ctx.Id);
            ctx.Status = StatusInfo.Of(ServiceStatus.TechnicalFailure,
                "Wystąpił błąd techniczny po naszej stronie. To nie jest ocena treści. Spróbuj ponownie później.");
            ctx.Step("unhandled", false, 0, ex.GetType().Name);
        }

        var result = BuildResult(ctx, request);
        store.Save(result);
        await WriteAuditAsync(ctx, request, text, url, result);
        return result;
    }

    private async Task RunCoreAsync(RunContext ctx, CheckRequest request, string text, string? url, CancellationToken ct)
    {
        var opts = options.Value;
        var material = text;

        // 1. Pobranie strony (opcjonalnie)
        if (!string.IsNullOrEmpty(url))
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    throw new ContentFetchException(FetchFailureKind.InvalidUrl, "To nie jest poprawny adres strony internetowej.");
                }

                // Sprawdzana strona nie może być dowodem na samą siebie.
                AddExcludedDomain(ctx, uri.ToString());
                var page = await fetcher.FetchAsync(uri, ct);
                ctx.Page = page;
                AddExcludedDomain(ctx, page.FinalUrl.ToString());
                ctx.Input = ctx.Input! with { PageTitle = page.Title, PagePublisher = page.Publisher, PagePublishedAt = page.PublishedAt };
                var pageText = InputSanitizer.Sanitize(page.Text, opts.MaxMaterialChars);
                material = string.IsNullOrEmpty(text) ? pageText : text + "\n\n" + pageText;
                if (!string.IsNullOrEmpty(page.Title))
                {
                    material = InputSanitizer.Sanitize(page.Title, 300) + "\n\n" + material;
                }

                ctx.Step("fetch", true, sw.ElapsedMilliseconds);
            }
            catch (ContentFetchException ex)
            {
                ctx.Step("fetch", false, sw.ElapsedMilliseconds, ex.Kind.ToString());
                ctx.Status = StatusInfo.Of(ServiceStatus.CannotCheck, ex.UserMessage);
                return;
            }
        }

        material = TextTools.Truncate(material, opts.MaxMaterialChars);
        if (material.Trim().Length < 10)
        {
            ctx.Status = StatusInfo.Of(ServiceStatus.NeedsClarification,
                "Wklej treść wiadomości, którą chcesz sprawdzić, albo link do artykułu.");
            return;
        }

        ctx.MaterialExcerpt = TextTools.Truncate(material, 2000);
        ctx.InjectionSignals.AddRange(InjectionDetector.Detect(material));

        // 2. Wyodrębnienie twierdzeń
        ClaimExtraction extraction;
        var esw = Stopwatch.StartNew();
        try
        {
            extraction = await extractor.ExtractAsync(material, ct);
            ctx.Step("extract-claims", true, esw.ElapsedMilliseconds);
        }
        catch (LlmException ex)
        {
            ctx.Step("extract-claims", false, esw.ElapsedMilliseconds, ex.Kind + ": " + ex.Message);
            ctx.Status = StatusInfo.Of(ServiceStatus.TechnicalFailure, LlmFailureMessage(ex));
            return;
        }

        if (extraction.InjectionSuspected)
        {
            ctx.InjectionSignals.Add("model:extraction");
        }

        if (extraction.Claims.Count == 0)
        {
            ctx.Status = StatusInfo.Of(ServiceStatus.NeedsClarification,
                "Nie znaleźliśmy w treści konkretnego twierdzenia o faktach. Napisz jednym zdaniem, co dokładnie chcesz sprawdzić, np. kto, co i kiedy powiedział lub zrobił.");
            return;
        }

        // 3-5. Każde twierdzenie osobno: źródła → ocena → werdykt
        foreach (var claim in extraction.Claims)
        {
            var claimResult = await CheckClaimAsync(ctx, claim, ct);
            ctx.Claims.Add(claimResult);

            try
            {
                await alerts.EvaluateAsync(ctx.Id, request.UserHash, claimResult, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Alert evaluation failed for check {CheckId}", ctx.Id);
            }
        }
    }

    private async Task<ClaimResult> CheckClaimAsync(RunContext ctx, Claim claim, CancellationToken ct)
    {
        if (!claim.IsCheckable)
        {
            ctx.Traces.Add(new ClaimTrace { ClaimIndex = claim.Index });
            return VerdictEngine.NotVerifiable(claim);
        }

        if (claim.NeedsClarification)
        {
            ctx.Traces.Add(new ClaimTrace { ClaimIndex = claim.Index });
            return VerdictEngine.Status(claim, ServiceStatus.NeedsClarification,
                claim.ClarificationQuestion ?? "To twierdzenie jest zbyt ogólne. Doprecyzuj, kogo lub czego dotyczy i kiedy.");
        }

        var rsw = Stopwatch.StartNew();
        var retrieval = await retriever.RetrieveAsync(claim, ctx.ExcludedDomains, ct);
        ctx.Step($"retrieve#{claim.Index}", !retrieval.AllSourcesFailed, rsw.ElapsedMilliseconds);
        ctx.SourceLogs.AddRange(retrieval.Logs);

        if (retrieval.AllSourcesFailed)
        {
            ctx.Traces.Add(new ClaimTrace { ClaimIndex = claim.Index, SourceQueries = retrieval.Logs });
            return VerdictEngine.Status(claim, ServiceStatus.CannotCheck,
                "Nie udało się połączyć z żadnym ze źródeł. To nie jest ocena twierdzenia. Spróbuj ponownie później.",
                sourceLogs: retrieval.Logs);
        }

        EvidenceAssessment assessment;
        if (retrieval.Passages.Count == 0)
        {
            assessment = new EvidenceAssessment { Evidence = [] };
        }
        else
        {
            var asw = Stopwatch.StartNew();
            try
            {
                assessment = await assessor.AssessAsync(claim, retrieval.Passages, ct);
                ctx.Step($"assess#{claim.Index}", true, asw.ElapsedMilliseconds);
            }
            catch (LlmException ex)
            {
                ctx.Step($"assess#{claim.Index}", false, asw.ElapsedMilliseconds, ex.Kind + ": " + ex.Message);
                ctx.Traces.Add(new ClaimTrace
                {
                    ClaimIndex = claim.Index,
                    SourceQueries = retrieval.Logs,
                    CandidateCount = retrieval.CandidateCount,
                });
                return VerdictEngine.Status(claim, ServiceStatus.TechnicalFailure, LlmFailureMessage(ex), sourceLogs: retrieval.Logs);
            }

            if (assessment.InjectionSuspected)
            {
                ctx.InjectionSignals.Add($"model:assessment#{claim.Index}");
            }
        }

        ctx.Traces.Add(new ClaimTrace
        {
            ClaimIndex = claim.Index,
            SourceQueries = retrieval.Logs,
            CandidateCount = retrieval.CandidateCount,
            PassagesAssessed = assessment.Evidence.Count,
            QuotesRejected = assessment.Evidence.Count(e => e.Stance != Stance.Unrelated && !e.QuoteVerified),
            Passages = assessment.Evidence.Select(e => new PassageTrace
            {
                Source = e.Document.Source.Name,
                Url = e.Document.Url,
                OwnershipGroup = e.Document.Source.OwnershipGroup,
                Reliability = e.Document.Source.Reliability,
                Stance = e.Stance,
                Relevance = e.Relevance,
                IsContext = e.IsContext,
                QuoteVerified = e.QuoteVerified,
            }).ToList(),
        });

        return VerdictEngine.Decide(claim, assessment, retrieval.Logs, time.GetUtcNow());
    }

    private CheckResult BuildResult(RunContext ctx, CheckRequest request)
    {
        var coverage = ctx.SourceLogs
            .GroupBy(l => l.SourceId)
            .Select(g =>
            {
                var worst = g.Any(l => l.Status == SourceQueryStatus.Failed) ? g.First(l => l.Status == SourceQueryStatus.Failed)
                    : g.FirstOrDefault(l => l.Status == SourceQueryStatus.Ok) ?? g.First();
                return new SourceCoverage { SourceId = g.Key, Name = worst.SourceName, Status = worst.Status, Message = worst.Message };
            })
            .ToList();

        return new CheckResult
        {
            Id = ctx.Id,
            StartedAt = ctx.StartedAt,
            CompletedAt = time.GetUtcNow(),
            Channel = request.Channel,
            Input = ctx.Input!,
            Status = ctx.Status,
            Claims = ctx.Claims.ToList(),
            SourcesConsulted = coverage,
            Limitations = BuildLimitations(ctx, coverage),
            InjectionSuspected = ctx.InjectionSignals.Count > 0,
        };
    }

    private List<string> BuildLimitations(RunContext ctx, IReadOnlyList<SourceCoverage> coverage)
    {
        var list = new List<string>();
        var active = registry.All.Where(s => s.Enabled).ToList();
        var registered = active.Where(s => s.Type != SourceType.Web).ToList();
        var web = active.Where(s => s.Type == SourceType.Web).ToList();

        list.Add("Szukamy w tych źródłach: " + string.Join(", ", registered.Select(s => s.Name)) +
                 (web.Count > 0
                     ? $". Dodatkowo przeszukujemy internet ({string.Join(", ", web.Select(s => s.Name))}), ale widzimy tylko kilka pierwszych wyników."
                     : ". Nie przeszukujemy całego internetu.") +
                 " Brak wyniku nie znaczy, że informacja jest prawdziwa ani że jest fałszywa.");

        if (web.Count > 0)
        {
            list.Add("Strony z wyszukiwarki spoza listy zaufanych domen mają niską wagę i same nie wystarczą do werdyktu.");
        }

        if (ctx.ExcludedPublishers.Count > 0)
        {
            list.Add($"Nie używamy jako dowodu sprawdzanej strony ani innych serwisów tego samego wydawcy ({string.Join(", ", ctx.ExcludedPublishers)}: {string.Join(", ", ctx.ExcludedDomains)}).");
        }
        else if (ctx.ExcludedDomains.Count > 0)
        {
            list.Add("Nie używamy jako dowodu samej sprawdzanej strony (" + string.Join(", ", ctx.ExcludedDomains) + ").");
        }

        var limited = active.Where(s => !string.IsNullOrWhiteSpace(s.CoverageNote)).ToList();
        foreach (var s in limited)
        {
            list.Add($"{s.Name}: {s.CoverageNote}");
        }

        var disabled = registry.All.Where(s => !s.Enabled).ToList();
        if (disabled.Count > 0)
        {
            list.Add("Nie przeszukano: " + string.Join("; ", disabled.Select(s => $"{s.Name} ({s.DisabledReason ?? "wyłączone"})")) + ".");
        }

        var failed = coverage.Where(c => c.Status == SourceQueryStatus.Failed).ToList();
        if (failed.Count > 0)
        {
            list.Add("Tym razem nie udało się przeszukać: " + string.Join(", ", failed.Select(f => $"{f.Name} ({f.Message})")) + ".");
        }

        if (ctx.Page is not null)
        {
            list.Add($"Analizowaliśmy tekst strony pobrany {ctx.Page.FetchedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl)}. Obrazy, filmy i komentarze nie są analizowane.");
        }

        if (ctx.Claims.Count > 0)
        {
            list.Add($"Werdykt wyznaczają jawne reguły na podstawie wagi źródeł. Model językowy ({ModelsDescription()}) wyodrębnia twierdzenia, ocenia fragmenty źródeł i pisze opis. Może się pomylić, dlatego pokazujemy cytaty i linki.");

            if (ctx.Claims.Any(c => c.Evidence.Any(e => e.QuoteTranslationPl is not null)))
            {
                list.Add("Część materiałów jest w innym języku. Cytaty pokazujemy w oryginale (to one są sprawdzane), a polskie tłumaczenie przygotował model i może być niedokładne.");
            }
        }

        if (ctx.InjectionSignals.Count > 0)
        {
            list.Add("Analizowany materiał zawierał polecenia skierowane do systemów AI. Zignorowaliśmy je i nie wpłynęły na zasady oceny.");
        }

        if (ctx.Claims.Any(c => c.Risk == RiskLevel.High))
        {
            list.Add("Sprawa dotyczy zdrowia, bezpieczeństwa, wyborów lub pieniędzy publicznych. Ten wynik nie zastępuje porady lekarza, prawnika ani komunikatów oficjalnych służb.");
        }

        return list;
    }

    private async Task WriteAuditAsync(RunContext ctx, CheckRequest request, string text, string? url, CheckResult result)
    {
        try
        {
            await auditLog.WriteCheckAsync(new AuditRecord
            {
                CheckId = ctx.Id,
                StartedAt = ctx.StartedAt,
                CompletedAt = result.CompletedAt,
                DurationMs = (long)(result.CompletedAt - ctx.StartedAt).TotalMilliseconds,
                Channel = request.Channel,
                UserHash = request.UserHash,
                InputText = string.IsNullOrEmpty(text) ? null : text,
                InputUrl = url,
                FetchedTextExcerpt = ctx.Page is null ? null : ctx.MaterialExcerpt,
                Model = ModelsDescription(),
                PromptVersion = Prompts.Version,
                RulesVersion = VerdictEngine.RulesVersion,
                InjectionSignals = ctx.InjectionSignals.Distinct().ToList(),
                ClaimTraces = ctx.Traces.ToList(),
                Steps = ctx.Steps.ToList(),
                Result = result,
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit write failed for check {CheckId}", ctx.Id);
        }
    }

    private static string LlmFailureMessage(LlmException ex) => ex.Kind switch
    {
        LlmFailureKind.RateLimited => "Usługa analizy jest teraz przeciążona. To nie jest ocena treści. Spróbuj ponownie za minutę.",
        LlmFailureKind.InvalidOutput => "Model analizy zwrócił niepoprawną odpowiedź, więc nie pokazujemy wyniku. Spróbuj ponownie.",
        _ => "Usługa analizy jest chwilowo niedostępna. To nie jest ocena treści. Spróbuj ponownie później.",
    };

    private string ModelsDescription()
    {
        var extraction = llm.ModelFor(LlmPurposes.Extraction);
        var assessment = llm.ModelFor(LlmPurposes.Assessment);
        return extraction == assessment ? extraction : $"ekstrakcja: {extraction}, ocena: {assessment}";
    }

    /// <summary>Wyklucza domenę sprawdzanej strony i wszystkie domeny tego samego wydawcy.</summary>
    private void AddExcludedDomain(RunContext ctx, string url)
    {
        var host = Domains.HostOf(url);
        if (host is null)
        {
            return;
        }

        var domains = new List<string> { host };
        var group = publisherGroups.Value.GroupOf(host);
        if (group is not null)
        {
            domains.AddRange(group.Domains);
            if (!ctx.ExcludedPublishers.Contains(group.Name))
            {
                ctx.ExcludedPublishers.Add(group.Name);
            }
        }

        foreach (var d in domains.Select(d => d.Trim().ToLowerInvariant()))
        {
            if (!ctx.ExcludedDomains.Contains(d))
            {
                ctx.ExcludedDomains.Add(d);
            }
        }
    }

    private static bool LooksLikeSingleUrl(string text) =>
        !string.IsNullOrEmpty(text)
        && !text.Any(char.IsWhiteSpace)
        && Uri.TryCreate(text, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private sealed class RunContext(Guid id, DateTimeOffset startedAt)
    {
        public Guid Id { get; } = id;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public InputSummary? Input { get; set; }
        public FetchedContent? Page { get; set; }
        public string? MaterialExcerpt { get; set; }
        public StatusInfo? Status { get; set; }
        public List<ClaimResult> Claims { get; } = [];
        public List<SourceQueryLog> SourceLogs { get; } = [];
        public List<ClaimTrace> Traces { get; } = [];
        public List<StepTrace> Steps { get; } = [];
        public List<string> InjectionSignals { get; } = [];
        public List<string> ExcludedDomains { get; } = [];
        public List<string> ExcludedPublishers { get; } = [];

        public void Step(string name, bool success, long ms, string? error = null) =>
            Steps.Add(new StepTrace { Step = name, Success = success, DurationMs = ms, Error = error });
    }
}
