namespace TECNIHUB.Domain.Entities;

public sealed class ChatMessage
{
    public const int MaxBodyLength = 1000;

    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public ChatConversation? Conversation { get; set; }

    public Guid SenderUserId { get; set; }
    public User? SenderUser { get; set; }

    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
