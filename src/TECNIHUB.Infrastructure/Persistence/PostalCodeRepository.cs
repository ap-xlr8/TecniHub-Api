using Microsoft.EntityFrameworkCore;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class PostalCodeRepository : IPostalCodeRepository
{
    private readonly TecniHubDbContext _dbContext;

    public PostalCodeRepository(TecniHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PostalCodeEntry>> FindByPostalCodeAsync(
        string postalCode,
        CancellationToken cancellationToken = default) =>
        await _dbContext.PostalCodes
            .AsNoTracking()
            .Where(pc => pc.PostalCode == postalCode)
            .OrderBy(pc => pc.Neighborhood)
            .ToListAsync(cancellationToken);
}
