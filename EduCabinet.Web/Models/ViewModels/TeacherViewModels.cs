using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Models.ViewModels;

public class TeacherDashboardViewModel
{
    public int DisciplinesCount { get; set; }
    public int TopicsCount { get; set; }
    public int LecturesCount { get; set; }
    public int StudentsCount { get; set; }
    public int PendingReviewsCount { get; set; }
    public List<EventLog> RecentLogs { get; set; } = new();
    public List<DisciplineSummaryDto> Disciplines { get; set; } = new();
}

public class DisciplineSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TopicsCount { get; set; }
    public int LecturesCount { get; set; }
    public int EnrolledStudentsCount { get; set; }
}

public class GroupProgressReportViewModel
{
    public int SelectedDisciplineId { get; set; }
    public int? SelectedGroupId { get; set; }
    public List<Discipline> Disciplines { get; set; } = new();
    public List<StudentGroup> Groups { get; set; } = new();

    public string DisciplineTitle { get; set; } = string.Empty;
    public List<LectureReportColumnDto> Lectures { get; set; } = new();
    public List<StudentProgressRowDto> Students { get; set; } = new();
    public List<StudentIncompleteDto> IncompleteStudents { get; set; } = new();
    public List<QuestionDifficultyDto> DifficultQuestions { get; set; } = new();
}

public class QuestionDifficultyDto
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string TestTitle { get; set; } = string.Empty;
    public string LectureTitle { get; set; } = string.Empty;
    public int TotalAnswers { get; set; }
    public int IncorrectAnswers { get; set; }
    public double ErrorRatePercent { get; set; }
}

public class LectureReportColumnDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
}

public class StudentProgressRowDto
{
    public int StudentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public int CompletedLecturesCount { get; set; }
    public Dictionary<int, StudentLectureStatusDto> LectureStatuses { get; set; } = new();
}

public class StudentLectureStatusDto
{
    public bool IsCompleted { get; set; }
    public bool IsManuallyUnlocked { get; set; }
    public bool IsControlQuestionsPassed { get; set; }
    public bool IsTestPassed { get; set; }
    public int? TestScorePercent { get; set; }
    public int AttemptsCount { get; set; }
}

