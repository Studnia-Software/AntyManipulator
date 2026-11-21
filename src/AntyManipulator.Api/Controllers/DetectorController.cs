using AntyManipulator.Api.Requests;
using AntyManipulator.Integrations.Models;
using AntyManipulator.Integrations.Services;
using Microsoft.AspNetCore.Mvc;

namespace AntyManipulator.Api.Controllers;


public static class DetectorController
{
    public static IEndpointRouteBuilder MapDetector(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/detector");

        group.MapPost("/upload", UploadAsync);

        return endpoints;
    }

    private static async Task<AnalysisResultDto> UploadAsync(CheckLinkRequest request, [FromServices] IScalewayService service)
    {
        var res  = await service.AnalyzePost(new UploadMediaModel
        {
            Content = request.LinkToCheck
        });

        return res;

    }
}