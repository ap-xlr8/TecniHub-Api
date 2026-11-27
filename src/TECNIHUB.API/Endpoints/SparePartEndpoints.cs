using System.Security.Claims;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.SpareParts;

namespace TECNIHUB.API.Endpoints;

public static class SparePartEndpoints
{
    public static IEndpointRouteBuilder MapSparePartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/spare-parts")
            .WithTags("SpareParts")
            .RequireAuthorization("TechnicianOnly");

        group.MapGet(string.Empty, GetAvailableAsync)
            .WithName("GetAvailableSpareParts")
            .Produces<PagedResult<SparePartSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/mine", GetMineAsync)
            .WithName("GetMySpareParts")
            .Produces<PagedResult<SparePartSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetDetailAsync)
            .WithName("GetSparePartDetail")
            .Produces<SparePartDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost(string.Empty, CreateAsync)
            .WithName("CreateSparePart")
            .Produces<SparePartDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSparePart")
            .Produces<SparePartDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}/availability", SetAvailabilityAsync)
            .WithName("SetSparePartAvailability")
            .Produces<SparePartDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSparePart")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAvailableAsync(
        [AsParameters] SparePartBoardQuery query,
        ISparePartService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAvailableAsync(query, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetMineAsync(
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        ISparePartService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMineAsync(userId, page, pageSize, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetDetailAsync(
        Guid id,
        ClaimsPrincipal user,
        ISparePartService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetDetailAsync(id, userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> CreateAsync(
        UpsertSparePartInput input,
        ClaimsPrincipal user,
        ISparePartService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/spare-parts/{result.Value!.Id}", result.Value)
            : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpsertSparePartInput input,
        ClaimsPrincipal user,
        ISparePartService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateAsync(id, userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> SetAvailabilityAsync(
        Guid id,
        SetSparePartAvailabilityInput input,
        ClaimsPrincipal user,
        ISparePartService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.SetAvailabilityAsync(id, userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        ISparePartService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteAsync(id, userId, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : EndpointHelpers.ToProblemResult(result.Error!);
    }
}
