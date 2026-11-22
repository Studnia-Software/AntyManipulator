using AntyManipulator.Api.Requests;
using AntyManipulator.Core.Channels;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Domain;
using AntyManipulator.Core.Pipeline;
using Microsoft.AspNetCore.Mvc;

namespace AntyManipulator.Api.Controllers;

public static class ChecksController
{
    public const string RateLimitPolicy = "checks";

    public static IEndpointRouteBuilder MapChecks(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Sprawdzanie");

        group.MapPost("/checks", CreateAsync)
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Sprawdź tekst lub adres URL")
            .Produces<CheckResult>()
            .ProducesValidationProblem();

        group.MapGet("/checks/{id:guid}", Get)
            .WithSummary("Pobierz wynik wcześniejszego sprawdzenia (do restartu aplikacji)");

        group.MapGet("/sources", GetSources)
            .WithSummary("Rejestr źródeł, z których korzysta system");

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateCheckRequest request,
        HttpContext http,
        [FromServices] IFactCheckPipeline pipeline,
        [FromServices] IUserPseudonymizer pseudonymizer,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await pipeline.RunAsync(new CheckRequest
        {
            Channel = "web",
            // Pseudonim z adresu IP; surowe IP nie trafia do zapisu.
            UserHash = pseudonymizer.Pseudonymize("web", http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            Text = request.Text,
            Url = request.Url,
        }, cancellationToken);

        return Results.Ok(result);
    }

    private static IResult Get(Guid id, [FromServices] ICheckResultStore store) =>
        store.Get(id) is { } result ? Results.Ok(result) : Results.NotFound();

    private static IResult GetSources([FromServices] ISourceRegistry registry) =>
        Results.Ok(registry.All.Select(s => new
        {
            s.Id,
            s.Name,
            s.Publisher,
            s.Type,
            TypeLabel = Labels.For(s.Type),
            s.Language,
            s.BaseUrl,
            s.Reliability,
            s.Enabled,
            s.Description,
            s.CoverageNote,
            s.DisabledReason,
        }));

    private static Dictionary<string, string[]> Validate(CreateCheckRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var hasText = !string.IsNullOrWhiteSpace(request.Text);
        var hasUrl = !string.IsNullOrWhiteSpace(request.Url);

        if (!hasText && !hasUrl)
        {
            errors["text"] = ["Wklej tekst albo podaj adres strony."];
        }

        if (request.Text?.Length > 20_000)
        {
            errors["text"] = ["Tekst jest za długi. Wklej najważniejszy fragment (do 5000 znaków)."];
        }

        if (hasUrl && (request.Url!.Length > 2048
                       || !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri)
                       || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            errors["url"] = ["Podaj pełny adres strony zaczynający się od http:// lub https://."];
        }

        return errors;
    }

}
