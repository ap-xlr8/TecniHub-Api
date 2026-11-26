using System.Security.Claims;
using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Application.Profile;

namespace TECNIHUB.API.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        group.MapGet("/me", GetOwnProfileAsync)
            .WithName("GetOwnProfile")
            .Produces<UserSummaryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/me", UpdateOwnProfileAsync)
            .WithName("UpdateOwnProfile")
            .Produces<UserSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetOwnProfileAsync(
        ClaimsPrincipal user,
        IProfileService profileService,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await profileService.GetOwnProfileAsync(userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> UpdateOwnProfileAsync(
        UpdateOwnProfileRequest request,
        ClaimsPrincipal user,
        IProfileService profileService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await profileService.UpdateOwnProfileAsync(
            userId,
            request,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);

        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }
}
