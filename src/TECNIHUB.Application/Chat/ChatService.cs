using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.Notifications;
using TECNIHUB.Application.Repairs;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Domain.Rules;

namespace TECNIHUB.Application.Chat;

public sealed class ChatService : IChatService
{
    private readonly IChatRepository _chat;
    private readonly IRepairRequestRepository _repairs;
    private readonly ISparePartRepository _spareParts;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationService? _notifications;
    private readonly IRealtimeNotifier? _realtime;

    public ChatService(
        IChatRepository chat,
        IRepairRequestRepository repairs,
        ISparePartRepository spareParts,
        IAuditLogger auditLogger,
        IDateTimeProvider clock,
        INotificationService? notifications = null,
        IRealtimeNotifier? realtime = null)
    {
        _chat = chat;
        _repairs = repairs;
        _spareParts = spareParts;
        _auditLogger = auditLogger;
        _clock = clock;
        _notifications = notifications;
        _realtime = realtime;
    }

    public async Task<Result<IReadOnlyList<ChatConversationSummaryDto>>> GetMyConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var conversations = await _chat.GetUserConversationsAsync(userId, cancellationToken);
        if (conversations.Count == 0)
        {
            return Result<IReadOnlyList<ChatConversationSummaryDto>>.Success([]);
        }

        var unreadMap = await _chat.GetUnreadCountsByConversationAsync(userId, cancellationToken);
        var latestMap = await _chat.GetLatestMessagesByConversationAsync(
            conversations.Select(c => c.Id).ToArray(),
            cancellationToken);

