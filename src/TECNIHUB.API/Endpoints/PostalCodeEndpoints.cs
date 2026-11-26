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
        CancellationToken cancellationToken)
    {
        var result = await postalCodeService.LookupAsync(postalCode, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }
}
