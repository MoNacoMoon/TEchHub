namespace EduCabinet.Web.Models.Entities;

public enum NotificationType
{
    Info = 1,
    Success = 2,
    Warning = 3,
    Danger = 4
}

public class UserNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.Info;
    public string? Url { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
