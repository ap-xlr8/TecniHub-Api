using TECNIHUB.Application.Common;

namespace TECNIHUB.Application.Notifications;

public sealed record UserNotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? ActionUrl,
    bool IsRead,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt);

public sealed record NotificationListResponse(
    int UnreadCount,
    IReadOnlyList<UserNotificationDto> Items);

public sealed record VapidPublicKeyResponse(
    bool Enabled,
    string? PublicKey);

public sealed record RegisterPushSubscriptionInput(
    string Endpoint,
    string P256dh,
    string Auth);

public sealed record UnregisterPushSubscriptionInput(string Endpoint);

public interface INotificationService
{
    Task<Result<NotificationListResponse>> GetMyNotificationsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<bool>> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task<Result<int>> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);
    VapidPublicKeyResponse GetVapidPublicKey();
    Task<Result<bool>> RegisterPushSubscriptionAsync(Guid userId, RegisterPushSubscriptionInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<bool>> UnregisterPushSubscriptionAsync(Guid userId, UnregisterPushSubscriptionInput input, CancellationToken cancellationToken = default);
    Task NotifyAsync(Guid recipientUserId, string type, string title, string body, string? actionUrl, CancellationToken cancellationToken = default);
}