        var items = conversations
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c =>
            {
                latestMap.TryGetValue(c.Id, out var latest);
                unreadMap.TryGetValue(c.Id, out var unread);
                return MapConversationSummary(c, userId, latest, unread);
            })
            .ToArray();

        return Result<IReadOnlyList<ChatConversationSummaryDto>>.Success(items);
    }

    public async Task<Result<UnreadCountDto>> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var count = await _chat.GetUnreadCountForUserAsync(userId, cancellationToken);
        return Result<UnreadCountDto>.Success(new UnreadCountDto(count));
    }

    public async Task<Result<ChatConversationSummaryDto>> OpenRepairConversationAsync(
        Guid repairRequestId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairRequestId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<ChatConversationSummaryDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (!repair.IsConfirmedAssignment || !repair.AssignedTechnicianId.HasValue)
        {
            return Result<ChatConversationSummaryDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "chat_requires_assignment",
                "El chat se habilita únicamente cuando un técnico ha aceptado la solicitud."));
        }

        if (repair.ClientUserId != actorUserId && repair.AssignedTechnicianId != actorUserId)
        {
            await LogDeniedAccessAsync(actorUserId, "RepairRequest", repairRequestId, metadata, cancellationToken);
            return Result<ChatConversationSummaryDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "chat_access_denied",
                "No participas en esta solicitud de reparación."));
        }

        var existing = await _chat.GetByRepairRequestAndTechnicianAsync(repair.Id, repair.AssignedTechnicianId.Value, cancellationToken);
        if (existing is null)
        {
            var utcNow = _clock.UtcNow;
            existing = new ChatConversation
            {
                Id = Guid.NewGuid(),
                ContextType = ConversationContextType.RepairRequest,
                RepairRequestId = repair.Id,
                ParticipantAUserId = repair.ClientUserId,
                ParticipantBUserId = repair.AssignedTechnicianId.Value,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };
            await _chat.AddConversationAsync(existing, cancellationToken);
            await _chat.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                action: "ChatConversationCreated",
                entityName: "ChatConversation",
                entityId: existing.Id.ToString(),
                outcome: AuditOutcome.Success,
                actorUserId: actorUserId,
                metadata: new { contextType = nameof(ConversationContextType.RepairRequest), repairRequestId = repair.Id },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            existing = await _chat.GetConversationByIdAsync(existing.Id, cancellationToken) ?? existing;
        }

        return Result<ChatConversationSummaryDto>.Success(MapConversationSummary(existing, actorUserId, null, 0));
    }

    public async Task<Result<ChatConversationSummaryDto>> OpenSparePartConversationAsync(
        Guid sparePartId,
        Guid actorTechnicianId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var part = await _spareParts.GetByIdAsync(sparePartId, cancellationToken);
        if (part is null || part.DeletedAt.HasValue)
        {
            return Result<ChatConversationSummaryDto>.Failure(new AppError(
                ErrorType.NotFound,
                "spare_part_not_found",
                "Publicación de refacción no encontrada."));
        }

        if (part.TechnicianUserId == actorTechnicianId)
        {
            return Result<ChatConversationSummaryDto>.Failure(new AppError(
                ErrorType.Validation,
                "cannot_chat_with_self",
                "No puedes iniciar una conversación contigo mismo en tu propia publicación."));
        }

        var existing = await _chat.GetBySparePartAndTechnicianAsync(part.Id, actorTechnicianId, cancellationToken);
        if (existing is null)
        {
            if (!part.IsAvailable)
            {
                return Result<ChatConversationSummaryDto>.Failure(new AppError(
                    ErrorType.Conflict,
                    "spare_part_unavailable",
                    "Esta refacción ya no está disponible para iniciar nuevas conversaciones."));
            }

            var utcNow = _clock.UtcNow;
            existing = new ChatConversation
            {
                Id = Guid.NewGuid(),
                ContextType = ConversationContextType.SparePart,
                SparePartId = part.Id,
                ParticipantAUserId = part.TechnicianUserId,
                ParticipantBUserId = actorTechnicianId,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };
            await _chat.AddConversationAsync(existing, cancellationToken);
            await _chat.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                action: "ChatConversationCreated",
                entityName: "ChatConversation",
                entityId: existing.Id.ToString(),
                outcome: AuditOutcome.Success,
                actorUserId: actorTechnicianId,
                actorRole: nameof(UserRole.Technician),
                metadata: new { contextType = nameof(ConversationContextType.SparePart), sparePartId = part.Id },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            existing = await _chat.GetConversationByIdAsync(existing.Id, cancellationToken) ?? existing;
        }

        return Result<ChatConversationSummaryDto>.Success(MapConversationSummary(existing, actorTechnicianId, null, 0));
    }

    public async Task<Result<ChatThreadDto>> GetMessagesAsync(
        Guid conversationId,
        Guid actorUserId,
        DateTimeOffset? afterUtc,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _chat.GetConversationByIdAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            return Result<ChatThreadDto>.Failure(new AppError(
                ErrorType.NotFound,
                "conversation_not_found",
                "Conversación no encontrada."));
        }

        if (!conversation.IsParticipant(actorUserId))
        {
            await LogDeniedAccessAsync(actorUserId, "ChatConversation", conversationId, metadata, cancellationToken);
            return Result<ChatThreadDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "chat_access_denied",
                "No tienes permiso para acceder a esta conversación."));
        }

        var utcNow = _clock.UtcNow;
        var markedCount = await _chat.MarkIncomingMessagesAsReadAsync(conversation.Id, actorUserId, utcNow, cancellationToken);
        if (markedCount > 0)
        {
            await _chat.SaveChangesAsync(cancellationToken);
        }

        var messages = await _chat.GetMessagesAsync(conversation.Id, afterUtc, cancellationToken);
        var mappedMessages = messages.Select(m => MapMessage(m, actorUserId)).ToArray();

        return Result<ChatThreadDto>.Success(new ChatThreadDto(
            MapConversationSummary(conversation, actorUserId, messages.LastOrDefault(), unreadCount: 0),
            mappedMessages));
    }

    public async Task<Result<ChatMessageDto>> SendMessageAsync(
        Guid conversationId,
        Guid actorUserId,
        SendChatMessageInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _chat.GetConversationByIdAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            return Result<ChatMessageDto>.Failure(new AppError(
                ErrorType.NotFound,
                "conversation_not_found",
                "Conversación no encontrada."));
        }

        if (!conversation.IsParticipant(actorUserId))
        {
            await LogDeniedAccessAsync(actorUserId, "ChatConversation", conversationId, metadata, cancellationToken);
            return Result<ChatMessageDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "chat_access_denied",
                "No tienes permiso para enviar mensajes en esta conversación."));
        }

        if (IsConversationReadOnly(conversation, actorUserId))
        {
            return Result<ChatMessageDto>.Failure(new AppError(
                ErrorType.Conflict,
                "conversation_read_only",
                "Esta conversación se encuentra en modo solo lectura."));
        }

        var body = input.Body?.Trim() ?? string.Empty;
        var imageUrl = string.IsNullOrWhiteSpace(input.ImageUrl) ? null : input.ImageUrl.Trim();
        if ((string.IsNullOrWhiteSpace(body) && imageUrl is null) || body.Length > ChatMessage.MaxBodyLength)
        {
            return Result<ChatMessageDto>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_message_body",
                $"Escribe un mensaje (máximo {ChatMessage.MaxBodyLength} caracteres) o adjunta una fotografía."));
        }

        if (imageUrl is not null && !ImageSignatureValidator.IsAllowedHostedImageUrl(imageUrl))
        {
            return Result<ChatMessageDto>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_message_image",
                "La imagen adjunta debe haberse cargado en el servicio permitido."));
        }

        var utcNow = _clock.UtcNow;
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            SenderUserId = actorUserId,
            Body = body,
            ImageUrl = imageUrl,
            SentAt = utcNow
        };

        conversation.LastMessageAt = utcNow;
        conversation.UpdatedAt = utcNow;

        await _chat.AddMessageAsync(message, cancellationToken);
        await _chat.SaveChangesAsync(cancellationToken);

        // Audit metadata ONLY — never store message body text in audit_logs.
        await _auditLogger.LogAsync(
            action: "ChatMessageSent",
            entityName: "ChatMessage",
            entityId: message.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: actorUserId,
            metadata: new { conversationId = conversation.Id, contextType = conversation.ContextType.ToString(), hasImage = imageUrl is not null },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var senderUser = conversation.ParticipantAUserId == actorUserId
            ? conversation.ParticipantAUser
            : conversation.ParticipantBUser;
        message.SenderUser = senderUser;

        var counterpartId = conversation.GetCounterpartUserId(actorUserId);
        if (_realtime is not null)
        {
            try
            {
                await _realtime.NotifyChatMessageAsync(counterpartId, MapMessage(message, counterpartId), cancellationToken);
            }
            catch
            {
                // Best-effort realtime push.
            }
        }

        if (_notifications is not null)
        {
            var senderLabel = FormatDisplayName(senderUser);
            await _notifications.NotifyAsync(
                counterpartId,
                "ChatMessage",
                $"Nuevo mensaje de {senderLabel}",
                imageUrl is not null && string.IsNullOrWhiteSpace(body)
                    ? "Te envió una fotografía en la conversación."
                    : "Tienes un nuevo mensaje en tu bandeja de TECNIHUB.",
                $"/mensajes/{conversation.Id}",
                cancellationToken);
        }

        return Result<ChatMessageDto>.Success(MapMessage(message, actorUserId));
    }

    private async Task LogDeniedAccessAsync(
        Guid actorUserId,
        string entityName,
        Guid entityId,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        await _auditLogger.LogAsync(
            action: "ChatAccessDenied",
            entityName: entityName,
            entityId: entityId.ToString(),
            outcome: AuditOutcome.Blocked,
            actorUserId: actorUserId,
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);
    }

    private static bool IsConversationReadOnly(ChatConversation conversation, Guid currentUserId)
    {
        var actor = conversation.ParticipantAUserId == currentUserId
            ? conversation.ParticipantAUser
            : conversation.ParticipantBUser;

        if (actor is not null && actor.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            return true;
        }

        var counterpart = conversation.ParticipantAUserId == currentUserId
            ? conversation.ParticipantBUser
            : conversation.ParticipantAUser;

        if (counterpart is not null && counterpart.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            return true;
        }

        if (conversation.ContextType == ConversationContextType.RepairRequest && conversation.RepairRequest is not null)
        {
            if (conversation.RepairRequest.Status is RepairRequestStatus.Open or RepairRequestStatus.Completed or RepairRequestStatus.Cancelled)
            {
                return true;
            }

            if (conversation.RepairRequest.AssignedTechnicianId != conversation.ParticipantBUserId)
            {
                return true;
            }
        }

        return false;
    }

    private static ChatConversationSummaryDto MapConversationSummary(
        ChatConversation conversation,
        Guid currentUserId,
        ChatMessage? latestMessage,
        int unreadCount)
    {
        var counterpartId = conversation.GetCounterpartUserId(currentUserId);
        var counterpartUser = conversation.ParticipantAUserId == currentUserId
            ? conversation.ParticipantBUser
            : conversation.ParticipantAUser;

        var contextId = conversation.ContextType == ConversationContextType.RepairRequest
            ? conversation.RepairRequestId ?? Guid.Empty
            : conversation.SparePartId ?? Guid.Empty;

        var contextTitle = conversation.ContextType == ConversationContextType.RepairRequest
            ? $"Reparación: {conversation.RepairRequest?.ApplianceName ?? "Servicio"}"
            : $"Refacción: {conversation.SparePart?.Title ?? "Publicación"}";

        var contextStatusLabel = conversation.ContextType == ConversationContextType.RepairRequest
            ? (conversation.RepairRequest is not null
                ? RepairRequestService.GetStatusLabel(conversation.RepairRequest.Status)
                : "Asignada")
            : (conversation.SparePart?.IsAvailable == true ? "Disponible" : "No disponible");

        var counterpartRoleLabel = counterpartUser?.GetUserRole() == UserRole.Technician ? "Técnico" : "Cliente";

        var preview = latestMessage is null
            ? null
            : string.IsNullOrWhiteSpace(latestMessage.Body) && !string.IsNullOrWhiteSpace(latestMessage.ImageUrl)
                ? "📷 Fotografía adjunta"
                : Summarize(latestMessage.Body, 80);

        var counterpartAvatar = counterpartUser is null || counterpartUser.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized
            ? null
            : counterpartUser.AvatarUrl;

        return new ChatConversationSummaryDto(
            conversation.Id,
            conversation.ContextType.ToString(),
            contextId,
            contextTitle,
            contextStatusLabel,
            counterpartId,
            FormatDisplayName(counterpartUser),
            counterpartRoleLabel,
            preview,
            conversation.LastMessageAt ?? latestMessage?.SentAt,
            unreadCount,
            IsConversationReadOnly(conversation, currentUserId),
            counterpartAvatar);
    }

    private static ChatMessageDto MapMessage(ChatMessage message, Guid currentUserId) => new(
        message.Id,
        message.ConversationId,
        message.SenderUserId,
        FormatDisplayName(message.SenderUser),
        message.SenderUserId == currentUserId,
        message.Body,
        message.SentAt,
        message.ReadAt,
        message.ImageUrl);

    private static string FormatDisplayName(User? user)
    {
        if (user is null)
        {
            return "Usuario";
        }

        if (user.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            return "Usuario eliminado";
        }

        return $"{user.FirstName} {user.LastName}".Trim();
    }

    private static string Summarize(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..(maxLength - 1)].TrimEnd()}…";
}
