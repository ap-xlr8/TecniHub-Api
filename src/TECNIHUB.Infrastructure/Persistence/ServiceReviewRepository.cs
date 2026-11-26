using Microsoft.EntityFrameworkCore;
using Npgsql;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class ServiceReviewRepository : IServiceReviewRepository
{
    private readonly TecniHubDbContext _dbContext;

    public ServiceReviewRepository(TecniHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> GetActiveUserWithProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Users
            .AsNoTracking()
            .Include(u => u.TechnicianProfile)
            .FirstOrDefaultAsync(u => u.Id == userId && u.Status == AccountStatus.Active, cancellationToken);

    public Task<int> CountCompletedServicesByTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default) =>
        _dbContext.RepairRequests
            .AsNoTracking()
            .CountAsync(
                r => r.AssignedTechnicianId == technicianUserId
                    && r.Status == RepairRequestStatus.Completed
                    && r.DeletedAt == null,
                cancellationToken);

    public Task<int> CountCompletedServicesByClientAsync(Guid clientUserId, CancellationToken cancellationToken = default) =>
        _dbContext.RepairRequests
            .AsNoTracking()
            .CountAsync(
                r => r.ClientUserId == clientUserId
                    && r.Status == RepairRequestStatus.Completed
                    && r.DeletedAt == null,
                cancellationToken);

    public Task<bool> HasTechnicianWorkedWithClientAsync(
        Guid technicianUserId,
        Guid clientUserId,
        CancellationToken cancellationToken = default) =>
        _dbContext.RepairRequests
            .AsNoTracking()
            .AnyAsync(
                r => r.ClientUserId == clientUserId
                    && r.AssignedTechnicianId == technicianUserId
                    && r.DeletedAt == null
                    && (r.Status == RepairRequestStatus.Assigned
                        || r.Status == RepairRequestStatus.Scheduled
                        || r.Status == RepairRequestStatus.OnTheWay
                        || r.Status == RepairRequestStatus.Arrived
                        || r.Status == RepairRequestStatus.InProgress
                        || r.Status == RepairRequestStatus.ClosureRequested
                        || r.Status == RepairRequestStatus.Completed),
                cancellationToken);

    public async Task<IReadOnlyList<ServiceReview>> GetReviewsForTechnicianAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.ServiceReviews
            .AsNoTracking()
            .Include(r => r.ClientUser)
            .Include(r => r.RepairRequest)
            .Where(r => r.TechnicianUserId == technicianUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

    public async Task<(int Count, decimal? AverageRating)> GetReviewSummaryForTechnicianAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _dbContext.ServiceReviews
            .AsNoTracking()
            .Where(r => r.TechnicianUserId == technicianUserId)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync(cancellationToken);

        if (summary is null || summary.Count == 0)
        {
            return (0, null);
        }

        return (summary.Count, Math.Round((decimal)summary.Average, 1, MidpointRounding.AwayFromZero));
    }

    public Task<ServiceReview?> GetByRepairRequestIdAsync(
        Guid repairRequestId,
        CancellationToken cancellationToken = default) =>
        _dbContext.ServiceReviews
            .AsNoTracking()
            .Include(r => r.ClientUser)
            .Include(r => r.RepairRequest)
            .FirstOrDefaultAsync(r => r.RepairRequestId == repairRequestId, cancellationToken);

    public async Task<bool> TryAddReviewAsync(ServiceReview review, CancellationToken cancellationToken = default)
    {
        await _dbContext.ServiceReviews.AddAsync(review, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _dbContext.Entry(review).State = EntityState.Detached;
            return false;
        }
    }
}
