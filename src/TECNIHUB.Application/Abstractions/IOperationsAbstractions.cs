using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Application.Abstractions;

public interface IPostalCodeRepository
{
    Task<IReadOnlyList<PostalCodeEntry>> FindByPostalCodeAsync(
        string postalCode,
        CancellationToken cancellationToken = default);
}

public interface IImageStorageService
{
    Task<Result<string>> UploadImageAsync(
        Stream content,
        long contentLength,
        Guid actorUserId,
        string actorRole,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed record OpenRepairBoardFilter(
    string? Search = null,
    string? State = null,
    string? Municipality = null,
    string? PostalCode = null,
    int Page = 1,
    int PageSize = PagedResult<object>.DefaultBoardPageSize);

public sealed record SparePartBoardFilter(
    string? Search = null,
    string? Category = null,
    SparePartModality? Modality = null,
    SparePartCondition? Condition = null,
    decimal? MinPriceMxn = null,
    decimal? MaxPriceMxn = null,
    int Page = 1,
    int PageSize = PagedResult<object>.DefaultSparePartsPageSize);

public interface IRepairRequestRepository
{
    Task AddAsync(RepairRequest request, CancellationToken cancellationToken = default);
    Task<RepairRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepairRequest>> GetByClientAsync(Guid clientUserId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<RepairRequest> Items, int TotalCount)> GetOpenBoardPageAsync(
        OpenRepairBoardFilter filter,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepairRequest>> GetByAssignedTechnicianAsync(Guid technicianUserId, CancellationToken cancellationToken = default);
    Task<bool> TryAcceptAtomicAsync(Guid repairRequestId, Guid technicianUserId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<bool> TryCancelAtomicAsync(Guid repairRequestId, Guid clientUserId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task ReplaceImagesAsync(Guid repairRequestId, IReadOnlyList<RepairRequestImage> newImages, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISparePartRepository
{
    Task AddAsync(SparePart sparePart, CancellationToken cancellationToken = default);
    Task<SparePart?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<SparePart> Items, int TotalCount)> GetAvailablePageAsync(
        SparePartBoardFilter filter,
        CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<SparePart> Items, int TotalCount)> GetByTechnicianPageAsync(
        Guid technicianUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task ReplaceImagesAsync(Guid sparePartId, IReadOnlyList<SparePartImage> newImages, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IChatRepository
{
    Task<ChatConversation?> GetConversationByIdAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<ChatConversation?> GetByRepairRequestIdAsync(Guid repairRequestId, CancellationToken cancellationToken = default);
    Task<ChatConversation?> GetByRepairRequestAndTechnicianAsync(Guid repairRequestId, Guid technicianUserId, CancellationToken cancellationToken = default) =>
        GetByRepairRequestIdAsync(repairRequestId, cancellationToken);
    Task<ChatConversation?> GetBySparePartAndTechnicianAsync(Guid sparePartId, Guid interestedTechnicianId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatConversation>> GetUserConversationsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, DateTimeOffset? afterUtc, CancellationToken cancellationToken = default);
    Task<int> MarkIncomingMessagesAsReadAsync(Guid conversationId, Guid readerUserId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, int>> GetUnreadCountsByConversationAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, ChatMessage>> GetLatestMessagesByConversationAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken = default);
    Task AddConversationAsync(ChatConversation conversation, CancellationToken cancellationToken = default);
    Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
