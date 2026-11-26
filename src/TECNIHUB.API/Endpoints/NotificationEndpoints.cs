using System.Security.Claims;
using TECNIHUB.Application.Notifications;

namespace TECNIHUB.API.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet(string.Empty, GetMineAsync)
            .WithName("GetMyNotifications")
            .Produces<NotificationListResponse>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/read", MarkAsReadAsync)
            .WithName("MarkNotificationAsRead")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/read-all", MarkAllAsReadAsync)
            .WithName("MarkAllNotificationsAsRead")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/vapid-public-key", GetVapidPublicKey)
            .WithName("GetVapidPublicKey")
            .Produces<VapidPublicKeyResponse>(StatusCodes.Status200OK);

        group.MapPost("/push-subscriptions", RegisterPushSubscriptionAsync)
            .RequireRateLimiting("ChatRateLimit")
            .WithName("RegisterPushSubscription")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/push-subscriptions", UnregisterPushSubscriptionAsync)
            .WithName("UnregisterPushSubscription")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static async Task<IResult> GetMineAsync(
        ClaimsPrincipal user,
        INotificationService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMyNotificationsAsync(userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> MarkAsReadAsync(
        Guid id,
        ClaimsPrincipal user,
        INotificationService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        await service.MarkAsReadAsync(userId, id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllAsReadAsync(
        ClaimsPrincipal user,
        INotificationService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.MarkAllAsReadAsync(userId, cancellationToken);
        return Results.Ok(new { marked = result.Value });
    }

    private static IResult GetVapidPublicKey(INotificationService service) =>
        Results.Ok(service.GetVapidPublicKey());

    private static async Task<IResult> RegisterPushSubscriptionAsync(
        RegisterPushSubscriptionInput input,
        ClaimsPrincipal user,
        INotificationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterPushSubscriptionAsync(
            userId,
            input,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);

        return result.IsSuccess ? Results.NoContent() : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> UnregisterPushSubscriptionAsync(
        string endpoint,
        ClaimsPrincipal user,
        INotificationService service,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        await service.UnregisterPushSubscriptionAsync(
            userId,
            new UnregisterPushSubscriptionInput(endpoint),
            cancellationToken);
        return Results.NoContent();
    }
}
