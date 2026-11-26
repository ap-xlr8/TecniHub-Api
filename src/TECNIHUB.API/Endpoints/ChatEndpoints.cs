using System.Security.Claims;
using TECNIHUB.Application.Chat;

namespace TECNIHUB.API.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat")
            .WithTags("Chat")
            .RequireAuthorization();

        group.MapGet("/conversations", GetMyConversationsAsync)
            .WithName("GetMyChatConversations")
            .Produces<IReadOnlyList<ChatConversationSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/unread-count", GetUnreadCountAsync)
            .WithName("GetChatUnreadCount")
            .Produces<UnreadCountDto>(StatusCodes.Status200OK);

        group.MapPost("/conversations/repair/{repairId:guid}", OpenRepairConversationAsync)
            .WithName("OpenRepairChatConversation")
            .Produces<ChatConversationSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/conversations/spare-part/{sparePartId:guid}", OpenSparePartConversationAsync)
            .RequireAuthorization("TechnicianOnly")
            .WithName("OpenSparePartChatConversation")
            .Produces<ChatConversationSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/conversations/{id:guid}/messages", GetMessagesAsync)
            .WithName("GetChatMessages")
            .Produces<ChatThreadDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/conversations/{id:guid}/messages", SendMessageAsync)
            .RequireRateLimiting("ChatRateLimit")
            .WithName("SendChatMessage")
            .Produces<ChatMessageDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> GetMyConversationsAsync(
        ClaimsPrincipal user,
        IChatService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMyConversationsAsync(userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetUnreadCountAsync(
        ClaimsPrincipal user,
        IChatService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetUnreadCountAsync(userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> OpenRepairConversationAsync(
        Guid repairId,
        ClaimsPrincipal user,
        IChatService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.OpenRepairConversationAsync(
            repairId,
            userId,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> OpenSparePartConversationAsync(
        Guid sparePartId,
        ClaimsPrincipal user,
        IChatService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.OpenSparePartConversationAsync(
            sparePartId,
            userId,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetMessagesAsync(
        Guid id,
        DateTimeOffset? after,
        ClaimsPrincipal user,
        IChatService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMessagesAsync(
            id,
            userId,
            after,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> SendMessageAsync(
        Guid id,
        SendChatMessageInput input,
        ClaimsPrincipal user,
        IChatService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.SendMessageAsync(
            id,
            userId,
            input,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/chat/conversations/{id}/messages", result.Value)
            : EndpointHelpers.ToProblemResult(result.Error!);
    }
}
