using TECNIHUB.Application.PostalCodes;

namespace TECNIHUB.API.Endpoints;

public static class PostalCodeEndpoints
{
    public static IEndpointRouteBuilder MapPostalCodeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/postal-codes/{postalCode}", LookupAsync)
            .RequireAuthorization()
            .WithTags("PostalCodes")
            .WithName("LookupPostalCode")
            .Produces<PostalCodeLookupDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> LookupAsync(
        string postalCode,
        IPostalCodeService postalCodeService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await postalCodeService.LookupAsync(postalCode, cancellationToken);
        if (!result.IsSuccess)
        {
            return EndpointHelpers.ToProblemResult(result.Error!);
        }

        if (result.Value?.Found == true)
        {
            httpContext.Response.Headers.CacheControl = "private, max-age=86400";
        }

        return Results.Ok(result.Value);
    }
}
