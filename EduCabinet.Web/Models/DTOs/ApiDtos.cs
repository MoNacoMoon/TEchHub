using System.ComponentModel.DataAnnotations;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Models.DTOs;

// --- Auth DTOs ---
public class LoginRequestDto
{
    [Required]
    public string Username { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? FullName { get; set; }
    public string? Role { get; set; }
}

public class RegisterRequestDto
{
    [Required]
    public string Username { get; set; } = string.Empty;
    [Required]
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    [Required]
    public int GroupId { get; set; }
    [Required]
    public string Password { get; set; } = string.Empty;
}

// --- Discipline DTOs ---
public class DisciplineDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TopicsCount { get; set; }
    public int LecturesCount { get; set; }
}

public class DisciplineDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public List<TopicDto> Topics { get; set; } = new();
}

public class TopicDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OrderIndex { get; set; }
    public List<LectureSummaryDto> Lectures { get; set; } = new();
}

public class LectureSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public LectureAccessStatus Status { get; set; }
    public bool HasAttachment { get; set; }
    public bool HasTest { get; set; }
    public bool IsCompleted { get; set; }
    public int? BestTestScore { get; set; }
    public bool IsManuallyUnlocked { get; set; }
}

// --- Lecture & Question DTOs ---
public class LectureDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TopicTitle { get; set; } = string.Empty;
    public string DisciplineTitle { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string? AttachmentFileName { get; set; }
    public string? AttachmentDownloadUrl { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsControlQuestionsPassed { get; set; }
    public bool IsTestPassed { get; set; }
    public List<ControlQuestionDto> Questions { get; set; } = new();
    public TestOverviewDto? TestOverview { get; set; }
}

public class ControlQuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public int OrderIndex { get; set; }
    public string? Explanation { get; set; }
    public List<OptionDto> Options { get; set; } = new();
    public bool IsAnswered { get; set; }
    public bool IsCorrect { get; set; }
    public string? StudentAnswer { get; set; }
}

public class OptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? MatchingKey { get; set; }
}

public class SubmitAnswerRequestDto
{
    [Required]
    public int QuestionId { get; set; }
    [Required]
    public string Answer { get; set; } = string.Empty;
}

public class SubmitAnswerResponseDto
{
    public bool Success { get; set; }
    public bool IsCorrect { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public bool IsOpenEnded { get; set; }
    public bool IsLectureCompleted { get; set; }
}

// --- Test DTOs ---
public class TestOverviewDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PassingScorePercent { get; set; }
    public int MaxAttempts { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int AttemptsUsed { get; set; }
    public bool IsPassed { get; set; }
    public int? BestScorePercent { get; set; }
}

public class TakeTestDto
{
    public int TestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int PassingScorePercent { get; set; }
    public int? TimeLimitSeconds { get; set; }
    public int AttemptNumber { get; set; }
    public List<TestQuestionDto> Questions { get; set; } = new();
}

public class TestQuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public int Points { get; set; }
    public List<OptionDto> Options { get; set; } = new();
    public List<string> MatchingTargets { get; set; } = new();
}

public class SubmitTestRequestDto
{
    [Required]
    public int TestId { get; set; }
    public Dictionary<int, string> Answers { get; set; } = new();
}

public class SubmitTestResponseDto
{
    public int AttemptId { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectQuestions { get; set; }
    public int ScorePercent { get; set; }
    public bool IsPassed { get; set; }
    public int PassingScorePercent { get; set; }
    public int AttemptsRemaining { get; set; }
    public bool NextLectureUnlocked { get; set; }
    public int? NextLectureId { get; set; }
}

// --- Teacher API DTOs ---
public class ManualUnlockRequestDto
{
    [Required]
    public int StudentId { get; set; }
    [Required]
    public int LectureId { get; set; }
    [Required]
    [StringLength(300, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public class GradeReviewRequestDto
{
    [Required]
    public int AnswerId { get; set; }
    [Required]
    public bool IsCorrect { get; set; }
    public string? TeacherComment { get; set; }
}

public class ResetAttemptsRequestDto
{
    [Required]
    public int StudentId { get; set; }
    [Required]
    public int TestId { get; set; }
    public string? Reason { get; set; }
}
