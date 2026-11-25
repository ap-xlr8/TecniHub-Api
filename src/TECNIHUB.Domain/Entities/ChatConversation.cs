using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Domain.Entities;

public sealed class ChatConversation
{
    public Guid Id { get; set; }
    public ConversationContextType ContextType { get; set; }

    public Guid? RepairRequestId { get; set; }
    public RepairRequest? RepairRequest { get; set; }

    public Guid? SparePartId { get; set; }
    public SparePart? SparePart { get; set; }

    public Guid ParticipantAUserId { get; set; }
    public User? ParticipantAUser { get; set; }

    public Guid ParticipantBUserId { get; set; }
    public User? ParticipantBUser { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = [];

    public bool IsParticipant(Guid userId) =>
        ParticipantAUserId == userId || ParticipantBUserId == userId;

    public Guid GetCounterpartUserId(Guid currentUserId) =>
        ParticipantAUserId == currentUserId ? ParticipantBUserId : ParticipantAUserId;
}
