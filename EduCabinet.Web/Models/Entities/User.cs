namespace EduCabinet.Web.Models.Entities;

public enum UserRole
{
    Admin = 0,
    Teacher = 1,
    Student = 2
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Подтверждение почты
    public bool IsEmailConfirmed { get; set; } = false;
    public string? EmailConfirmationCode { get; set; }
    public DateTime? EmailConfirmationExpiresAt { get; set; }

    // Восстановление пароля
    public string? PasswordResetCode { get; set; }
    public DateTime? PasswordResetExpiresAt { get; set; }

    // Navigation
    public ICollection<GroupMembership> GroupMemberships { get; set; } = new List<GroupMembership>();
    public ICollection<LectureProgress> LectureProgresses { get; set; } = new List<LectureProgress>();
    public ICollection<TestAttempt> TestAttempts { get; set; } = new List<TestAttempt>();
    public ICollection<StudentControlAnswer> ControlAnswers { get; set; } = new List<StudentControlAnswer>();
}
