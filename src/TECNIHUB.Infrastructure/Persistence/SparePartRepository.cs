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

    public async Task<(IReadOnlyList<SparePart> Items, int TotalCount)> GetAvailablePageAsync(
        SparePartBoardFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SpareParts
            .AsNoTracking()
            .Where(p => p.IsAvailable
                && p.DeletedAt == null
                && p.TechnicianUser != null
                && p.TechnicianUser.Status == AccountStatus.Active);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Title.ToLower().Contains(term)
                || p.Description.ToLower().Contains(term)
                || p.ApplianceCategory.ToLower().Contains(term)
                || (p.Compatibility != null && p.Compatibility.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var cat = filter.Category.Trim().ToLowerInvariant();
            query = query.Where(p => p.ApplianceCategory.ToLower() == cat);
        }

        if (filter.Modality.HasValue)
        {
            query = query.Where(p => p.Modality == filter.Modality.Value);
        }

        if (filter.Condition.HasValue)
        {
            query = query.Where(p => p.Condition == filter.Condition.Value);
        }

        if (filter.MinPriceMxn.HasValue)
        {
            query = query.Where(p => p.PriceMxn >= filter.MinPriceMxn.Value);
        }

        if (filter.MaxPriceMxn.HasValue)
        {
            query = query.Where(p => p.PriceMxn <= filter.MaxPriceMxn.Value);
        }

        return await ExecutePagedSparePartsAsync(query, filter.Page, filter.PageSize, cancellationToken);
    }

    public Task<(IReadOnlyList<SparePart> Items, int TotalCount)> GetByTechnicianPageAsync(
        Guid technicianUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SpareParts
            .AsNoTracking()
            .Where(p => p.TechnicianUserId == technicianUserId && p.DeletedAt == null);

        return ExecutePagedSparePartsAsync(query, page, pageSize, cancellationToken);
    }

    private async Task<(IReadOnlyList<SparePart> Items, int TotalCount)> ExecutePagedSparePartsAsync(
        IQueryable<SparePart> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var effectivePage = Math.Min(Math.Max(page, 1), totalPages);

        var itemsQuery = query
            .Include(p => p.TechnicianUser)
                .ThenInclude(u => u!.TechnicianProfile)
            .Include(p => p.Images)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((effectivePage - 1) * pageSize)
            .Take(pageSize);

        if (_dbContext.Database.IsRelational())
        {
            itemsQuery = itemsQuery.AsSplitQuery();
        }

        var items = await itemsQuery.ToListAsync(cancellationToken);
        return (items, totalCount);
    }

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
