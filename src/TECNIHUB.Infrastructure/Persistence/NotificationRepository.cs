using Microsoft.EntityFrameworkCore;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly TecniHubDbContext _dbContext;

    public NotificationRepository(TecniHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddNotificationAsync(UserNotification notification, CancellationToken cancellationToken = default) =>
        await _dbContext.UserNotifications.AddAsync(notification, cancellationToken);

    public async Task<IReadOnlyList<UserNotification>> GetRecentForUserAsync(
        Guid userId,
        int take = 40,
        CancellationToken cancellationToken = default) =>
        await _dbContext.UserNotifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.UserNotifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

    public async Task<bool> MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
        if (item is null || item.IsRead)
        {
            return false;
        }

        item.IsRead = true;
        item.ReadAt = utcNow;
        return true;
    }

    public async Task<int> MarkAllAsReadAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var unread = await _dbContext.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var item in unread)
        {
            item.IsRead = true;
            item.ReadAt = utcNow;
        }

        return unread.Count;
    }

    public Task<PushSubscriptionRecord?> GetPushSubscriptionByEndpointAsync(
        string endpoint,
        CancellationToken cancellationToken = default) =>
        _dbContext.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

    public async Task<IReadOnlyList<PushSubscriptionRecord>> GetPushSubscriptionsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

    public async Task AddPushSubscriptionAsync(PushSubscriptionRecord subscription, CancellationToken cancellationToken = default) =>
        await _dbContext.PushSubscriptions.AddAsync(subscription, cancellationToken);

    public Task RemovePushSubscriptionAsync(PushSubscriptionRecord subscription, CancellationToken cancellationToken = default)
    {
        _dbContext.PushSubscriptions.Remove(subscription);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
