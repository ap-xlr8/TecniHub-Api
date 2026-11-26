using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Chat;
using TECNIHUB.Application.Notifications;

namespace TECNIHUB.Infrastructure.Notifications;

public sealed class VapidOptions
{
    public const string SectionName = "Vapid";

    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = "mailto:soporte@tecnihub.local";
}

public sealed class WebPushNotificationDispatcher : IWebPushDispatcher
{
    private readonly INotificationRepository _repository;
    private readonly PushServiceClient _pushClient;
    private readonly VapidOptions _options;
    private readonly ILogger<WebPushNotificationDispatcher> _logger;

    public WebPushNotificationDispatcher(
        INotificationRepository repository,
        HttpClient httpClient,
        IOptions<VapidOptions> options,
        ILogger<WebPushNotificationDispatcher> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
        _pushClient = new PushServiceClient(httpClient);
        if (IsConfigured)
        {
            _pushClient.DefaultAuthentication = new VapidAuthentication(_options.PublicKey, _options.PrivateKey)
            {
                Subject = _options.Subject
            };
        }
    }

    public string PublicKey => _options.PublicKey;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.PublicKey)
        && !string.IsNullOrWhiteSpace(_options.PrivateKey);

    public async Task SendToUserAsync(
        Guid userId,
        string title,
        string body,
        string? actionUrl,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        var subscriptions = await _repository.GetPushSubscriptionsForUserAsync(userId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return;
        }

        // Format compatible with Angular Service Worker (ngsw-worker.js) SwPush notification handler.
        var payload = JsonSerializer.Serialize(new
        {
            notification = new
            {
                title,
                body,
                icon = "/icons/icon-192x192.png",
                badge = "/icons/icon-96x96.png",
                data = new { url = actionUrl ?? "/" }
            }
        });

        var expired = new List<Domain.Entities.PushSubscriptionRecord>();
        foreach (var sub in subscriptions)
        {
            if (!NotificationService.IsValidPushEndpoint(sub.Endpoint))
            {
                expired.Add(sub);
                continue;
            }

            try
            {
                var pushSub = new PushSubscription
                {
                    Endpoint = sub.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        ["p256dh"] = sub.P256dh,
                        ["auth"] = sub.Auth
                    }
                };
                var message = new PushMessage(payload) { TimeToLive = 3600 };
                await _pushClient.RequestPushMessageDeliveryAsync(pushSub, message, cancellationToken);
            }
            catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                expired.Add(sub);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web Push delivery failed for user {UserId}.", userId);
            }
        }

        if (expired.Count > 0)
        {
            foreach (var sub in expired)
            {
                await _repository.RemovePushSubscriptionAsync(sub, cancellationToken);
            }

            await _repository.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class NoOpRealtimeNotifier : IRealtimeNotifier
{
    public Task NotifyChatMessageAsync(Guid recipientUserId, ChatMessageDto message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyUserNotificationAsync(Guid recipientUserId, UserNotificationDto notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
