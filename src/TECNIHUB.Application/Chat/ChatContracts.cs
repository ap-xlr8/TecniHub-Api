using TECNIHUB.Application.Common;

namespace TECNIHUB.Application.Chat;

public sealed record ChatConversationSummaryDto(
    Guid Id,
    string ContextType,
    Guid ContextId,
    string ContextTitle,
    string ContextStatusLabel,
    Guid CounterpartUserId,
    string CounterpartDisplayName,
    string CounterpartRoleLabel,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    bool IsReadOnly);

public sealed record ChatMessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string SenderDisplayName,
    bool IsMine,
    string Body,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt);

public sealed record ChatThreadDto(
    ChatConversationSummaryDto Conversation,
    IReadOnlyList<ChatMessageDto> Messages);

public sealed record SendChatMessageInput(string Body);

public sealed record UnreadCountDto(int UnreadCount);

public interface IChatService
{
    Task<Result<IReadOnlyList<ChatConversationSummaryDto>>> GetMyConversationsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<UnreadCountDto>> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<ChatConversationSummaryDto>> OpenRepairConversationAsync(Guid repairRequestId, Guid actorUserId, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<ChatConversationSummaryDto>> OpenSparePartConversationAsync(Guid sparePartId, Guid actorTechnicianId, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<ChatThreadDto>> GetMessagesAsync(Guid conversationId, Guid actorUserId, DateTimeOffset? afterUtc, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<ChatMessageDto>> SendMessageAsync(Guid conversationId, Guid actorUserId, SendChatMessageInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
}
