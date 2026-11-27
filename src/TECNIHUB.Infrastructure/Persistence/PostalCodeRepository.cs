using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class PostalCodeRepository : IPostalCodeRepository
{
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromHours(6);
    private static readonly TimeSpan CacheAbsoluteExpiration = TimeSpan.FromHours(24);

    private readonly TecniHubDbContext _dbContext;
    private readonly IMemoryCache? _cache;

    public PostalCodeRepository(TecniHubDbContext dbContext, IMemoryCache? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<IReadOnlyList<PostalCodeEntry>> FindByPostalCodeAsync(
        string postalCode,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"sepomex:cp:{postalCode}";
        if (_cache is not null && _cache.TryGetValue(cacheKey, out IReadOnlyList<PostalCodeEntry>? cached) && cached is not null)
        {
            return cached;
        }

        var entries = await _dbContext.PostalCodes
            .AsNoTracking()
            .Where(pc => pc.PostalCode == postalCode)
            .OrderBy(pc => pc.Neighborhood)
            .ToListAsync(cancellationToken);

        if (_cache is not null && entries.Count > 0)
        {
            _cache.Set<IReadOnlyList<PostalCodeEntry>>(
                cacheKey,
                entries,
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = CacheSlidingExpiration,
                    AbsoluteExpirationRelativeToNow = CacheAbsoluteExpiration
                });
        }

        return entries;
    }
}
