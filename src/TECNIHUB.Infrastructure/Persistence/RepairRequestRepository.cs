using Microsoft.EntityFrameworkCore;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class RepairRequestRepository : IRepairRequestRepository
{
    private readonly TecniHubDbContext _dbContext;

    public RepairRequestRepository(TecniHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(RepairRequest request, CancellationToken cancellationToken = default) =>
        await _dbContext.RepairRequests.AddAsync(request, cancellationToken);

    public Task<RepairRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.RepairRequests
            .Include(r => r.ClientUser)
            .Include(r => r.AssignedTechnician)
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RepairRequest>> GetByClientAsync(
        Guid clientUserId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.RepairRequests
            .AsNoTracking()
            .Include(r => r.AssignedTechnician)
            .Include(r => r.Images)
            .Where(r => r.ClientUserId == clientUserId && r.DeletedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RepairRequest>> GetOpenBoardAsync(
        string? search,
        string? state = null,
        string? municipality = null,
        string? postalCode = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.RepairRequests
            .AsNoTracking()
            .Include(r => r.ClientUser)
            .Include(r => r.Images)
            .Where(r => r.Status == RepairRequestStatus.Open
                && r.DeletedAt == null
                && r.ClientUser != null
                && r.ClientUser.Status == AccountStatus.Active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(r =>
                r.ApplianceName.ToLower().Contains(term)
                || (r.BrandModel != null && r.BrandModel.ToLower().Contains(term))
                || r.IssueDescription.ToLower().Contains(term)
                || r.Neighborhood.ToLower().Contains(term)
                || r.Municipality.ToLower().Contains(term)
                || r.PostalCode.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            var st = state.Trim().ToLowerInvariant();
            query = query.Where(r => r.State.ToLower().Contains(st));
        }

        if (!string.IsNullOrWhiteSpace(municipality))
        {
            var mun = municipality.Trim().ToLowerInvariant();
            query = query.Where(r => r.Municipality.ToLower().Contains(mun));
        }

        if (!string.IsNullOrWhiteSpace(postalCode))
        {
            var cp = postalCode.Trim();
            query = query.Where(r => r.PostalCode == cp);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RepairRequest>> GetByAssignedTechnicianAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.RepairRequests
            .AsNoTracking()
            .Include(r => r.ClientUser)
            .Include(r => r.Images)
            .Where(r => r.AssignedTechnicianId == technicianUserId && r.DeletedAt == null)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryAcceptAtomicAsync(
        Guid repairRequestId,
        Guid technicianUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            var rows = await _dbContext.RepairRequests
                .Where(r => r.Id == repairRequestId
                    && r.Status == RepairRequestStatus.Open
                    && r.AssignedTechnicianId == null
                    && r.DeletedAt == null
                    && r.ClientUser != null
                    && r.ClientUser.Status == AccountStatus.Active)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(r => r.AssignedTechnicianId, technicianUserId)
                        .SetProperty(r => r.AssignedAt, utcNow)
                        .SetProperty(r => r.Status, RepairRequestStatus.Assigned)
                        .SetProperty(r => r.UpdatedAt, utcNow),
                    cancellationToken);

            return rows == 1;
        }

        var entity = await _dbContext.RepairRequests
            .Include(r => r.ClientUser)
            .FirstOrDefaultAsync(r => r.Id == repairRequestId && r.DeletedAt == null, cancellationToken);

        if (entity is null || entity.ClientUser?.Status != AccountStatus.Active)
        {
            return false;
        }

        if (!entity.Accept(technicianUserId, utcNow))
        {
            return false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryCancelAtomicAsync(
        Guid repairRequestId,
        Guid clientUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            var rows = await _dbContext.RepairRequests
                .Where(r => r.Id == repairRequestId
                    && r.ClientUserId == clientUserId
                    && r.Status == RepairRequestStatus.Open
                    && r.AssignedTechnicianId == null
                    && r.DeletedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(r => r.Status, RepairRequestStatus.Cancelled)
                        .SetProperty(r => r.CancelledAt, utcNow)
                        .SetProperty(r => r.UpdatedAt, utcNow),
                    cancellationToken);

            return rows == 1;
        }

        var entity = await _dbContext.RepairRequests
            .FirstOrDefaultAsync(r => r.Id == repairRequestId && r.ClientUserId == clientUserId && r.DeletedAt == null, cancellationToken);

        if (entity is null || entity.AssignedTechnicianId.HasValue || !entity.Cancel(utcNow))
        {
            return false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReplaceImagesAsync(
        Guid repairRequestId,
        IReadOnlyList<RepairRequestImage> newImages,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.RepairRequestImages
            .Where(i => i.RepairRequestId == repairRequestId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _dbContext.RepairRequestImages.RemoveRange(existing);
        }

        if (newImages.Count > 0)
        {
            await _dbContext.RepairRequestImages.AddRangeAsync(newImages, cancellationToken);
        }

        var parent = await _dbContext.RepairRequests.FindAsync([repairRequestId], cancellationToken);
        if (parent is not null)
        {
            parent.UpdatedAt = utcNow;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
