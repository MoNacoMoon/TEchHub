using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.DTOs;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/lectures")]
[Produces("application/json")]
public class LecturesApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILectureAccessService _accessService;
    private readonly IGradingService _gradingService;

    public LecturesApiController(
        AppDbContext db,
        ILectureAccessService accessService,
        IGradingService gradingService)
    {
        _db = db;
        _accessService = accessService;
        _gradingService = gradingService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out int id) ? id : 0;
    }

    /// <summary>
    /// Получить данные лекции (с серверной валидацией последовательного доступа)
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<LectureDetailDto>> GetById(int id)
    {
        int studentId = GetCurrentUserId();

        // СТРОГИЙ СЕРВЕРНЫЙ КОНТРОЛЬ:
        // Если лекция заблокирована, возвращаем 403 Forbidden
        if (studentId > 0 && !User.IsInRole("Teacher") && !User.IsInRole("Admin"))
        {
            bool canAccess = await _accessService.CanStudentAccessLectureAsync(studentId, id);
            if (!canAccess)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Доступ к лекции заблокирован",
                    message = "Необходимо сначала выполнить задания и сдать тест предыдущей лекции."
                });
            }
        }

        var lecture = await _db.Lectures
            .Include(l => l.Topic)
                .ThenInclude(t => t.Discipline)
            .Include(l => l.ControlQuestions.OrderBy(cq => cq.OrderIndex))
                .ThenInclude(cq => cq.Options)
            .Include(l => l.Test)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lecture == null) return NotFound(new { message = "Лекция не найдена" });

        var progress = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == studentId && lp.LectureId == id);

        var cqIds = lecture.ControlQuestions.Select(cq => cq.Id).ToList();
        var studentAnswers = await _db.StudentControlAnswers
            .Where(a => a.StudentId == studentId && cqIds.Contains(a.ControlQuestionId))
            .ToDictionaryAsync(a => a.ControlQuestionId);

        var attempts = lecture.Test != null
            ? await _db.TestAttempts.Where(ta => ta.StudentId == studentId && ta.TestId == lecture.Test.Id).ToListAsync()
            : new List<TestAttempt>();

        var dto = new LectureDetailDto
        {
            Id = lecture.Id,
            Title = lecture.Title,
            TopicTitle = lecture.Topic.Title,
            DisciplineTitle = lecture.Topic.Discipline.Title,
            ContentHtml = lecture.ContentHtml,
            AttachmentFileName = lecture.AttachmentOriginalName,
            AttachmentDownloadUrl = !string.IsNullOrEmpty(lecture.AttachmentFilePath) ? $"/Student/DownloadAttachment/{lecture.Id}" : null,
            IsCompleted = progress?.IsCompleted ?? false,
            IsControlQuestionsPassed = progress?.IsControlQuestionsPassed ?? false,
            IsTestPassed = progress?.IsTestPassed ?? false,
            Questions = lecture.ControlQuestions.OrderBy(q => q.OrderIndex).Select(q =>
            {
                studentAnswers.TryGetValue(q.Id, out var ans);
                return new ControlQuestionDto
                {
                    Id = q.Id,
                    Text = q.Text,
                    Type = q.Type,
                    OrderIndex = q.OrderIndex,
                    Explanation = q.Explanation,
                    IsAnswered = ans != null,
                    IsCorrect = ans?.IsCorrect ?? false,
                    StudentAnswer = ans?.GivenAnswer,
                    Options = q.Options.OrderBy(o => o.OrderIndex).Select(o => new OptionDto
                    {
                        Id = o.Id,
                        Text = o.Text,
                        MatchingKey = o.MatchingKey
                    }).ToList()
                };
            }).ToList()
        };

        if (lecture.Test != null && lecture.Test.IsActive)
        {
            dto.TestOverview = new TestOverviewDto
            {
                Id = lecture.Test.Id,
                Title = lecture.Test.Title,
                Description = lecture.Test.Description,
                PassingScorePercent = lecture.Test.PassingScorePercent,
                MaxAttempts = lecture.Test.MaxAttempts,
                TimeLimitMinutes = lecture.Test.TimeLimitMinutes,
                AttemptsUsed = attempts.Count,
                IsPassed = attempts.Any(a => a.IsPassed),
                BestScorePercent = attempts.Any() ? attempts.Max(a => a.ScorePercent) : null
            };
        }

        return Ok(dto);
    }

    /// <summary>
    /// Проверить статус доступности лекции для текущего студента
    /// </summary>
    [HttpGet("{id:int}/status")]
    public async Task<ActionResult> GetStatus(int id)
    {
        int studentId = GetCurrentUserId();
        var status = await _accessService.GetLectureStatusAsync(studentId, id);
        return Ok(new { lectureId = id, status = status.ToString() });
    }

    /// <summary>
    /// Отправить ответ на контрольный вопрос
    /// </summary>
    [HttpPost("answer")]
    public async Task<ActionResult<SubmitAnswerResponseDto>> SubmitAnswer([FromBody] SubmitAnswerRequestDto dto)
    {
        int studentId = GetCurrentUserId();

        var question = await _db.ControlQuestions
            .Include(cq => cq.Options)
            .Include(cq => cq.Lecture)
            .FirstOrDefaultAsync(cq => cq.Id == dto.QuestionId);

        if (question == null) return NotFound(new { message = "Вопрос не найден" });

        if (studentId > 0 && !await _accessService.CanStudentAccessLectureAsync(studentId, question.LectureId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Доступ к лекции заблокирован" });
        }

        var grade = _gradingService.GradeControlQuestion(question, dto.Answer);

        var existingAnswer = await _db.StudentControlAnswers
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.ControlQuestionId == dto.QuestionId);

        if (existingAnswer == null)
        {
            existingAnswer = new StudentControlAnswer
            {
                StudentId = studentId,
                ControlQuestionId = dto.QuestionId,
                GivenAnswer = dto.Answer,
                IsCorrect = grade.IsCorrect,
                IsReviewedByTeacher = question.Type != QuestionType.OpenEnded,
                SubmittedAt = DateTime.UtcNow
            };
            _db.StudentControlAnswers.Add(existingAnswer);
        }
        else
        {
            existingAnswer.GivenAnswer = dto.Answer;
            existingAnswer.IsCorrect = grade.IsCorrect;
            existingAnswer.IsReviewedByTeacher = question.Type != QuestionType.OpenEnded;
            existingAnswer.SubmittedAt = DateTime.UtcNow;
            existingAnswer.TeacherComment = null;
        }

        await _db.SaveChangesAsync();

        bool isLectureCompleted = await _accessService.UpdateLectureCompletionStatusAsync(studentId, question.LectureId);

        return Ok(new SubmitAnswerResponseDto
        {
            Success = true,
            IsCorrect = grade.IsCorrect,
            Feedback = grade.Feedback,
            IsOpenEnded = question.Type == QuestionType.OpenEnded,
            IsLectureCompleted = isLectureCompleted
        });
    }
}
