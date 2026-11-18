using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task CreateNotificationAsync(int userId, string title, string message, NotificationType type = NotificationType.Info, string? url = null)
    {
        var notif = new UserNotification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Url = url,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.UserNotifications.Add(notif);
        await _db.SaveChangesAsync();
    }

    public async Task NotifyDisciplineTeachersAsync(int disciplineId, string title, string message, NotificationType type = NotificationType.Info, string? url = null)
    {
        var discipline = await _db.Disciplines
            .Include(d => d.CreatedBy)
            .FirstOrDefaultAsync(d => d.Id == disciplineId);

        var recipientIds = new HashSet<int>();
        if (discipline?.CreatedByUserId > 0)
        {
            recipientIds.Add(discipline.CreatedByUserId);
        }

        // Если автор не найден или для надежности уведомляем преподавателей/админов
        if (!recipientIds.Any())
        {
            var adminTeachers = await _db.Users
                .Where(u => u.IsActive && (u.Role == UserRole.Teacher || u.Role == UserRole.Admin))
                .Select(u => u.Id)
                .ToListAsync();
            foreach (var id in adminTeachers) recipientIds.Add(id);
        }

        var now = DateTime.UtcNow;
        foreach (var teacherId in recipientIds)
        {
            _db.UserNotifications.Add(new UserNotification
            {
                UserId = teacherId,
                Title = title,
                Message = message,
                Type = type,
                Url = url,
                IsRead = false,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<UserNotificationDto>> GetRecentNotificationsAsync(int userId, int take = 10)
    {
        var list = await _db.UserNotifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();

        return list.Select(n => new UserNotificationDto
        {
            Id = n.Id,
            Title = n.Title,
            Message = n.Message,
            Type = n.Type switch
            {
                NotificationType.Success => "success",
                NotificationType.Warning => "warning",
                NotificationType.Danger => "danger",
                _ => "info"
            },
            Url = n.Url,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
            TimeAgo = FormatTimeAgo(n.CreatedAt)
        }).ToList();
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        return await _db.UserNotifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<bool> MarkAsReadAsync(int notificationId, int userId)
    {
        var notif = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notif == null) return false;

        notif.IsRead = true;
        notif.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> MarkAllAsReadAsync(int userId)
    {
        var unread = await _db.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        if (!unread.Any()) return 0;

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }

        await _db.SaveChangesAsync();
        return unread.Count;
    }

    private static string FormatTimeAgo(DateTime utcDate)
    {
        var span = DateTime.UtcNow - utcDate;
        if (span.TotalSeconds < 60) return "Только что";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} мин. назад";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} ч. назад";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} дн. назад";
        return utcDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }
}
