using Microsoft.EntityFrameworkCore;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class ChatRepository : IChatRepository
{
    private readonly TecniHubDbContext _dbContext;

    public ChatRepository(TecniHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ChatConversation?> GetConversationByIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        _dbContext.ChatConversations
            .Include(c => c.ParticipantAUser)
            .Include(c => c.ParticipantBUser)
            .Include(c => c.RepairRequest)
            .Include(c => c.SparePart)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public Task<ChatConversation?> GetByRepairRequestIdAsync(
        Guid repairRequestId,
        CancellationToken cancellationToken = default) =>
        _dbContext.ChatConversations
            .Include(c => c.ParticipantAUser)
            .Include(c => c.ParticipantBUser)
            .Include(c => c.RepairRequest)
            .FirstOrDefaultAsync(c => c.RepairRequestId == repairRequestId, cancellationToken);

    public Task<ChatConversation?> GetBySparePartAndTechnicianAsync(
        Guid sparePartId,
        Guid interestedTechnicianId,
        CancellationToken cancellationToken = default) =>
        _dbContext.ChatConversations
            .Include(c => c.ParticipantAUser)
            .Include(c => c.ParticipantBUser)
            .Include(c => c.SparePart)
            .FirstOrDefaultAsync(
                c => c.SparePartId == sparePartId && c.ParticipantBUserId == interestedTechnicianId,
                cancellationToken);

    public async Task<IReadOnlyList<ChatConversation>> GetUserConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.ChatConversations
            .AsNoTracking()
            .Include(c => c.ParticipantAUser)
            .Include(c => c.ParticipantBUser)
            .Include(c => c.RepairRequest)
            .Include(c => c.SparePart)
            .Where(c => c.ParticipantAUserId == userId || c.ParticipantBUserId == userId)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        DateTimeOffset? afterUtc,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ChatMessages
            .AsNoTracking()
            .Include(m => m.SenderUser)
            .Where(m => m.ConversationId == conversationId);

        if (afterUtc.HasValue)
        {
            query = query.Where(m => m.SentAt > afterUtc.Value);
        }

        return await query
            .OrderBy(m => m.SentAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> MarkIncomingMessagesAsReadAsync(
        Guid conversationId,
        Guid readerUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var unread = await _dbContext.ChatMessages
            .Where(m => m.ConversationId == conversationId
                && m.SenderUserId != readerUserId
                && m.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var msg in unread)
        {
            msg.ReadAt = utcNow;
        }

        return unread.Count;
    }

    public Task<int> GetUnreadCountForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.ChatMessages
            .AsNoTracking()
            .CountAsync(
                m => m.ReadAt == null
                    && m.SenderUserId != userId
                    && m.Conversation != null
                    && (m.Conversation.ParticipantAUserId == userId || m.Conversation.ParticipantBUserId == userId),
                cancellationToken);

    public async Task<Dictionary<Guid, int>> GetUnreadCountsByConversationAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(m => m.ReadAt == null
                && m.SenderUserId != userId
                && m.Conversation != null
                && (m.Conversation.ParticipantAUserId == userId || m.Conversation.ParticipantBUserId == userId))
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ConversationId, r => r.Count);
    }

    public async Task<Dictionary<Guid, ChatMessage>> GetLatestMessagesByConversationAsync(
        IReadOnlyCollection<Guid> conversationIds,
        CancellationToken cancellationToken = default)
    {
        if (conversationIds.Count == 0)
        {
            return [];
        }

        var latestTimestamps = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, MaxSentAt = g.Max(m => m.SentAt) })
            .ToListAsync(cancellationToken);

        if (latestTimestamps.Count == 0)
        {
            return [];
        }

        var minLatest = latestTimestamps.Min(x => x.MaxSentAt);
        var candidates = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId) && m.SentAt >= minLatest)
            .OrderByDescending(m => m.SentAt)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, ChatMessage>(latestTimestamps.Count);
        foreach (var msg in candidates)
        {
            result.TryAdd(msg.ConversationId, msg);
            if (result.Count == latestTimestamps.Count)
            {
                break;
            }
        }

        return result;
    }

    public async Task AddConversationAsync(ChatConversation conversation, CancellationToken cancellationToken = default) =>
        await _dbContext.ChatConversations.AddAsync(conversation, cancellationToken);

    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) =>
        await _dbContext.ChatMessages.AddAsync(message, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
