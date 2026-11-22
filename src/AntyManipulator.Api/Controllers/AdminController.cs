using System.Security.Cryptography;
using System.Text;
using AntyManipulator.Core.Alerts;
using Microsoft.AspNetCore.Mvc;

namespace AntyManipulator.Api.Controllers;

public static class AdminController
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .WithTags("Administracja")
            .AddEndpointFilter(RequireAdminKey);

        group.MapGet("/alerts", ([FromServices] IAlertService alerts) => Results.Ok(alerts.RecentAlerts()))
            .WithSummary("Ostatnie alerty (wysokie ryzyko, powtarzające się pytania bez odpowiedzi)");

        group.MapGet("/unanswered", ([FromServices] IAlertService alerts) => Results.Ok(alerts.UnansweredClusters()))
            .WithSummary("Grupy podobnych pytań, na które zabrakło źródeł");

        group.MapGet("/queue", ([FromServices] IServiceProvider sp) =>
                Results.Ok(new { pending = sp.GetService<AntyManipulator.Core.Queue.ICheckQueue>()?.Pending ?? 0 }))
            .WithSummary("Liczba zadań czekających w kolejce kanałów asynchronicznych");

        group.MapExport();

        return endpoints;
    }

    private static async ValueTask<object?> RequireAdminKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Admin:ApiKey"];
        if (string.IsNullOrEmpty(configured))
        {
            return Results.Problem("Panel administracyjny jest wyłączony: brak Admin:ApiKey w konfiguracji.", statusCode: 503);
        }

        var provided = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
        var ok = provided.Length > 0 && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));

        return ok ? await next(context) : Results.Unauthorized();
    }
}
