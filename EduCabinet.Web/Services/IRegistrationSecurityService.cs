namespace EduCabinet.Web.Services;

/// <summary>
/// Сервис защиты регистрации: ограничение до 3 попыток на 1 IP-адрес
/// с временным окном блокировки для защиты от массовой регистрации и перебора инвайтов.
/// </summary>
public interface IRegistrationSecurityService
{
    int MaxAttempts { get; }
    TimeSpan LockoutWindow { get; }

    bool IsBlocked(string ip, out int remainingAttempts, out TimeSpan? retryAfter);
    int RecordAttempt(string ip);
    int GetRemainingAttempts(string ip);
    void Reset(string ip);
}
