using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.DTOs;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Controllers.Api;

[Authorize(Roles = "Teacher,Admin")]
[ApiController]
[Route("api/teacher")]
[Produces("application/json")]
public class TeacherApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILectureAccessService _accessService;
    private readonly IEventLogService _eventLogger;

    public TeacherApiController(
        AppDbContext db,
        ILectureAccessService accessService,
        IEventLogService eventLogger)
    {
        _db = db;
        _accessService = accessService;
        _eventLogger = eventLogger;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out int id) ? id : 0;
    }

    /// <summary>
    /// Получить сводную статистику системы для панели преподавателя
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary()
    {
        int disciplinesCount = await _db.Disciplines.CountAsync(d => d.IsActive);
        int topicsCount = await _db.Topics.CountAsync();
        int lecturesCount = await _db.Lectures.CountAsync();
        int studentsCount = await _db.Users.CountAsync(u => u.Role == UserRole.Student);
        int pendingReviews = await _db.StudentControlAnswers
            .Include(a => a.ControlQuestion)
            .CountAsync(a => a.ControlQuestion.Type == QuestionType.OpenEnded && !a.IsReviewedByTeacher);

        return Ok(new
        {
            disciplines = disciplinesCount,
            topics = topicsCount,
            lectures = lecturesCount,
            students = studentsCount,
            pendingReviews = pendingReviews
        });
    }

    /// <summary>
    /// Ручное открытие лекции студенту в индивидуальном порядке с фиксацией в аудите
    /// </summary>
    [HttpPost("unlock")]
    public async Task<ActionResult> ManualUnlock([FromBody] ManualUnlockRequestDto dto)
    {
        int teacherId = GetCurrentUserId();

        bool success = await _accessService.ManuallyUnlockLectureAsync(
            dto.StudentId,
            dto.LectureId,
            dto.Reason,
            teacherId,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        if (!success)
        {
            return BadRequest(new { message = "Не удалось открыть лекцию. Проверьте ID студента и лекции." });
        }

        return Ok(new { success = true, message = "Лекция успешно открыта студенту вручную" });
    }

    /// <summary>
    /// Проверить и оценить развернутый ответ студента
    /// </summary>
    [HttpPost("grade-review")]
    public async Task<ActionResult> GradeReview([FromBody] GradeReviewRequestDto dto)
    {
        var answer = await _db.StudentControlAnswers
            .Include(a => a.Student)
            .Include(a => a.ControlQuestion)
                .ThenInclude(cq => cq.Lecture)
            .FirstOrDefaultAsync(a => a.Id == dto.AnswerId);

        if (answer == null) return NotFound(new { message = "Ответ не найден" });

        answer.IsReviewedByTeacher = true;
        answer.IsCorrect = dto.IsCorrect;
        answer.TeacherComment = dto.TeacherComment?.Trim();

        await _db.SaveChangesAsync();

        await _accessService.UpdateLectureCompletionStatusAsync(answer.StudentId, answer.ControlQuestion.LectureId);

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "API_GRADE_ANSWER",
            $"Проверен ответ студента {answer.Student.FullName}. Оценка: {(dto.IsCorrect ? "Зачтено" : "Не зачтено")}",
            answer.Student.FullName,
            answer.ControlQuestion.Lecture.Title
        );

        return Ok(new { success = true, isCorrect = dto.IsCorrect });
    }

    /// <summary>
    /// Сброс попыток тестирования для студента
    /// </summary>
    [HttpPost("reset-attempts")]
    public async Task<ActionResult> ResetAttempts([FromBody] ResetAttemptsRequestDto dto)
    {
        var test = await _db.Tests.Include(t => t.Lecture).FirstOrDefaultAsync(t => t.Id == dto.TestId);
        var student = await _db.Users.FindAsync(dto.StudentId);

        if (test == null || student == null) return NotFound(new { message = "Тест или студент не найден" });

        var attempts = await _db.TestAttempts
            .Where(a => a.StudentId == dto.StudentId && a.TestId == dto.TestId)
            .ToListAsync();

        _db.TestAttempts.RemoveRange(attempts);

        var prog = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == dto.StudentId && lp.LectureId == test.LectureId);

        if (prog != null)
        {
            prog.IsTestPassed = false;
            prog.IsCompleted = false;
        }

        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "API_RESET_ATTEMPTS",
            $"Сброшены {attempts.Count} попыток тестирования для {student.FullName}",
            student.FullName,
            test.Lecture.Title
        );

        return Ok(new { success = true, deletedAttempts = attempts.Count });
    }
}
