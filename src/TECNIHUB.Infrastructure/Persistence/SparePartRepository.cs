using Microsoft.EntityFrameworkCore;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class SparePartRepository : ISparePartRepository
{
    private readonly TecniHubDbContext _dbContext;

    public SparePartRepository(TecniHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(SparePart sparePart, CancellationToken cancellationToken = default) =>
        await _dbContext.SpareParts.AddAsync(sparePart, cancellationToken);

    public Task<SparePart?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.SpareParts
            .Include(p => p.TechnicianUser)
                .ThenInclude(u => u!.TechnicianProfile)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SparePart>> GetAvailableAsync(
        string? search,
        string? category,
        SparePartModality? modality,
        SparePartCondition? condition = null,
        decimal? minPriceMxn = null,
        decimal? maxPriceMxn = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SpareParts
            .AsNoTracking()
            .Include(p => p.TechnicianUser)
                .ThenInclude(u => u!.TechnicianProfile)
            .Include(p => p.Images)
            .Where(p => p.IsAvailable
                && p.DeletedAt == null
                && p.TechnicianUser != null
                && p.TechnicianUser.Status == AccountStatus.Active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Title.ToLower().Contains(term)
                || p.Description.ToLower().Contains(term)
                || p.ApplianceCategory.ToLower().Contains(term)
                || (p.Compatibility != null && p.Compatibility.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim().ToLowerInvariant();
            query = query.Where(p => p.ApplianceCategory.ToLower() == cat);
        }

        if (modality.HasValue)
        {
            query = query.Where(p => p.Modality == modality.Value);
        }

        if (condition.HasValue)
        {
            query = query.Where(p => p.Condition == condition.Value);
        }

        if (minPriceMxn.HasValue)
        {
            query = query.Where(p => p.PriceMxn >= minPriceMxn.Value);
        }

        if (maxPriceMxn.HasValue)
        {
            query = query.Where(p => p.PriceMxn <= maxPriceMxn.Value);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SparePart>> GetByTechnicianAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.SpareParts
            .AsNoTracking()
            .Include(p => p.TechnicianUser)
                .ThenInclude(u => u!.TechnicianProfile)
            .Include(p => p.Images)
            .Where(p => p.TechnicianUserId == technicianUserId && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task ReplaceImagesAsync(
        Guid sparePartId,
        IReadOnlyList<SparePartImage> newImages,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.SparePartImages
            .Where(i => i.SparePartId == sparePartId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _dbContext.SparePartImages.RemoveRange(existing);
        }

        if (newImages.Count > 0)
        {
            await _dbContext.SparePartImages.AddRangeAsync(newImages, cancellationToken);
        }

        var parent = await _dbContext.SpareParts.FindAsync([sparePartId], cancellationToken);
        if (parent is not null)
        {
            parent.UpdatedAt = utcNow;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
