using TECNIHUB.Application.Chat;
using TECNIHUB.Application.Notifications;
using TECNIHUB.Domain.Entities;

namespace TECNIHUB.Application.Abstractions;

public interface INotificationRepository
{
    Task AddNotificationAsync(UserNotification notification, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserNotification>> GetRecentForUserAsync(Guid userId, int take = 40, CancellationToken cancellationToken = default);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<int> MarkAllAsReadAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<PushSubscriptionRecord?> GetPushSubscriptionByEndpointAsync(string endpoint, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PushSubscriptionRecord>> GetPushSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddPushSubscriptionAsync(PushSubscriptionRecord subscription, CancellationToken cancellationToken = default);
    Task RemovePushSubscriptionAsync(PushSubscriptionRecord subscription, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IWebPushDispatcher
{
    string PublicKey { get; }
    bool IsConfigured { get; }
    Task SendToUserAsync(Guid userId, string title, string body, string? actionUrl, CancellationToken cancellationToken = default);
}

public interface IRealtimeNotifier
{
    Task NotifyChatMessageAsync(Guid recipientUserId, ChatMessageDto message, CancellationToken cancellationToken = default);
    Task NotifyUserNotificationAsync(Guid recipientUserId, UserNotificationDto notification, CancellationToken cancellationToken = default);
}
