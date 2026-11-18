using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Services;

public class UserNotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "info"; // info, success, warning, danger
    public string? Url { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TimeAgo { get; set; } = string.Empty;
}

public interface INotificationService
{
    Task CreateNotificationAsync(int userId, string title, string message, NotificationType type = NotificationType.Info, string? url = null);
    Task NotifyDisciplineTeachersAsync(int disciplineId, string title, string message, NotificationType type = NotificationType.Info, string? url = null);
    Task<List<UserNotificationDto>> GetRecentNotificationsAsync(int userId, int take = 10);
    Task<int> GetUnreadCountAsync(int userId);
    Task<bool> MarkAsReadAsync(int notificationId, int userId);
    Task<int> MarkAllAsReadAsync(int userId);
}
