using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Chat;
using TECNIHUB.Application.Notifications;

namespace TECNIHUB.API.Hubs;

[Authorize]
public sealed class RealtimeHub : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }
}

public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<RealtimeHub> _hubContext;

    public SignalRRealtimeNotifier(IHubContext<RealtimeHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyChatMessageAsync(
        Guid recipientUserId,
        ChatMessageDto message,
        CancellationToken cancellationToken = default) =>
        _hubContext.Clients
            .Group(RealtimeHub.UserGroup(recipientUserId))
            .SendAsync("ChatMessageReceived", message, cancellationToken);

    public Task NotifyUserNotificationAsync(
        Guid recipientUserId,
        UserNotificationDto notification,
        CancellationToken cancellationToken = default) =>
        _hubContext.Clients
            .Group(RealtimeHub.UserGroup(recipientUserId))
            .SendAsync("NotificationReceived", notification, cancellationToken);
}
