using System.Security.Claims;
using TECNIHUB.Application.Reviews;

namespace TECNIHUB.API.Endpoints;

public static class PublicProfileEndpoints
{
    public static IEndpointRouteBuilder MapPublicProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/api/profiles")
            .WithTags("PublicProfiles")
            .RequireAuthorization();

        profiles.MapGet("/technicians/{technicianId:guid}", GetPublicTechnicianProfileAsync)
            .WithName("GetPublicTechnicianProfile")
            .Produces<PublicTechnicianProfileDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        profiles.MapGet("/clients/{clientId:guid}", GetPublicClientProfileAsync)
            .WithName("GetPublicClientProfile")
            .Produces<PublicClientProfileDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/repairs/{id:guid}/review", GetRepairReviewAsync)
            .RequireAuthorization()
            .WithTags("Reviews")
            .WithName("GetRepairReview")
            .Produces<PublicReviewItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/repairs/{id:guid}/review", CreateRepairReviewAsync)
            .RequireAuthorization("ClientOnly")
            .WithTags("Reviews")
            .WithName("CreateRepairReview")
            .Produces<PublicReviewItemDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetPublicTechnicianProfileAsync(
        Guid technicianId,
        IPublicProfileService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPublicTechnicianProfileAsync(technicianId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetPublicClientProfileAsync(
        Guid clientId,
        ClaimsPrincipal user,
        IPublicProfileService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var actorId) || !EndpointHelpers.TryGetUserRole(user, out var role))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetPublicClientProfileAsync(
            clientId,
            actorId,
            role,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetRepairReviewAsync(
        Guid id,
        ClaimsPrincipal user,
        IPublicProfileService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var actorId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetReviewForRepairAsync(id, actorId, cancellationToken);
        if (!result.IsSuccess)
        {
            return EndpointHelpers.ToProblemResult(result.Error!);
        }

        return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateRepairReviewAsync(
        Guid id,
        CreateServiceReviewInput input,
        ClaimsPrincipal user,
        IPublicProfileService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var clientId))
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateReviewAsync(
            id,
            clientId,
            input,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/repairs/{id}/review", result.Value)
            : EndpointHelpers.ToProblemResult(result.Error!);
    }
}
