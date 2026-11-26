using System.Security.Claims;
using TECNIHUB.Application.Repairs;

namespace TECNIHUB.API.Endpoints;

public static class RepairRequestEndpoints
{
    public static IEndpointRouteBuilder MapRepairRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/repairs")
            .WithTags("Repairs")
            .RequireAuthorization();

        group.MapPost(string.Empty, CreateAsync)
            .RequireAuthorization("ClientOnly")
            .WithName("CreateRepairRequest")
            .Produces<RepairDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/mine", GetMineAsync)
            .RequireAuthorization("ClientOnly")
            .WithName("GetMyRepairRequests")
            .Produces<IReadOnlyList<ClientRepairSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/board", GetBoardAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("GetOpenRepairBoard")
            .Produces<IReadOnlyList<OpenRepairBoardItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/itinerary", GetItineraryAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("GetTechnicianItinerary")
            .Produces<IReadOnlyList<ItineraryItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetDetailAsync)
            .WithName("GetRepairRequestDetail")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/images", UpdateImagesAsync)
            .RequireAuthorization("ClientOnly")
            .WithName("UpdateRepairRequestImages")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/accept", AcceptAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("AcceptRepairRequest")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/schedule", ScheduleAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("ScheduleRepairRequest")
            .Produces<ScheduleRepairResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/on-the-way", MarkOnTheWayAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("MarkRepairOnTheWay")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/arrived", MarkArrivedAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("MarkRepairArrived")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/start", StartWorkAsync)
            .RequireAuthorization("TechnicianOnly")
            .RequireRateLimiting("AuthRateLimit")
            .WithName("StartRepairRequest")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/request-closure", RequestClosureAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("RequestRepairClosure")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/reject-closure", RejectClosureAsync)
            .RequireAuthorization("ClientOnly")
            .WithName("RejectRepairClosure")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/confirm-closure", ConfirmClosureAsync)
            .RequireAuthorization("ClientOnly")
            .WithName("ConfirmRepairClosure")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/release", ReleaseByTechnicianAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("ReleaseRepairAssignment")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .RequireAuthorization("ClientOnly")
            .WithName("CancelRepairRequest")
            .Produces<RepairDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateRepairRequestInput input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/repairs/{result.Value!.Id}", result.Value)
            : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetMineAsync(
        ClaimsPrincipal user,
        IRepairRequestService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetClientRequestsAsync(userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetBoardAsync(
        string? search,
        string? state,
        string? municipality,
        string? postalCode,
        IRepairRequestService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetOpenBoardAsync(search, state, municipality, postalCode, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetItineraryAsync(
        ClaimsPrincipal user,
        IRepairRequestService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetTechnicianItineraryAsync(userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetDetailAsync(
        Guid id,
        ClaimsPrincipal user,
        IRepairRequestService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId) || !EndpointHelpers.TryGetUserRole(user, out var role))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetDetailAsync(id, userId, role, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> UpdateImagesAsync(
        Guid id,
        UpdateRepairImagesInput input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateImagesAsync(id, userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> AcceptAsync(
        Guid id,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.AcceptAsync(id, userId, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> ScheduleAsync(
        Guid id,
        ScheduleRepairInput input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ScheduleAsync(id, userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> MarkOnTheWayAsync(
        Guid id,
        MarkOnTheWayInput? input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.MarkOnTheWayAsync(
            id,
            userId,
            input ?? new MarkOnTheWayInput(null),
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> MarkArrivedAsync(
        Guid id,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.MarkArrivedAsync(id, userId, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> StartWorkAsync(
        Guid id,
        StartWorkInput? input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.StartWorkAsync(id, userId, EndpointHelpers.BuildMetadata(httpContext), input, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> RequestClosureAsync(
        Guid id,
        RequestClosureInput? input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RequestClosureAsync(
            id,
            userId,
            input ?? new RequestClosureInput(null),
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> RejectClosureAsync(
        Guid id,
        RejectClosureInput input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RejectClosureAsync(id, userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> ConfirmClosureAsync(
        Guid id,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ConfirmClosureAsync(id, userId, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> ReleaseByTechnicianAsync(
        Guid id,
        ReleaseRepairInput input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ReleaseByTechnicianAsync(id, userId, input, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        CancelRepairInput? input,
        ClaimsPrincipal user,
        IRepairRequestService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.CancelAsync(id, userId, EndpointHelpers.BuildMetadata(httpContext), input, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }
}
