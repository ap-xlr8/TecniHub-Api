using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Chat;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.Notifications;
using TECNIHUB.Application.Repairs;
using TECNIHUB.Application.SpareParts;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.UnitTests;

public sealed class VisitTrackingAndNotificationsTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 25, 19, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLogger _audit = new();
    private readonly RequestMetadata _meta = new("127.0.0.1", "UnitTests/4.0");

    [Fact]
    public async Task Repair_requires_between_1_and_6_images_and_supports_visit_tracking_pin_and_closure_rejection()
    {
        var clientId = Guid.NewGuid();
        var techId = Guid.NewGuid();
        var repairRepo = new InMemoryRepairRepo();
        var chatRepo = new InMemoryChatRepo();
        var notifRepo = new InMemoryNotificationRepo();
        var notifService = new NotificationService(notifRepo, new StubWebPush(), new StubRealtime(), _audit, _clock);
        var sut = new RepairRequestService(repairRepo, chatRepo, _audit, _clock, notifService);

        // 0 images -> rejected
        var zeroImg = await sut.CreateAsync(clientId, BuildRepairInput([]), _meta);
        Assert.False(zeroImg.IsSuccess);
        Assert.Equal(ErrorType.Validation, zeroImg.Error!.Type);

        // 7 images -> rejected
        var sevenImgs = Enumerable.Range(1, 7).Select(i => $"https://i.ibb.co/{i}/pic.jpg").ToArray();
        var tooMany = await sut.CreateAsync(clientId, BuildRepairInput(sevenImgs), _meta);
        Assert.False(tooMany.IsSuccess);
        Assert.Equal(ErrorType.Validation, tooMany.Error!.Type);

        // 6 images -> accepted
        var sixImgs = Enumerable.Range(1, 6).Select(i => $"https://i.ibb.co/{i}/pic.jpg").ToArray();
        var created = await sut.CreateAsync(clientId, BuildRepairInput(sixImgs), _meta);
        Assert.True(created.IsSuccess);
        var repairId = created.Value!.Id;

        // Accept -> OnTheWay (generates PIN visible ONLY to client) -> Arrived
        Assert.True((await sut.AcceptAsync(repairId, techId, _meta)).IsSuccess);
        var onTheWay = await sut.MarkOnTheWayAsync(repairId, techId, new MarkOnTheWayInput(20), _meta);
        Assert.True(onTheWay.IsSuccess);
        Assert.Equal("OnTheWay", onTheWay.Value!.Status);
        Assert.Null(onTheWay.Value.ClientArrivalPin); // Never exposed to technician!
        Assert.True(onTheWay.Value.RequiresArrivalPin);

        var arrived = await sut.MarkArrivedAsync(repairId, techId, _meta);
        Assert.True(arrived.IsSuccess);
        Assert.Equal("Arrived", arrived.Value!.Status);
        Assert.Null(arrived.Value.ClientArrivalPin);

        // Client sees the 4-digit PIN
        var clientView = await sut.GetDetailAsync(repairId, clientId, UserRole.Client);
        Assert.True(clientView.IsSuccess);
        var pin = clientView.Value!.ClientArrivalPin;
        Assert.NotNull(pin);
        Assert.Equal(4, pin!.Length);

        // Wrong PIN rejected
        var badPin = await sut.StartWorkAsync(repairId, techId, _meta, new StartWorkInput("0000"));
        Assert.False(badPin.IsSuccess);
        Assert.Equal(ErrorType.Validation, badPin.Error!.Type);

        // Correct PIN starts work and clears PIN
        var started = await sut.StartWorkAsync(repairId, techId, _meta, new StartWorkInput(pin));
        Assert.True(started.IsSuccess);
        Assert.Equal("InProgress", started.Value!.Status);
        Assert.False(started.Value.RequiresArrivalPin);

        // Request closure -> Client rejects with reason -> Back to InProgress -> Re-request -> Confirm
        Assert.True((await sut.RequestClosureAsync(repairId, techId, new RequestClosureInput("Listo"), _meta)).IsSuccess);
        var rejected = await sut.RejectClosureAsync(repairId, clientId, new RejectClosureInput("Sigue fugando agua en el drenaje."), _meta);
        Assert.True(rejected.IsSuccess);
        Assert.Equal("InProgress", rejected.Value!.Status);
        Assert.Equal("Sigue fugando agua en el drenaje.", rejected.Value.ClosureRejectionReason);

        Assert.True((await sut.RequestClosureAsync(repairId, techId, new RequestClosureInput("Empaque cambiado."), _meta)).IsSuccess);
        var confirmed = await sut.ConfirmClosureAsync(repairId, clientId, _meta);
        Assert.True(confirmed.IsSuccess);
        Assert.Equal("Completed", confirmed.Value!.Status);

        // Notifications persisted for both actors
        Assert.Contains(notifRepo.Notifications, n => n.UserId == clientId);
        Assert.Contains(notifRepo.Notifications, n => n.UserId == techId);
    }

    [Fact]
    public async Task Technician_release_reopens_repair_and_spare_part_enforces_images_and_soft_delete()
    {
        var clientId = Guid.NewGuid();
        var techId = Guid.NewGuid();
        var repairRepo = new InMemoryRepairRepo();
        var repairSut = new RepairRequestService(repairRepo, new InMemoryChatRepo(), _audit, _clock);

        var created = await repairSut.CreateAsync(clientId, BuildRepairInput(["https://i.ibb.co/1/r.jpg"]), _meta);
        var repairId = created.Value!.Id;
        var acceptA = await repairSut.AcceptAsync(repairId, techId, _meta);
        Assert.True(acceptA.IsSuccess);

        var released = await repairSut.ReleaseByTechnicianAsync(repairId, techId, new ReleaseRepairInput("Emergencia mecánica, no puedo acudir."), _meta);
        Assert.True(released.IsSuccess);
        Assert.Equal("Open", released.Value!.Status);
        Assert.Null(released.Value.AssignedTechnicianId);
        Assert.Null(released.Value.FullAddress);

        // New tech accepting reopened repair gets an isolated conversation (never shares Tech A's thread)
        var techBId = Guid.NewGuid();
        var acceptB = await repairSut.AcceptAsync(repairId, techBId, _meta);
        Assert.True(acceptB.IsSuccess);
        Assert.NotEqual(acceptA.Value!.ConversationId, acceptB.Value!.ConversationId);

        // Spare parts require >= 1 image and support soft delete by owner only
        var spRepo = new InMemorySparePartRepo();
        var spSut = new SparePartService(spRepo, _audit, _clock);

        var noImgPart = await spSut.CreateAsync(techId, new UpsertSparePartInput(
            "Sensor", "Sensor de temperatura nuevo para refrigerador.", "Refrigeración",
            null, "New", "Sale", 250m, "7731112233", null, null, []), _meta);
        Assert.False(noImgPart.IsSuccess);

        var part = await spSut.CreateAsync(techId, new UpsertSparePartInput(
            "Sensor", "Sensor de temperatura nuevo para refrigerador.", "Refrigeración",
            null, "New", "Sale", 250m, "7731112233", null, null, ["https://i.ibb.co/1/s.jpg"]), _meta);
        Assert.True(part.IsSuccess);

        var otherDelete = await spSut.DeleteAsync(part.Value!.Id, Guid.NewGuid(), _meta);
        Assert.False(otherDelete.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, otherDelete.Error!.Type);

        var ownerDelete = await spSut.DeleteAsync(part.Value.Id, techId, _meta);
        Assert.True(ownerDelete.IsSuccess);
        Assert.Empty((await spSut.GetAvailableAsync(null, null, null)).Value!);
    }

    [Fact]
    public async Task NotificationService_blocks_ssrf_push_endpoints_and_registers_valid_https_endpoint()
    {
        var userId = Guid.NewGuid();
        var repo = new InMemoryNotificationRepo();
        var sut = new NotificationService(repo, new StubWebPush(), new StubRealtime(), _audit, _clock);

        var loopback = await sut.RegisterPushSubscriptionAsync(
            userId,
            new RegisterPushSubscriptionInput("https://127.0.0.1/push", "key", "auth"),
            _meta);
        Assert.False(loopback.IsSuccess);

        var privateIp = await sut.RegisterPushSubscriptionAsync(
            userId,
            new RegisterPushSubscriptionInput("https://192.168.1.5/push", "key", "auth"),
            _meta);
        Assert.False(privateIp.IsSuccess);

        var httpPlain = await sut.RegisterPushSubscriptionAsync(
            userId,
            new RegisterPushSubscriptionInput("http://fcm.googleapis.com/fcm/send/abc", "key", "auth"),
            _meta);
        Assert.False(httpPlain.IsSuccess);

        var ipv4MappedIpv6 = await sut.RegisterPushSubscriptionAsync(
            userId,
            new RegisterPushSubscriptionInput("https://[::ffff:10.0.0.1]/push", "key", "auth"),
            _meta);
        Assert.False(ipv4MappedIpv6.IsSuccess);

        var nipIoRebind = await sut.RegisterPushSubscriptionAsync(
            userId,
            new RegisterPushSubscriptionInput("https://127.0.0.1.nip.io/push", "key", "auth"),
            _meta);
        Assert.False(nipIoRebind.IsSuccess);

        var nonDefaultPort = await sut.RegisterPushSubscriptionAsync(
            userId,
            new RegisterPushSubscriptionInput("https://fcm.googleapis.com:8443/fcm/send/abc", "key", "auth"),
            _meta);
        Assert.False(nonDefaultPort.IsSuccess);

        var valid = await sut.RegisterPushSubscriptionAsync(
            userId,
            new RegisterPushSubscriptionInput("https://fcm.googleapis.com/fcm/send/valid-sub", "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM", "tBHItJI5svbpez7KI4CCXg"),
            _meta);
        Assert.True(valid.IsSuccess);
        Assert.Single(repo.Subscriptions);
    }

    private CreateRepairRequestInput BuildRepairInput(IReadOnlyList<string> imageUrls) => new(
        "Lavadora", "Mabe Aqua", "No llena la tina y marca parpadeo en tablero.",
        DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.AddDays(1)), "Mañana (09:00 - 13:00)",
        "42855", "Hidalgo", "Tepeji del Río de Ocampo", "Tepeji del Río de Ocampo", "Atengo",
        "Calle Hidalgo", "15", null, "Casa blanca", imageUrls);

    private sealed class FakeClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public Task LogAsync(string action, string entityName, string? entityId, AuditOutcome outcome, Guid? actorUserId = null, string? actorRole = null, object? metadata = null, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubWebPush : IWebPushDispatcher
    {
        public string PublicKey => "TEST_PUBLIC_KEY";
        public bool IsConfigured => true;
        public Task SendToUserAsync(Guid userId, string title, string body, string? actionUrl, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubRealtime : IRealtimeNotifier
    {
        public Task NotifyChatMessageAsync(Guid recipientUserId, ChatMessageDto message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyUserNotificationAsync(Guid recipientUserId, UserNotificationDto notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryNotificationRepo : INotificationRepository
    {
        public List<UserNotification> Notifications { get; } = [];
        public List<PushSubscriptionRecord> Subscriptions { get; } = [];
        public Task AddNotificationAsync(UserNotification notification, CancellationToken cancellationToken = default) { Notifications.Add(notification); return Task.CompletedTask; }
        public Task<IReadOnlyList<UserNotification>> GetRecentForUserAsync(Guid userId, int take = 40, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserNotification>>(Notifications.Where(n => n.UserId == userId).ToList());
        public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Notifications.Count(n => n.UserId == userId && !n.IsRead));
        public Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> MarkAllAsReadAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<PushSubscriptionRecord?> GetPushSubscriptionByEndpointAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult(Subscriptions.FirstOrDefault(s => s.Endpoint == endpoint));
        public Task<IReadOnlyList<PushSubscriptionRecord>> GetPushSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PushSubscriptionRecord>>(Subscriptions.Where(s => s.UserId == userId).ToList());
        public Task AddPushSubscriptionAsync(PushSubscriptionRecord subscription, CancellationToken cancellationToken = default) { Subscriptions.Add(subscription); return Task.CompletedTask; }
        public Task RemovePushSubscriptionAsync(PushSubscriptionRecord subscription, CancellationToken cancellationToken = default) { Subscriptions.Remove(subscription); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryRepairRepo : IRepairRequestRepository
    {
        public List<RepairRequest> Items { get; } = [];
        public Task AddAsync(RepairRequest request, CancellationToken cancellationToken = default) { Items.Add(request); return Task.CompletedTask; }
        public Task<RepairRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<RepairRequest>> GetByClientAsync(Guid clientUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RepairRequest>>(Items.Where(r => r.ClientUserId == clientUserId).ToList());
        public Task<IReadOnlyList<RepairRequest>> GetOpenBoardAsync(string? search, string? state = null, string? municipality = null, string? postalCode = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RepairRequest>>(Items.Where(r => r.Status == RepairRequestStatus.Open).ToList());
        public Task<IReadOnlyList<RepairRequest>> GetByAssignedTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RepairRequest>>(Items.Where(r => r.AssignedTechnicianId == technicianUserId).ToList());
        public Task<bool> TryAcceptAtomicAsync(Guid repairRequestId, Guid technicianUserId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            var r = Items.FirstOrDefault(x => x.Id == repairRequestId);
            return Task.FromResult(r is not null && r.Accept(technicianUserId, utcNow));
        }
        public Task ReplaceImagesAsync(Guid repairRequestId, IReadOnlyList<RepairRequestImage> newImages, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemorySparePartRepo : ISparePartRepository
    {
        public List<SparePart> Items { get; } = [];
        public Task AddAsync(SparePart sparePart, CancellationToken cancellationToken = default) { Items.Add(sparePart); return Task.CompletedTask; }
        public Task<SparePart?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<SparePart>> GetAvailableAsync(string? search, string? category, SparePartModality? modality, SparePartCondition? condition = null, decimal? minPriceMxn = null, decimal? maxPriceMxn = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SparePart>>(Items.Where(p => p.IsAvailable && p.DeletedAt == null).ToList());
        public Task<IReadOnlyList<SparePart>> GetByTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SparePart>>(Items.Where(p => p.TechnicianUserId == technicianUserId && p.DeletedAt == null).ToList());
        public Task ReplaceImagesAsync(Guid sparePartId, IReadOnlyList<SparePartImage> newImages, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryChatRepo : IChatRepository
    {
        public List<ChatConversation> Conversations { get; } = [];
        public Task<ChatConversation?> GetConversationByIdAsync(Guid conversationId, CancellationToken cancellationToken = default) => Task.FromResult(Conversations.FirstOrDefault(c => c.Id == conversationId));
        public Task<ChatConversation?> GetByRepairRequestIdAsync(Guid repairRequestId, CancellationToken cancellationToken = default) => Task.FromResult(Conversations.FirstOrDefault(c => c.RepairRequestId == repairRequestId));
        public Task<ChatConversation?> GetByRepairRequestAndTechnicianAsync(Guid repairRequestId, Guid technicianUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Conversations.FirstOrDefault(c => c.RepairRequestId == repairRequestId && c.ParticipantBUserId == technicianUserId));
        public Task<ChatConversation?> GetBySparePartAndTechnicianAsync(Guid sparePartId, Guid interestedTechnicianId, CancellationToken cancellationToken = default) => Task.FromResult<ChatConversation?>(null);
        public Task<IReadOnlyList<ChatConversation>> GetUserConversationsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ChatConversation>>([]);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, DateTimeOffset? afterUtc, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task<int> MarkIncomingMessagesAsReadAsync(Guid conversationId, Guid readerUserId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetUnreadCountForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<Dictionary<Guid, int>> GetUnreadCountsByConversationAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(new Dictionary<Guid, int>());
        public Task<Dictionary<Guid, ChatMessage>> GetLatestMessagesByConversationAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken = default) => Task.FromResult(new Dictionary<Guid, ChatMessage>());
        public Task AddConversationAsync(ChatConversation conversation, CancellationToken cancellationToken = default) { Conversations.Add(conversation); return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
