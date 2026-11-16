using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Models.ViewModels;

public class StudentAchievement
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ColorClass { get; set; } = "indigo"; // gold, emerald, indigo, locked-icon
    public bool IsUnlocked { get; set; }
    public string? UnlockedInfo { get; set; }
}

public class StudentDashboardViewModel
{
    public string StudentFullName { get; set; } = string.Empty;
    public string StudentUsername { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public DateTime MemberSince { get; set; } = DateTime.UtcNow;
    public List<DisciplineCardViewModel> Disciplines { get; set; } = new();

    // Показатели успеваемости и геймификация
    public int TotalCompletedLectures { get; set; }
    public int TotalLectures { get; set; }
    public int OverallProgressPercent { get; set; }
    public double? AverageTestScore { get; set; }
    public int TestsPassedCount { get; set; }
    public int PerfectScoresCount { get; set; }
    public int GroupRank { get; set; } = 1;
    public int GroupTotalStudents { get; set; } = 1;
    public List<StudentAchievement> Achievements { get; set; } = new();

    // Ударный режим (Streak) и активность по дням недели
    public int LearningStreakDays { get; set; } = 5;
    public List<StudentDayActivityDto> WeeklyActivity { get; set; } = new();

    // Текущий фокус (Spotlight: лекция для продолжения)
    public SpotlightLectureDto? SpotlightLecture { get; set; }

    // Лента последних результатов тестирования
    public List<StudentRecentAttemptDto> RecentAttempts { get; set; } = new();
}

public class SpotlightLectureDto
{
    public int DisciplineId { get; set; }
    public string DisciplineTitle { get; set; } = string.Empty;
    public int LectureId { get; set; }
    public string LectureTitle { get; set; } = string.Empty;
    public int LectureOrderIndex { get; set; }
    public int DisciplineProgressPercent { get; set; }
}

public class StudentDayActivityDto
{
    public string DayName { get; set; } = string.Empty; // Пн, Вт...
    public bool IsActive { get; set; }
    public bool IsToday { get; set; }
}

public class StudentRecentAttemptDto
{
    public string DisciplineTitle { get; set; } = string.Empty;
    public string TestTitle { get; set; } = string.Empty;
    public int ScorePercent { get; set; }
    public bool IsPassed { get; set; }
    public DateTime CompletedAt { get; set; }
}

public class DisciplineCardViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TotalLectures { get; set; }
    public int CompletedLectures { get; set; }
    public int ProgressPercent { get; set; }
    public int? NextAvailableLectureId { get; set; }
    public string? NextAvailableLectureTitle { get; set; }
    public bool IsCertificateAvailable => ProgressPercent >= 100;
}

public class DisciplineStudyViewModel
{
    public int DisciplineId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public int TotalLectures { get; set; }
    public int CompletedLectures { get; set; }
    public List<TopicStudyViewModel> Topics { get; set; } = new();
    public List<CourseAnnouncementDto> Announcements { get; set; } = new();
    public bool IsCertificateAvailable => ProgressPercent >= 100;
}

public class CourseAnnouncementDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StudentCertificateViewModel
{
    public string CertificateNumber { get; set; } = string.Empty;
    public string StudentFullName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int DisciplineId { get; set; }
    public string DisciplineTitle { get; set; } = string.Empty;
    public string TeacherFullName { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public int TotalLectures { get; set; }
    public int AverageScorePercent { get; set; }
    public string GradeHonor { get; set; } = "С отличием";
    public List<string> TopicsCompleted { get; set; } = new();
    public string VerificationQrUrl { get; set; } = string.Empty;
}

public class TopicStudyViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OrderIndex { get; set; }
    public List<LectureItemViewModel> Lectures { get; set; } = new();
}

public class LectureItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public LectureAccessStatus Status { get; set; }
    public bool HasAttachment { get; set; }
    public bool HasTest { get; set; }
    public bool HasControlQuestions { get; set; }
    public bool IsCompleted { get; set; }
    public int? BestTestScore { get; set; }
    public bool IsManuallyUnlocked { get; set; }
}

public class LectureStudyViewModel
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public int DisciplineId { get; set; }
    public string DisciplineTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string? AttachmentFilePath { get; set; }
    public string? AttachmentOriginalName { get; set; }
    public string? AttachmentContentType { get; set; }
    public int OrderIndex { get; set; }
    public bool RequiresManualReview { get; set; }

    public bool IsCompleted { get; set; }
    public bool IsControlQuestionsPassed { get; set; }
    public bool IsTestPassed { get; set; }
    public bool IsManuallyUnlocked { get; set; }

    public int? NextLectureId { get; set; }
    public bool CanOpenNextLecture { get; set; }

    public List<ControlQuestionStudyDto> ControlQuestions { get; set; } = new();
    public TestSummaryDto? TestSummary { get; set; }
}

public class ControlQuestionStudyDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public int OrderIndex { get; set; }
    public string? Explanation { get; set; }
    public string? ImageUrl { get; set; }
    public List<QuestionOptionDto> Options { get; set; } = new();

    public bool IsAnswered { get; set; }
    public string? StudentGivenAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public bool IsReviewedByTeacher { get; set; }
    public string? TeacherComment { get; set; }
}

public class QuestionOptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? MatchingKey { get; set; }
    public string? ImageUrl { get; set; }
}

public class TestSummaryDto
{
    public int TestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PassingScorePercent { get; set; }
    public int MaxAttempts { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int AttemptsUsed { get; set; }
    public int AttemptsRemaining => Math.Max(0, MaxAttempts - AttemptsUsed);
    public bool CanTakeTest => AttemptsRemaining > 0;
    public bool IsPassed { get; set; }
    public int? BestScorePercent { get; set; }
    public List<TestAttemptSummaryDto> Attempts { get; set; } = new();
}

public class TestAttemptSummaryDto
{
    public int AttemptId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime CompletedAt { get; set; }
    public int ScorePercent { get; set; }
    public int CorrectQuestions { get; set; }
    public int TotalQuestions { get; set; }
    public bool IsPassed { get; set; }
}

public class TakeTestViewModel
{
    public int TestId { get; set; }
    public int LectureId { get; set; }
    public string LectureTitle { get; set; } = string.Empty;
    public string TestTitle { get; set; } = string.Empty;
    public int PassingScorePercent { get; set; }
    public int? TimeLimitSeconds { get; set; }
    public int AttemptNumber { get; set; }
    public List<TestQuestionTakeDto> Questions { get; set; } = new();
}

public class TestQuestionTakeDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public int Points { get; set; }
    public string? ImageUrl { get; set; }
    public List<QuestionOptionDto> Options { get; set; } = new();
    public List<string> MatchingTargets { get; set; } = new(); // Для Matching вопросов - перемешанные варианты правой стороны
}

public class SubmitTestInputModel
{
    public int TestId { get; set; }
    public Dictionary<int, string> Answers { get; set; } = new();
}

public class TestResultViewModel
{
    public int AttemptId { get; set; }
    public int TestId { get; set; }
    public int LectureId { get; set; }
    public string LectureTitle { get; set; } = string.Empty;
    public string TestTitle { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectQuestions { get; set; }
    public int ScorePercent { get; set; }
    public int PassingScorePercent { get; set; }
    public bool IsPassed { get; set; }
    public int AttemptsRemaining { get; set; }
    public bool NextLectureUnlocked { get; set; }
    public int? NextLectureId { get; set; }
    public List<TestAnswerDetailDto> Answers { get; set; } = new();
}

public class TestAnswerDetailDto
{
    public string QuestionText { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public string? ImageUrl { get; set; }
    public string GivenAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
}
