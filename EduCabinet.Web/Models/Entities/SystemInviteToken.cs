namespace EduCabinet.Web.Models.Entities;

public class SystemInviteToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public UserRole TargetRole { get; set; } // Teacher or Admin
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
    public int? UsedByUserId { get; set; }
    public User? UsedByUser { get; set; }
    public DateTime? UsedAt { get; set; }
}
