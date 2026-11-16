namespace EduCabinet.Web.Services;

public enum LectureAccessStatus
{
    Locked = 0,
    Available = 1,
    Completed = 2
}

public class LectureStatusInfo
{
    public int LectureId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TopicId { get; set; }
    public int OrderIndex { get; set; }
    public LectureAccessStatus Status { get; set; }
    public bool IsControlQuestionsPassed { get; set; }
    public bool IsTestPassed { get; set; }
    public bool IsManuallyUnlocked { get; set; }
    public bool RequiresReportSubmission { get; set; }
    public bool IsReportApproved { get; set; }
    public int? ReportGrade { get; set; }
    public int? BestTestScore { get; set; }
    public int TestAttemptsCount { get; set; }
    public int MaxTestAttempts { get; set; }
}

public class DisciplineProgressInfo
{
    public int DisciplineId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TotalLectures { get; set; }
    public int CompletedLectures { get; set; }
    public int ProgressPercent { get; set; }
    public List<LectureStatusInfo> Lectures { get; set; } = new();
}

public interface ILectureAccessService
{
    Task<bool> CanStudentAccessLectureAsync(int studentId, int lectureId);
    Task<LectureAccessStatus> GetLectureStatusAsync(int studentId, int lectureId);
    Task<DisciplineProgressInfo> GetDisciplineProgressAsync(int studentId, int disciplineId);
    Task<bool> UpdateLectureCompletionStatusAsync(int studentId, int lectureId);
    Task<bool> ManuallyUnlockLectureAsync(int studentId, int lectureId, string reason, int teacherUserId, string? ipAddress);
}
