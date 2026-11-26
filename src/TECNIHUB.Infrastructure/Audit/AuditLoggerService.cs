using System.Text.Json;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Infrastructure.Persistence;

namespace TECNIHUB.Infrastructure.Audit;

public sealed class AuditLoggerService : IAuditLogger
{
    private readonly TecniHubDbContext _dbContext;
    private readonly IDateTimeProvider _clock;

    public AuditLoggerService(TecniHubDbContext dbContext, IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task LogAsync(
        string action,
        string entityName,
        string? entityId,
        AuditOutcome outcome,
        Guid? actorUserId = null,
        string? actorRole = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        string? serializedMetadata = null;
        if (metadata is not null)
        {
            serializedMetadata = Truncate(JsonSerializer.Serialize(metadata), 2000);
        }

        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAt = _clock.UtcNow,
            ActorUserId = actorUserId,
            ActorRole = Truncate(actorRole, 32),
            Action = Truncate(action, 100) ?? "Unknown",
            EntityName = Truncate(entityName, 80) ?? "Unknown",
            EntityId = Truncate(entityId, 100),
            Outcome = outcome,
            MetadataJson = serializedMetadata,
            IpAddress = Truncate(ipAddress, 64),
            UserAgent = Truncate(userAgent, 300)
        };

        await _dbContext.AuditLogs.AddAsync(entry, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
