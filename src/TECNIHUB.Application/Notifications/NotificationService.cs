using System.Net;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Application.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly IWebPushDispatcher _webPush;
    private readonly IRealtimeNotifier _realtime;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;

    public NotificationService(
        INotificationRepository repository,
        IWebPushDispatcher webPush,
        IRealtimeNotifier realtime,
        IAuditLogger auditLogger,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _webPush = webPush;
        _realtime = realtime;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Result<NotificationListResponse>> GetMyNotificationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var unread = await _repository.CountUnreadAsync(userId, cancellationToken);
        var items = await _repository.GetRecentForUserAsync(userId, 40, cancellationToken);
        var mapped = items.Select(Map).ToArray();
        return Result<NotificationListResponse>.Success(new NotificationListResponse(unread, mapped));
    }

    public async Task<Result<bool>> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var updated = await _repository.MarkAsReadAsync(notificationId, userId, _clock.UtcNow, cancellationToken);
        if (updated)
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return Result<bool>.Success(updated);
    }

    public async Task<Result<int>> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var updated = await _repository.MarkAllAsReadAsync(userId, _clock.UtcNow, cancellationToken);
        if (updated > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return Result<int>.Success(updated);
    }

    public VapidPublicKeyResponse GetVapidPublicKey() =>
        new(_webPush.IsConfigured, _webPush.IsConfigured ? _webPush.PublicKey : null);

    public async Task<Result<bool>> RegisterPushSubscriptionAsync(
        Guid userId,
        RegisterPushSubscriptionInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var endpoint = input.Endpoint?.Trim() ?? string.Empty;
        var p256dh = input.P256dh?.Trim() ?? string.Empty;
        var auth = input.Auth?.Trim() ?? string.Empty;

        if (!IsValidPushEndpoint(endpoint) || string.IsNullOrWhiteSpace(p256dh) || p256dh.Length > 256
            || string.IsNullOrWhiteSpace(auth) || auth.Length > 128)
        {
            return Result<bool>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_push_subscription",
                "La suscripción de notificaciones push no es válida."));
        }

        var utcNow = _clock.UtcNow;
        var existing = await _repository.GetPushSubscriptionByEndpointAsync(endpoint, cancellationToken);
        var reassigned = false;
        if (existing is not null)
        {
            reassigned = existing.UserId != userId;
            existing.UserId = userId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.UserAgent = TrimOrNull(metadata.UserAgent, 300);
            existing.UpdatedAt = utcNow;
        }
        else
        {
            var currentSubs = await _repository.GetPushSubscriptionsForUserAsync(userId, cancellationToken);
            if (currentSubs.Count >= 5)
            {
                foreach (var oldest in currentSubs.OrderBy(s => s.UpdatedAt).Take(currentSubs.Count - 4))
                {
                    await _repository.RemovePushSubscriptionAsync(oldest, cancellationToken);
                }
            }

            await _repository.AddPushSubscriptionAsync(new PushSubscriptionRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = TrimOrNull(metadata.UserAgent, 300),
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            }, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "PushSubscriptionRegistered",
            entityName: "PushSubscription",
            entityId: userId.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: userId,
            metadata: new { reassigned },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> UnregisterPushSubscriptionAsync(
        Guid userId,
        UnregisterPushSubscriptionInput input,
        CancellationToken cancellationToken = default)
    {
        var endpoint = input.Endpoint?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Result<bool>.Success(false);
        }

        var existing = await _repository.GetPushSubscriptionByEndpointAsync(endpoint, cancellationToken);
        if (existing is null || existing.UserId != userId)
        {
            return Result<bool>.Success(false);
        }

        await _repository.RemovePushSubscriptionAsync(existing, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task NotifyAsync(
        Guid recipientUserId,
        string type,
        string title,
        string body,
        string? actionUrl,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;
        var entity = new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = recipientUserId,
            Type = Truncate(type, 64),
            Title = Truncate(title, 140),
            Body = Truncate(body, 320),
            ActionUrl = string.IsNullOrWhiteSpace(actionUrl) ? null : Truncate(actionUrl.Trim(), 250),
            IsRead = false,
            CreatedAt = utcNow
        };

        await _repository.AddNotificationAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var dto = Map(entity);
        try
        {
            await _realtime.NotifyUserNotificationAsync(recipientUserId, dto, cancellationToken);
        }
        catch
        {
            // Real-time fan-out is best-effort; DB notification is already persisted.
        }

        try
        {
            await _webPush.SendToUserAsync(recipientUserId, entity.Title, entity.Body, entity.ActionUrl, cancellationToken);
        }
        catch
        {
            // Web Push delivery is best-effort and never blocks domain operations.
        }
    }

    public static bool IsValidPushEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 2000)
        {
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.IsLoopback)
        {
            return false;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // Reject any raw IP literals (including IPv4-mapped IPv6, ULA, CGNAT, loopback)
        if (IPAddress.TryParse(host, out _))
        {
            return false;
        }

        // Allow only official Web Push gateway domains (prevents DNS rebinding & SSRF).
        return host == "fcm.googleapis.com"
            || host == "android.googleapis.com"
            || host == "updates.push.services.mozilla.com"
            || host.EndsWith(".push.services.mozilla.com", StringComparison.Ordinal)
            || host == "web.push.apple.com"
            || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
            || host.EndsWith(".notify.windows.com", StringComparison.Ordinal);
    }

    private static UserNotificationDto Map(UserNotification n) => new(
        n.Id,
        n.Type,
        n.Title,
        n.Body,
        n.ActionUrl,
        n.IsRead,
        n.ReadAt,
        n.CreatedAt);

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Truncate(value, maxLength);
    }
}
