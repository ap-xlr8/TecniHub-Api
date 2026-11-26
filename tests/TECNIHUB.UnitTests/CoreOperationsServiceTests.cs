using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Chat;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.PostalCodes;
using TECNIHUB.Application.Profile;
using TECNIHUB.Application.Repairs;
using TECNIHUB.Application.SpareParts;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Domain.Rules;

namespace TECNIHUB.UnitTests;

public sealed class CoreOperationsServiceTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 25, 16, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLogger _audit = new();
    private readonly RequestMetadata _meta = new("127.0.0.1", "UnitTests/2.0");

    [Fact]
    public void ImageSignatureValidator_accepts_valid_jpeg_png_webp_and_rejects_spoofed_or_oversize()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        byte[] webp = [0x52, 0x49, 0x46, 0x46, 0x24, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];
        byte[] textSpoof = "not a real image!"u8.ToArray();

        Assert.True(ImageSignatureValidator.TryDetectFormat(jpeg, 1024, out var jpegMime, out _));
        Assert.Equal("image/jpeg", jpegMime);

        Assert.True(ImageSignatureValidator.TryDetectFormat(png, 1024, out var pngMime, out _));
        Assert.Equal("image/png", pngMime);

        Assert.True(ImageSignatureValidator.TryDetectFormat(webp, 1024, out var webpMime, out _));
        Assert.Equal("image/webp", webpMime);

        Assert.False(ImageSignatureValidator.TryDetectFormat(textSpoof, textSpoof.Length, out _, out _));
        Assert.False(ImageSignatureValidator.TryDetectFormat(jpeg, ImageSignatureValidator.MaxSizeBytes + 1, out _, out _));

        Assert.True(ImageSignatureValidator.IsAllowedHostedImageUrl("https://i.ibb.co/abc123/photo.jpg"));
        Assert.False(ImageSignatureValidator.IsAllowedHostedImageUrl("http://i.ibb.co/abc123/photo.jpg"));
        Assert.False(ImageSignatureValidator.IsAllowedHostedImageUrl("https://evil.example.com/photo.jpg"));
    }

    [Fact]
    public async Task ProfileService_updates_allowed_fields_and_preserves_role_and_email()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = (int)UserRole.Technician,
            FirstName = "Marco",
            LastName = "Vega",
            Email = "marco@example.com",
            NormalizedEmail = "MARCO@EXAMPLE.COM",
            Status = AccountStatus.Active,
            IsEmailVerified = true,
            TechnicianProfile = new TechnicianProfile
            {
                Specialties = "Lavadoras",
                ExperienceYears = 4,
                ServiceZone = "Tula",
                BusinessName = null
            }
        };
        user.TechnicianProfile.UserId = user.Id;

        var authRepo = new SingleUserAuthRepo(user);
        var sut = new ProfileService(authRepo, _audit, _clock);

        var bad = await sut.UpdateOwnProfileAsync(user.Id, new UpdateOwnProfileRequest("", "Vega", null, "Lavadoras", 4, "Tula", null), _meta);
        Assert.False(bad.IsSuccess);
        Assert.Equal(ErrorType.Validation, bad.Error!.Type);
        Assert.Equal("Marco", user.FirstName);

        var ok = await sut.UpdateOwnProfileAsync(
            user.Id,
            new UpdateOwnProfileRequest("Marco Antonio", "Vega", "7731234567", "Lavadoras y Refrigeradores", 6, "Tula y Tepeji", "Taller Vega"),
            _meta);

        Assert.True(ok.IsSuccess);
        Assert.Equal("Marco Antonio", ok.Value!.FirstName);
        Assert.Equal("marco@example.com", ok.Value.Email);
        Assert.Equal("Technician", ok.Value.Role);
        Assert.Equal("Taller Vega", ok.Value.TechnicianProfile!.BusinessName);
        Assert.Contains(_audit.Entries, e => e.Action == "ProfileUpdated");
    }

    [Fact]
    public async Task PostalCodeService_returns_distinct_neighborhoods_and_not_found_for_unknown_cp()
    {
        var repo = new InMemoryPostalCodeRepo(
        [
            new PostalCodeEntry { PostalCode = "42800", Neighborhood = "Centro", SettlementType = "Colonia", Municipality = "Tula de Allende", State = "Hidalgo", City = "Tula de Allende" },
            new PostalCodeEntry { PostalCode = "42800", Neighborhood = "El Salitre", SettlementType = "Colonia", Municipality = "Tula de Allende", State = "Hidalgo", City = "Tula de Allende" }
        ]);
        var sut = new PostalCodeService(repo);

        var invalid = await sut.LookupAsync("123");
        Assert.False(invalid.IsSuccess);

        var found = await sut.LookupAsync("42800");
        Assert.True(found.IsSuccess);
        Assert.True(found.Value!.Found);
        Assert.Equal("Hidalgo", found.Value.State);
        Assert.Equal(2, found.Value.Neighborhoods.Count);

        var missing = await sut.LookupAsync("99999");
        Assert.True(missing.IsSuccess);
        Assert.False(missing.Value!.Found);
        Assert.Empty(missing.Value.Neighborhoods);
    }

    [Fact]
    public async Task RepairRequest_hides_full_address_before_assignment_and_enforces_two_step_closure()
    {
        var clientId = Guid.NewGuid();
        var techAId = Guid.NewGuid();
        var techBId = Guid.NewGuid();
        var repairsRepo = new InMemoryRepairRepo();
        var chatRepo = new InMemoryChatRepo();
        var sut = new RepairRequestService(repairsRepo, chatRepo, _audit, _clock);

        // Reject more than 6 images
        var tooManyImages = await sut.CreateAsync(clientId, BuildRepairInput(
            [
                "https://i.ibb.co/1/a.jpg", "https://i.ibb.co/2/b.jpg", "https://i.ibb.co/3/c.jpg",
                "https://i.ibb.co/4/d.jpg", "https://i.ibb.co/5/e.jpg", "https://i.ibb.co/6/f.jpg",
                "https://i.ibb.co/7/g.jpg"
            ]), _meta);
        Assert.False(tooManyImages.IsSuccess);

        // Create valid request
        var created = await sut.CreateAsync(clientId, BuildRepairInput(["https://i.ibb.co/1/a.jpg"]), _meta);
        Assert.True(created.IsSuccess);
        Assert.NotNull(created.Value!.FullAddress); // Owner sees full address

        var repairId = created.Value.Id;

        // Before assignment, technician sees location only up to neighborhood
        var openDetailForTech = await sut.GetDetailAsync(repairId, techAId, UserRole.Technician);
        Assert.True(openDetailForTech.IsSuccess);
        Assert.Equal("Centro", openDetailForTech.Value!.Location.Neighborhood);
        Assert.Null(openDetailForTech.Value.FullAddress);

        // Tech A accepts atomically; Tech B gets Conflict
        var acceptA = await sut.AcceptAsync(repairId, techAId, _meta);
        Assert.True(acceptA.IsSuccess);
        Assert.NotNull(acceptA.Value!.FullAddress); // Now assigned tech sees street
        Assert.NotNull(acceptA.Value.ConversationId); // Linked chat created

        var acceptB = await sut.AcceptAsync(repairId, techBId, _meta);
        Assert.False(acceptB.IsSuccess);
        Assert.Equal(ErrorType.Conflict, acceptB.Error!.Type);

        // Unassigned Tech B cannot view assigned detail (privacy & anti-IDOR)
        var detailForTechB = await sut.GetDetailAsync(repairId, techBId, UserRole.Technician);
        Assert.False(detailForTechB.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, detailForTechB.Error!.Type);

        // Schedule by Tech A works and detects overlap on a second repair within 60 mins
        var slot1 = _clock.UtcNow.AddHours(3);
        var scheduled1 = await sut.ScheduleAsync(repairId, techAId, new ScheduleRepairInput(slot1), _meta);
        Assert.True(scheduled1.IsSuccess);
        Assert.False(scheduled1.Value!.HasScheduleOverlap);

        var secondCreated = await sut.CreateAsync(clientId, BuildRepairInput(null), _meta);
        await sut.AcceptAsync(secondCreated.Value!.Id, techAId, _meta);
        var scheduledOverlap = await sut.ScheduleAsync(secondCreated.Value.Id, techAId, new ScheduleRepairInput(slot1.AddMinutes(30)), _meta);
        Assert.True(scheduledOverlap.IsSuccess);
        Assert.True(scheduledOverlap.Value!.HasScheduleOverlap);
        Assert.Single(scheduledOverlap.Value.OverlappingAppointments);

        // Unassigned tech cannot schedule
        var unauthorizedSchedule = await sut.ScheduleAsync(repairId, techBId, new ScheduleRepairInput(slot1.AddHours(4)), _meta);
        Assert.False(unauthorizedSchedule.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, unauthorizedSchedule.Error!.Type);

        // Two-step closure (with mandatory arrival PIN verification)
        var arrived = await sut.MarkArrivedAsync(repairId, techAId, _meta);
        Assert.True(arrived.IsSuccess);
        var clientPin = (await sut.GetDetailAsync(repairId, clientId, UserRole.Client)).Value!.ClientArrivalPin;
        var start = await sut.StartWorkAsync(repairId, techAId, _meta, new StartWorkInput(clientPin));
        Assert.True(start.IsSuccess);
        Assert.Equal("InProgress", start.Value!.Status);

        var reqClose = await sut.RequestClosureAsync(repairId, techAId, new RequestClosureInput("Se reemplazó capacitor."), _meta);
        Assert.True(reqClose.IsSuccess);
        Assert.Equal("ClosureRequested", reqClose.Value!.Status);
        Assert.Null(reqClose.Value.CompletedAt); // Not completed until client confirms

        var confirmClose = await sut.ConfirmClosureAsync(repairId, clientId, _meta);
        Assert.True(confirmClose.IsSuccess);
        Assert.Equal("Completed", confirmClose.Value!.Status);
        Assert.NotNull(confirmClose.Value.CompletedAt);
    }

    [Fact]
    public async Task SparePartService_requires_mxn_price_and_respects_availability()
    {
        var techOwner = Guid.NewGuid();
        var otherTech = Guid.NewGuid();
        var repo = new InMemorySparePartRepo();
        var sut = new SparePartService(repo, _audit, _clock);

        var zeroPrice = await sut.CreateAsync(techOwner, new UpsertSparePartInput(
            "Bomba de desagüe", "Bomba universal para lavadora carga superior en buen estado.", "Lavado",
            "Whirlpool / Mabe", "Used", "Sale", 0m, "7731112233", null, null, null), _meta);
        Assert.False(zeroPrice.IsSuccess);
        Assert.Equal(ErrorType.Validation, zeroPrice.Error!.Type);

        var created = await sut.CreateAsync(techOwner, new UpsertSparePartInput(
            "Bomba de desagüe", "Bomba universal para lavadora carga superior en buen estado.", "Lavado",
            "Whirlpool / Mabe", "Used", "Sale", 450m, "7731112233", null, "Entregas en Tula centro",
            ["https://i.ibb.co/1/part.webp"]), _meta);
        Assert.True(created.IsSuccess);
        Assert.Equal(450m, created.Value!.PriceMxn);

        var boardBefore = await sut.GetAvailableAsync(null, null, null);
        Assert.Single(boardBefore.Value!);

        var hideByOther = await sut.SetAvailabilityAsync(created.Value.Id, otherTech, new SetSparePartAvailabilityInput(false), _meta);
        Assert.False(hideByOther.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, hideByOther.Error!.Type);

        var hideByOwner = await sut.SetAvailabilityAsync(created.Value.Id, techOwner, new SetSparePartAvailabilityInput(false), _meta);
        Assert.True(hideByOwner.IsSuccess);

        var boardAfter = await sut.GetAvailableAsync(null, null, null);
        Assert.Empty(boardAfter.Value!);
    }

    [Fact]
    public async Task ChatService_restricts_access_to_participants_and_never_audits_message_body()
    {
        var clientId = Guid.NewGuid();
        var techId = Guid.NewGuid();
        var intruderId = Guid.NewGuid();
        var repairsRepo = new InMemoryRepairRepo();
        var spareRepo = new InMemorySparePartRepo();
        var chatRepo = new InMemoryChatRepo(repairsRepo, spareRepo);
        var repairSut = new RepairRequestService(repairsRepo, chatRepo, _audit, _clock);
        var chatSut = new ChatService(chatRepo, repairsRepo, spareRepo, _audit, _clock);

        var created = await repairSut.CreateAsync(clientId, BuildRepairInput(null), _meta);
        var accepted = await repairSut.AcceptAsync(created.Value!.Id, techId, _meta);
        var convId = accepted.Value!.ConversationId!.Value;

        const string secretText = "Nos vemos a las 4 PM en la puerta azul.";
        var sent = await chatSut.SendMessageAsync(convId, clientId, new SendChatMessageInput(secretText), _meta);
        Assert.True(sent.IsSuccess);

        // Intruder is denied with Forbidden
        var intruderRead = await chatSut.GetMessagesAsync(convId, intruderId, null, _meta);
        Assert.False(intruderRead.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, intruderRead.Error!.Type);

        var intruderSend = await chatSut.SendMessageAsync(convId, intruderId, new SendChatMessageInput("Hola"), _meta);
        Assert.False(intruderSend.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, intruderSend.Error!.Type);

        // Audit log must NEVER store the message body
        var sendAudit = Assert.Single(_audit.Entries, e => e.Action == "ChatMessageSent");
        Assert.DoesNotContain(secretText, sendAudit.Metadata?.ToString() ?? string.Empty);

        // When repair completes, chat becomes read-only
        await repairSut.MarkArrivedAsync(created.Value.Id, techId, _meta);
        var pin = (await repairSut.GetDetailAsync(created.Value.Id, clientId, UserRole.Client)).Value!.ClientArrivalPin;
        await repairSut.StartWorkAsync(created.Value.Id, techId, _meta, new StartWorkInput(pin));
        await repairSut.RequestClosureAsync(created.Value.Id, techId, new RequestClosureInput(null), _meta);
        await repairSut.ConfirmClosureAsync(created.Value.Id, clientId, _meta);

        var sendAfterComplete = await chatSut.SendMessageAsync(convId, techId, new SendChatMessageInput("Gracias"), _meta);
        Assert.False(sendAfterComplete.IsSuccess);
        Assert.Equal(ErrorType.Conflict, sendAfterComplete.Error!.Type);
    }

    private CreateRepairRequestInput BuildRepairInput(IReadOnlyList<string>? imageUrls) => new(
        ApplianceName: "Refrigerador",
        BrandModel: "Samsung RT38",
        IssueDescription: "No enfría la parte inferior y hace ruido constante en el ventilador.",
        PreferredDate: DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.AddDays(1)),
        PreferredTimeSlot: "Mañana (09:00 - 13:00)",
        PostalCode: "42800",
        State: "Hidalgo",
        Municipality: "Tula de Allende",
        City: "Tula de Allende",
        Neighborhood: "Centro",
        Street: "Av. Hidalgo",
        ExteriorNumber: "45",
        InteriorNumber: "2B",
        AddressReference: "Frente al jardín principal",
        ImageUrls: imageUrls ?? ["https://i.ibb.co/1/default-repair.jpg"]);

    private sealed class FakeClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed record AuditEntry(string Action, string EntityName, AuditOutcome Outcome, object? Metadata);

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task LogAsync(string action, string entityName, string? entityId, AuditOutcome outcome, Guid? actorUserId = null, string? actorRole = null, object? metadata = null, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default)
        {
            Entries.Add(new AuditEntry(action, entityName, outcome, metadata));
            return Task.CompletedTask;
        }
    }

    private sealed class SingleUserAuthRepo(User user) : IAuthRepository
    {
        public Task<User?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => Task.FromResult<User?>(user.NormalizedEmail == normalizedEmail ? user : null);
        public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<User?>(user.Id == userId ? user : null);
        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddUserAsync(User u, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<VerificationCode?> GetLatestVerificationCodeAsync(Guid userId, VerificationPurpose purpose, CancellationToken cancellationToken = default) => Task.FromResult<VerificationCode?>(null);
        public Task<VerificationCode?> GetActiveLockoutAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult<VerificationCode?>(null);
        public Task<int> CountRecentVerificationCodesAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task AddVerificationCodeAsync(VerificationCode code, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePendingCodesAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default) => Task.FromResult<RefreshToken?>(null);
        public Task RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<User>> GetUsersPendingPurgeAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryPostalCodeRepo(IReadOnlyList<PostalCodeEntry> entries) : IPostalCodeRepository
    {
        public Task<IReadOnlyList<PostalCodeEntry>> FindByPostalCodeAsync(string postalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PostalCodeEntry>>(entries.Where(e => e.PostalCode == postalCode).ToList());
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
        public Task ReplaceImagesAsync(Guid repairRequestId, IReadOnlyList<RepairRequestImage> newImages, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            var r = Items.FirstOrDefault(x => x.Id == repairRequestId);
            if (r is not null) { r.Images = newImages.ToList(); r.UpdatedAt = utcNow; }
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemorySparePartRepo : ISparePartRepository
    {
        public List<SparePart> Items { get; } = [];
        public Task AddAsync(SparePart sparePart, CancellationToken cancellationToken = default) { Items.Add(sparePart); return Task.CompletedTask; }
        public Task<SparePart?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<SparePart>> GetAvailableAsync(string? search, string? category, SparePartModality? modality, SparePartCondition? condition = null, decimal? minPriceMxn = null, decimal? maxPriceMxn = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SparePart>>(Items.Where(p => p.IsAvailable && p.DeletedAt == null).ToList());
        public Task<IReadOnlyList<SparePart>> GetByTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SparePart>>(Items.Where(p => p.TechnicianUserId == technicianUserId).ToList());
        public Task ReplaceImagesAsync(Guid sparePartId, IReadOnlyList<SparePartImage> newImages, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            var p = Items.FirstOrDefault(x => x.Id == sparePartId);
            if (p is not null) { p.Images = newImages.ToList(); p.UpdatedAt = utcNow; }
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryChatRepo(InMemoryRepairRepo? repairsRepo = null, InMemorySparePartRepo? spareRepo = null) : IChatRepository
    {
        public List<ChatConversation> Conversations { get; } = [];
        public List<ChatMessage> Messages { get; } = [];
        public Task<ChatConversation?> GetConversationByIdAsync(Guid conversationId, CancellationToken cancellationToken = default)
        {
            var conv = Conversations.FirstOrDefault(c => c.Id == conversationId);
            if (conv is not null)
            {
                if (conv.RepairRequestId.HasValue && repairsRepo is not null)
                {
                    conv.RepairRequest = repairsRepo.Items.FirstOrDefault(r => r.Id == conv.RepairRequestId.Value);
                }
                if (conv.SparePartId.HasValue && spareRepo is not null)
                {
                    conv.SparePart = spareRepo.Items.FirstOrDefault(p => p.Id == conv.SparePartId.Value);
                }
            }
            return Task.FromResult(conv);
        }
        public Task<ChatConversation?> GetByRepairRequestIdAsync(Guid repairRequestId, CancellationToken cancellationToken = default) => Task.FromResult(Conversations.FirstOrDefault(c => c.RepairRequestId == repairRequestId));
        public Task<ChatConversation?> GetBySparePartAndTechnicianAsync(Guid sparePartId, Guid interestedTechnicianId, CancellationToken cancellationToken = default) => Task.FromResult(Conversations.FirstOrDefault(c => c.SparePartId == sparePartId && c.ParticipantBUserId == interestedTechnicianId));
        public Task<IReadOnlyList<ChatConversation>> GetUserConversationsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ChatConversation>>(Conversations.Where(c => c.IsParticipant(userId)).ToList());
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, DateTimeOffset? afterUtc, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.ConversationId == conversationId && (!afterUtc.HasValue || m.SentAt > afterUtc.Value)).OrderBy(m => m.SentAt).ToList());
        public Task<int> MarkIncomingMessagesAsReadAsync(Guid conversationId, Guid readerUserId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            var list = Messages.Where(m => m.ConversationId == conversationId && m.SenderUserId != readerUserId && m.ReadAt == null).ToList();
            foreach (var m in list) m.ReadAt = utcNow;
            return Task.FromResult(list.Count);
        }
        public Task<int> GetUnreadCountForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<Dictionary<Guid, int>> GetUnreadCountsByConversationAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(new Dictionary<Guid, int>());
        public Task<Dictionary<Guid, ChatMessage>> GetLatestMessagesByConversationAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken = default) => Task.FromResult(new Dictionary<Guid, ChatMessage>());
        public Task AddConversationAsync(ChatConversation conversation, CancellationToken cancellationToken = default) { Conversations.Add(conversation); return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
