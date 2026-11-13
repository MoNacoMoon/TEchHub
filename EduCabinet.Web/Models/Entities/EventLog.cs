namespace EduCabinet.Web.Models.Entities;

public class EventLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }

    public string Action { get; set; } = string.Empty; // e.g. "MANUAL_UNLOCK", "RESET_ATTEMPTS", "UPDATE_MATERIAL", "GRADE_ANSWER"
    public string Description { get; set; } = string.Empty;
    public string? TargetStudent { get; set; }
    public string? TargetLecture { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
