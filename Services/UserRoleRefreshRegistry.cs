using System.Collections.Concurrent;

namespace MiniFinance.Services;

/// <summary>
/// Реестр активных Blazor-сессий для мгновенного обновления роли без повторного входа.
/// </summary>
public interface IUserRoleRefreshRegistry
{
    Guid Register(string userId, Func<Task> refresh);
    void Unregister(string userId, Guid registrationId);
    Task NotifyRoleChangedAsync(string userId);
}

public sealed class UserRoleRefreshRegistry : IUserRoleRefreshRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<Task>>> _refreshers = new();

    public Guid Register(string userId, Func<Task> refresh)
    {
        var bucket = _refreshers.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Func<Task>>());
        var id = Guid.NewGuid();
        bucket[id] = refresh;
        return id;
    }

    public void Unregister(string userId, Guid registrationId)
    {
        if (_refreshers.TryGetValue(userId, out var bucket))
        {
            bucket.TryRemove(registrationId, out _);
            if (bucket.IsEmpty)
                _refreshers.TryRemove(userId, out _);
        }
    }

    public async Task NotifyRoleChangedAsync(string userId)
    {
        if (!_refreshers.TryGetValue(userId, out var bucket))
            return;

        foreach (var refresh in bucket.Values.ToList())
        {
            try
            {
                await refresh();
            }
            catch
            {
                // Сессия могла уже завершиться — игнорируем.
            }
        }
    }
}
