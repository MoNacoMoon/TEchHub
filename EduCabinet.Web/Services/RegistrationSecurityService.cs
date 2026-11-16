using System.Collections.Concurrent;

namespace EduCabinet.Web.Services;

/// <summary>
/// Потокобезопасная реализация ограничения попыток регистрации по IP.
/// Разрешает максимум 3 попытки регистрации в течение 15 минут.
/// </summary>
public class RegistrationSecurityService : IRegistrationSecurityService
{
    public int MaxAttempts => 3;
    public TimeSpan LockoutWindow => TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, List<DateTime>> _attempts = new();
    private readonly object _cleanupLock = new();
    private DateTime _lastCleanup = DateTime.UtcNow;

    private static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "127.0.0.1";
        var clean = ip.Trim();
        // В случае IPv6 loopback
        if (clean == "::1") return "127.0.0.1";
        return clean;
    }

    public bool IsBlocked(string ip, out int remainingAttempts, out TimeSpan? retryAfter)
    {
        var normalized = NormalizeIp(ip);
        CleanupExpiredIfNeeded();

        if (_attempts.TryGetValue(normalized, out var list))
        {
            lock (list)
            {
                var cutoff = DateTime.UtcNow - LockoutWindow;
                list.RemoveAll(t => t < cutoff);

                int used = list.Count;
                remainingAttempts = Math.Max(0, MaxAttempts - used);

                if (used >= MaxAttempts)
                {
                    var oldest = list.Min();
                    var unlockAt = oldest + LockoutWindow;
                    var diff = unlockAt - DateTime.UtcNow;
                    retryAfter = diff > TimeSpan.Zero ? diff : TimeSpan.FromSeconds(30);
                    return true;
                }

                retryAfter = null;
                return false;
            }
        }

        remainingAttempts = MaxAttempts;
        retryAfter = null;
        return false;
    }

    public int RecordAttempt(string ip)
    {
        var normalized = NormalizeIp(ip);
        CleanupExpiredIfNeeded();

        var list = _attempts.GetOrAdd(normalized, _ => new List<DateTime>());
        lock (list)
        {
            var cutoff = DateTime.UtcNow - LockoutWindow;
            list.RemoveAll(t => t < cutoff);
            list.Add(DateTime.UtcNow);

            return Math.Max(0, MaxAttempts - list.Count);
        }
    }

    public int GetRemainingAttempts(string ip)
    {
        IsBlocked(ip, out var remaining, out _);
        return remaining;
    }

    public void Reset(string ip)
    {
        var normalized = NormalizeIp(ip);
        _attempts.TryRemove(normalized, out _);
    }

    private void CleanupExpiredIfNeeded()
    {
        if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(5)) return;

        lock (_cleanupLock)
        {
            if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(5)) return;
            _lastCleanup = DateTime.UtcNow;

            var cutoff = DateTime.UtcNow - LockoutWindow;
            foreach (var kvp in _attempts)
            {
                lock (kvp.Value)
                {
                    kvp.Value.RemoveAll(t => t < cutoff);
                }
                if (kvp.Value.Count == 0)
                {
                    _attempts.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
