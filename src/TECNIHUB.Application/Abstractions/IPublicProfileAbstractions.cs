using TECNIHUB.Domain.Entities;

namespace TECNIHUB.Application.Abstractions;

public interface IServiceReviewRepository
{
    Task<User?> GetActiveUserWithProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> CountCompletedServicesByTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default);
    Task<int> CountCompletedServicesByClientAsync(Guid clientUserId, CancellationToken cancellationToken = default);
    Task<bool> HasTechnicianWorkedWithClientAsync(Guid technicianUserId, Guid clientUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceReview>> GetReviewsForTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default);
    Task<(int Count, decimal? AverageRating)> GetReviewSummaryForTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default);
    Task<ServiceReview?> GetByRepairRequestIdAsync(Guid repairRequestId, CancellationToken cancellationToken = default);
    Task<bool> TryAddReviewAsync(ServiceReview review, CancellationToken cancellationToken = default);
}
