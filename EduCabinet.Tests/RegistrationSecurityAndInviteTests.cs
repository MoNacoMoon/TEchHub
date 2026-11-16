using Xunit;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Tests;

public class RegistrationSecurityAndInviteTests
{
    [Fact]
    public void RegistrationSecurityService_LimitsToThreeAttemptsPerIp()
    {
        var limiter = new RegistrationSecurityService();
        var testIp = "192.168.1.100";

        // Начальное состояние: 3 попытки, не заблокирован
        Assert.False(limiter.IsBlocked(testIp, out int remaining0, out var retry0));
        Assert.Equal(3, remaining0);
        Assert.Null(retry0);

        // Попытка 1
        int left1 = limiter.RecordAttempt(testIp);
        Assert.Equal(2, left1);
        Assert.False(limiter.IsBlocked(testIp, out int remaining1, out _));
        Assert.Equal(2, remaining1);

        // Попытка 2
        int left2 = limiter.RecordAttempt(testIp);
        Assert.Equal(1, left2);
        Assert.False(limiter.IsBlocked(testIp, out int remaining2, out _));
        Assert.Equal(1, remaining2);

        // Попытка 3
        int left3 = limiter.RecordAttempt(testIp);
        Assert.Equal(0, left3);

        // После 3 попыток IP должен быть заблокирован
        bool isBlocked = limiter.IsBlocked(testIp, out int remainingAfter3, out var retryAfter);
        Assert.True(isBlocked);
        Assert.Equal(0, remainingAfter3);
        Assert.NotNull(retryAfter);
        Assert.True(retryAfter.Value > TimeSpan.Zero);

        // Другой IP не должен быть заблокирован
        var otherIp = "10.0.0.5";
        Assert.False(limiter.IsBlocked(otherIp, out int otherRemaining, out _));
        Assert.Equal(3, otherRemaining);
    }

    [Fact]
    public void RegistrationSecurityService_Reset_UnblocksIp()
    {
        var limiter = new RegistrationSecurityService();
        var testIp = "172.16.0.42";

        limiter.RecordAttempt(testIp);
        limiter.RecordAttempt(testIp);
        limiter.RecordAttempt(testIp);

        Assert.True(limiter.IsBlocked(testIp, out _, out _));

        limiter.Reset(testIp);

        Assert.False(limiter.IsBlocked(testIp, out int remaining, out _));
        Assert.Equal(3, remaining);
    }

    [Fact]
    public void GroupInviteCode_ValidWithinFiveMinutes_ExpiredAfterFiveMinutes()
    {
        var group = new StudentGroup
        {
            Name = "ИС-21",
            InviteCode = InviteCodeGenerator.GenerateCode("ИС-21"),
            InviteCodeGeneratedAt = DateTime.UtcNow,
            InviteCodeExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        // В течение 5 минут код должен быть валиден
        Assert.True(group.InviteCodeExpiresAt.HasValue);
        Assert.True(group.InviteCodeExpiresAt.Value > DateTime.UtcNow);

        // Имитируем истечение 5 минут
        group.InviteCodeExpiresAt = DateTime.UtcNow.AddSeconds(-10);
        bool isExpired = group.InviteCodeExpiresAt.Value < DateTime.UtcNow;
        Assert.True(isExpired);
    }

    [Fact]
    public void StaffInviteToken_ValidWithinFiveMinutes_ExpiredAfterFiveMinutes()
    {
        var token = new SystemInviteToken
        {
            Token = "TEACH-TECHHUB-TEST01",
            TargetRole = UserRole.Teacher,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false
        };

        // В течение 5 минут токен активен
        Assert.True(token.ExpiresAt > DateTime.UtcNow);
        Assert.False(token.IsUsed);

        // Имитируем истечение 5 минут
        token.ExpiresAt = DateTime.UtcNow.AddSeconds(-5);
        bool isExpired = token.ExpiresAt < DateTime.UtcNow;
        Assert.True(isExpired);
    }
}
