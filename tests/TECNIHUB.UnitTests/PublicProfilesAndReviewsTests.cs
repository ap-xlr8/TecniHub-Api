using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.Reviews;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.UnitTests;

public sealed class PublicProfilesAndReviewsTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 25, 18, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLogger _audit = new();
    private readonly RequestMetadata _meta = new("127.0.0.1", "UnitTests/3.0");

    [Fact]
    public async Task PublicTechnicianProfile_returns_null_average_when_no_reviews_and_real_average_when_reviewed()
    {
        var tech = new User
        {
            Id = Guid.NewGuid(),
            RoleId = (int)UserRole.Technician,
            FirstName = "Diego",
            LastName = "Nava",
            Email = "diego@example.com",
            NormalizedEmail = "DIEGO@EXAMPLE.COM",
            Phone = "7730001122",
            Status = AccountStatus.Active,
            CreatedAt = _clock.UtcNow.AddMonths(-2),
            TechnicianProfile = new TechnicianProfile
            {
                Specialties = "Estufas, Microondas",
                ExperienceYears = 9,
                ServiceZone = "Tepeji y Tula",
                BusinessName = "Servicio Nava"
            }
        };
        tech.TechnicianProfile.UserId = tech.Id;

        var client = new User
        {
            Id = Guid.NewGuid(),
            RoleId = (int)UserRole.Client,
            FirstName = "Laura",
            LastName = "Ponce",
            Status = AccountStatus.Active
        };

        var reviewRepo = new InMemoryReviewRepo([tech, client]);
        var repairRepo = new SingleRepairRepo();
        var sut = new PublicProfileService(reviewRepo, repairRepo, _audit, _clock);

        // No reviews -> AverageRating MUST be null (never fabricate 0.0 or 5.0)
        var initial = await sut.GetPublicTechnicianProfileAsync(tech.Id);
        Assert.True(initial.IsSuccess);
        Assert.Equal(0, initial.Value!.ReviewCount);
        Assert.Null(initial.Value.AverageRating);

        // Add completed repair and review
        var repair = new RepairRequest
        {
            Id = Guid.NewGuid(),
            ClientUserId = client.Id,
            ClientUser = client,
            AssignedTechnicianId = tech.Id,
            ApplianceName = "Estufa",
            Status = RepairRequestStatus.Completed
        };
        repairRepo.Items.Add(repair);
        reviewRepo.CompletedByTech = 1;

        var created = await sut.CreateReviewAsync(repair.Id, client.Id, new CreateServiceReviewInput(5, "Excelente trabajo y puntualidad."), _meta);
        Assert.True(created.IsSuccess);
        Assert.Contains(_audit.Entries, e => e.Action == "ServiceReviewCreated");

        // Duplicate review rejected
        var dup = await sut.CreateReviewAsync(repair.Id, client.Id, new CreateServiceReviewInput(4, "Otro"), _meta);
        Assert.False(dup.IsSuccess);
        Assert.Equal(ErrorType.Conflict, dup.Error!.Type);

        var updated = await sut.GetPublicTechnicianProfileAsync(tech.Id);
        Assert.True(updated.IsSuccess);
        Assert.Equal(1, updated.Value!.ReviewCount);
        Assert.Equal(5.0m, updated.Value.AverageRating);
    }

    [Fact]
    public async Task CreateReview_rejects_uncompleted_repair_invalid_rating_and_non_owner()
    {
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var techId = Guid.NewGuid();
        var reviewRepo = new InMemoryReviewRepo([]);
        var repairRepo = new SingleRepairRepo();
        var repair = new RepairRequest
        {
            Id = Guid.NewGuid(),
            ClientUserId = clientId,
            AssignedTechnicianId = techId,
            ApplianceName = "Lavadora",
            Status = RepairRequestStatus.InProgress
        };
        repairRepo.Items.Add(repair);
        var sut = new PublicProfileService(reviewRepo, repairRepo, _audit, _clock);

        var badRating = await sut.CreateReviewAsync(repair.Id, clientId, new CreateServiceReviewInput(6, null), _meta);
        Assert.False(badRating.IsSuccess);
        Assert.Equal(ErrorType.Validation, badRating.Error!.Type);

        var notCompleted = await sut.CreateReviewAsync(repair.Id, clientId, new CreateServiceReviewInput(5, null), _meta);
        Assert.False(notCompleted.IsSuccess);
        Assert.Equal(ErrorType.Conflict, notCompleted.Error!.Type);

        repair.Status = RepairRequestStatus.Completed;
        var wrongClient = await sut.CreateReviewAsync(repair.Id, otherClientId, new CreateServiceReviewInput(5, null), _meta);
        Assert.False(wrongClient.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, wrongClient.Error!.Type);
    }

    [Fact]
    public async Task PublicClientProfile_blocks_unauthorized_accounts_and_exposes_only_approved_fields()
    {
        var client = new User
        {
            Id = Guid.NewGuid(),
            RoleId = (int)UserRole.Client,
            FirstName = "Paula",
            LastName = "Díaz",
            Email = "paula.privado@example.com",
            Phone = "7739998877",
            PublicRegion = null,
            Status = AccountStatus.Active,
            CreatedAt = _clock.UtcNow.AddDays(-10)
        };
        var assignedTechId = Guid.NewGuid();
        var unrelatedTechId = Guid.NewGuid();

        var reviewRepo = new InMemoryReviewRepo([client])
        {
            AuthorizedPairs = [(assignedTechId, client.Id)],
            CompletedByClient = 2
        };
        var sut = new PublicProfileService(reviewRepo, new SingleRepairRepo(), _audit, _clock);

        // Unrelated technician -> Forbidden
        var deniedTech = await sut.GetPublicClientProfileAsync(client.Id, unrelatedTechId, UserRole.Technician, _meta);
        Assert.False(deniedTech.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, deniedTech.Error!.Type);

        // Another client -> Forbidden
        var deniedClient = await sut.GetPublicClientProfileAsync(client.Id, Guid.NewGuid(), UserRole.Client, _meta);
        Assert.False(deniedClient.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, deniedClient.Error!.Type);

        // Assigned technician -> Allowed, limited fields only
        var allowed = await sut.GetPublicClientProfileAsync(client.Id, assignedTechId, UserRole.Technician, _meta);
        Assert.True(allowed.IsSuccess);
        Assert.Equal("Paula Díaz", allowed.Value!.DisplayName);
        Assert.Null(allowed.Value.PublicRegion);
        Assert.Equal(2, allowed.Value.CompletedServicesCount);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed record AuditEntry(string Action);

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task LogAsync(string action, string entityName, string? entityId, AuditOutcome outcome, Guid? actorUserId = null, string? actorRole = null, object? metadata = null, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default)
        {
            Entries.Add(new AuditEntry(action));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryReviewRepo(IEnumerable<User> users) : IServiceReviewRepository
    {
        private readonly List<User> _users = users.ToList();
        public List<ServiceReview> Reviews { get; } = [];
        public List<(Guid TechId, Guid ClientId)> AuthorizedPairs { get; set; } = [];
        public int CompletedByTech { get; set; }
        public int CompletedByClient { get; set; }

        public Task<User?> GetActiveUserWithProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.FirstOrDefault(u => u.Id == userId && u.Status == AccountStatus.Active));
        public Task<int> CountCompletedServicesByTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CompletedByTech);
        public Task<int> CountCompletedServicesByClientAsync(Guid clientUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CompletedByClient);
        public Task<bool> HasTechnicianWorkedWithClientAsync(Guid technicianUserId, Guid clientUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthorizedPairs.Contains((technicianUserId, clientUserId)));
        public Task<IReadOnlyList<ServiceReview>> GetReviewsForTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceReview>>(Reviews.Where(r => r.TechnicianUserId == technicianUserId).ToList());
        public Task<(int Count, decimal? AverageRating)> GetReviewSummaryForTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default)
        {
            var items = Reviews.Where(r => r.TechnicianUserId == technicianUserId).ToList();
            if (items.Count == 0)
            {
                return Task.FromResult<(int, decimal?)>((0, null));
            }

            var avg = Math.Round((decimal)items.Average(r => r.Rating), 1, MidpointRounding.AwayFromZero);
            return Task.FromResult<(int, decimal?)>((items.Count, avg));
        }
        public Task<ServiceReview?> GetByRepairRequestIdAsync(Guid repairRequestId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Reviews.FirstOrDefault(r => r.RepairRequestId == repairRequestId));
        public Task<bool> TryAddReviewAsync(ServiceReview review, CancellationToken cancellationToken = default)
        {
            if (Reviews.Any(r => r.RepairRequestId == review.RepairRequestId))
            {
                return Task.FromResult(false);
            }

            Reviews.Add(review);
            return Task.FromResult(true);
        }
    }

    private sealed class SingleRepairRepo : IRepairRequestRepository
    {
        public List<RepairRequest> Items { get; } = [];
        public Task AddAsync(RepairRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RepairRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<RepairRequest>> GetByClientAsync(Guid clientUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RepairRequest>>([]);
        public Task<IReadOnlyList<RepairRequest>> GetOpenBoardAsync(string? search, string? state = null, string? municipality = null, string? postalCode = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RepairRequest>>([]);
        public Task<IReadOnlyList<RepairRequest>> GetByAssignedTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RepairRequest>>([]);
        public Task<bool> TryAcceptAtomicAsync(Guid repairRequestId, Guid technicianUserId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ReplaceImagesAsync(Guid repairRequestId, IReadOnlyList<RepairRequestImage> newImages, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
