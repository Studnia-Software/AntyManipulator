using AntyManipulator.Api.Controllers;

namespace AntyManipulator.Api.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDetector();
        
        return endpoints;
    }
}