public class StudentIncompleteDto
{
    public string StudentFullName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string LastCompletedLecture { get; set; } = string.Empty;
    public string BlockedAtLecture { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class ManualReviewItemViewModel
{
    public int AnswerId { get; set; }
    public int StudentId { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int LectureId { get; set; }
    public string LectureTitle { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string StudentAnswer { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
}

public class ManualReviewGradeInputModel
{
    [Required]
    public int AnswerId { get; set; }
    [Required]
    public bool IsCorrect { get; set; } // true = Зачтено, false = На доработку
    public string? TeacherComment { get; set; }
}

public class ManualUnlockInputModel
{
    [Required(ErrorMessage = "Выберите студента")]
    public int StudentId { get; set; }

    [Required(ErrorMessage = "Выберите лекцию")]
    public int LectureId { get; set; }

    [Required(ErrorMessage = "Укажите причину ручного открытия")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Причина должна содержать от 5 до 300 символов")]
    public string Reason { get; set; } = string.Empty;
}

public class ResetAttemptsInputModel
{
    [Required]
    public int StudentId { get; set; }
    [Required]
    public int TestId { get; set; }
    public string? Reason { get; set; }
}

public class DisciplineEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Введите название дисциплины")]
    [StringLength(200, MinimumLength = 3)]
    [Display(Name = "Название дисциплины")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите описание")]
    [Display(Name = "Описание дисциплины")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Закрепленные учебные группы")]
    public List<int> SelectedGroupIds { get; set; } = new();

    public List<StudentGroup> AvailableGroups { get; set; } = new();
}

public class TopicEditViewModel
{
    public int Id { get; set; }
    public int DisciplineId { get; set; }
    public string DisciplineTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите название темы")]
    [StringLength(200, MinimumLength = 3)]
    [Display(Name = "Название темы")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Описание темы")]
    public string? Description { get; set; }

    [Display(Name = "Порядковый номер")]
    public int OrderIndex { get; set; } = 1;
}

public class LectureEditViewModel
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public int DisciplineId { get; set; }

    [Required(ErrorMessage = "Введите название лекции")]
    [StringLength(250, MinimumLength = 3)]
    [Display(Name = "Название лекции")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Краткое описание")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Введите теоретический материал")]
    [Display(Name = "Теоретический материал (HTML/текст)")]
    public string ContentHtml { get; set; } = string.Empty;

    [Display(Name = "Порядковый номер лекции")]
    public int OrderIndex { get; set; } = 1;

    [Display(Name = "Требовать обязательную ручную проверку развёрнутых ответов до открытия следующей лекции")]
    public bool RequiresManualReview { get; set; } = true;

    [Display(Name = "Требовать сдачу отчёта/файла студентом")]
    public bool RequiresReportSubmission { get; set; } = false;

    [Display(Name = "Инструкции и требования к отчету")]
    public string? ReportInstructions { get; set; }

    [Display(Name = "Максимальный балл за отчет")]
    [Range(1, 1000, ErrorMessage = "Максимальный балл должен быть от 1 до 1000")]
    public int? MaxReportScore { get; set; } = 100;

    [Display(Name = "Крайний срок сдачи отчета (дедлайн)")]
    public DateTime? ReportDeadline { get; set; }

    [Display(Name = "Файл материала (PDF, DOCX и др.)")]
    public IFormFile? AttachmentFile { get; set; }

    public string? ExistingAttachmentName { get; set; }
    public string? ExistingAttachmentPath { get; set; }
}

public class QuestionCreateEditViewModel
{
    public int Id { get; set; }
    public int LectureId { get; set; }
    public int TestId { get; set; }
    public string LectureTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите текст вопроса")]
    [Display(Name = "Текст вопроса")]
    public string Text { get; set; } = string.Empty;

    [Display(Name = "Тип вопроса")]
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;

    [Display(Name = "Порядковый номер")]
    public int OrderIndex { get; set; } = 1;

    [Display(Name = "Количество баллов")]
    public int Points { get; set; } = 1;

    [Display(Name = "Изображение к вопросу")]
    public string? ImageUrl { get; set; }

    [Display(Name = "Файл изображения")]
    public IFormFile? ImageFile { get; set; }

    public bool RemoveImage { get; set; }

    [Display(Name = "Пояснение / подсказка")]
    public string? Explanation { get; set; }

    [Display(Name = "Правильный ответ (для краткого ответа или пропущенного слова)")]
    public string? CorrectTextAnswer { get; set; }

    [Display(Name = "Синонимы правильного ответа через запятую")]
    public string? SynonymsCsv { get; set; }

    // Варианты ответов
    public List<QuestionOptionInputDto> Options { get; set; } = new();
}

public class QuestionOptionInputDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string? MatchingKey { get; set; }
    public int OrderIndex { get; set; }
    public string? ImageUrl { get; set; }
}

public class TestEditViewModel
{
    public int Id { get; set; }
    public int LectureId { get; set; }
    public string LectureTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите название теста")]
    [Display(Name = "Название теста")]
    public string Title { get; set; } = "Итоговый тест";

    [Display(Name = "Описание / инструкции")]
    public string? Description { get; set; }

    [Range(1, 100, ErrorMessage = "Проходной процент от 1 до 100%")]
    [Display(Name = "Проходной балл (%)")]
    public int PassingScorePercent { get; set; } = 70;

    [Range(1, 10, ErrorMessage = "Количество попыток от 1 до 10")]
    [Display(Name = "Максимальное число попыток")]
    public int MaxAttempts { get; set; } = 3;

    [Display(Name = "Ограничение по времени (в минутах, 0 = без ограничения)")]
    public int? TimeLimitMinutes { get; set; } = 15;

    [Display(Name = "Перемешивать порядок вопросов")]
    public bool ShuffleQuestions { get; set; } = true;

    [Display(Name = "Перемешивать варианты ответов")]
    public bool ShuffleOptions { get; set; } = true;

    [Display(Name = "Активен")]
    public bool IsActive { get; set; } = true;

    public List<TestQuestionSummaryDto> Questions { get; set; } = new();
}

public class TestQuestionSummaryDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public int Points { get; set; }
    public int OrderIndex { get; set; }
    public int OptionsCount { get; set; }
    public string? ImageUrl { get; set; }
}

public class TeacherStudentsPageViewModel
{
    public List<GroupWithStudentsDto> Groups { get; set; } = new();
    public int SelectedCourse { get; set; } = 0; // 0 = Все курсы, 1..4 = Конкретный курс
    public int TotalGroupsCount => Groups.Count;
    public int TotalStudentsCount => Groups.Sum(g => g.Students.Count);
    public int GroupsCountForCourse(int c) => Groups.Count(g => g.CourseNumber == c);
    public int StudentsCountForCourse(int c) => Groups.Where(g => g.CourseNumber == c).Sum(g => g.Students.Count);
}

public class GroupWithStudentsDto
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public DateTime? InviteCodeExpiresAt { get; set; }
    public int RemainingSeconds => InviteCodeExpiresAt.HasValue && InviteCodeExpiresAt.Value > DateTime.UtcNow 
        ? (int)Math.Max(0, (InviteCodeExpiresAt.Value - DateTime.UtcNow).TotalSeconds) 
        : 0;
    public bool IsInviteExpired => RemainingSeconds == 0;
    public int CourseNumber { get; set; } = 1;
    public int StudyDurationYears { get; set; } = 4;
    public int AdmissionYear { get; set; } = 2026;
    public List<StudentCardProfileDto> Students { get; set; } = new();
}

public class StudentCardProfileDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsEmailConfirmed { get; set; }
    public int CompletedLecturesCount { get; set; }
    public int TestsPassedCount { get; set; }
    public double? AverageScorePercent { get; set; }
    public string RadarStatus { get; set; } = "В графике";
    public string RadarBadgeClass { get; set; } = "bg-primary";
    public List<StudentAttemptHistoryDto> RecentAttempts { get; set; } = new();
}

public class StudentAttemptHistoryDto
{
    public string TestTitle { get; set; } = string.Empty;
    public string LectureTitle { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public int ScorePercent { get; set; }
    public bool IsPassed { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TeacherReportsViewModel
{
    public int? SelectedDisciplineId { get; set; }
    public int? SelectedGroupId { get; set; }
    public ReportSubmissionStatus? SelectedStatus { get; set; }
    public string? SearchQuery { get; set; }

    public List<Discipline> Disciplines { get; set; } = new();
    public List<StudentGroup> Groups { get; set; } = new();
    public List<TeacherReportItemViewModel> Reports { get; set; } = new();

    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int NeedsRevisionCount { get; set; }
    public int RejectedCount { get; set; }
}

public class TeacherReportItemViewModel
{
    public int Id { get; set; }
    public int LectureId { get; set; }
    public string LectureTitle { get; set; } = string.Empty;
    public int DisciplineId { get; set; }
    public string DisciplineTitle { get; set; } = string.Empty;
    public int StudentId { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public string StudentGroupName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? StudentComment { get; set; }
    public DateTime SubmittedAt { get; set; }
    public ReportSubmissionStatus Status { get; set; }
    public int? Grade { get; set; }
    public int? MaxScore { get; set; }
    public string? TeacherFeedback { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByTeacherName { get; set; }
    public string? ReviewedFilePath { get; set; }
    public string? ReviewedFileName { get; set; }
    public long? ReviewedFileSizeBytes { get; set; }
}

public class GradeReportInputModel
{
    [Required]
    public int ReportId { get; set; }

    [Required]
    public ReportSubmissionStatus Status { get; set; }

    [Range(0, 1000, ErrorMessage = "Балл должен быть от 0 до 1000")]
    public int? Grade { get; set; }

    [StringLength(1000, ErrorMessage = "Отзыв не должен превышать 1000 символов")]
    public string? TeacherFeedback { get; set; }

    public IFormFile? ReviewedFile { get; set; }
}
