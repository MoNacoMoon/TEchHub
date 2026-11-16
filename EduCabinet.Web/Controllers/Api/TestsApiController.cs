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
[Route("api/tests")]
[Produces("application/json")]
public class TestsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILectureAccessService _accessService;
    private readonly IGradingService _gradingService;
    private readonly IEventLogService _eventLogger;

    public TestsApiController(
        AppDbContext db,
        ILectureAccessService accessService,
        IGradingService gradingService,
        IEventLogService eventLogger)
    {
        _db = db;
        _accessService = accessService;
        _gradingService = gradingService;
        _eventLogger = eventLogger;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out int id) ? id : 0;
    }

    /// <summary>
    /// Получить вопросы и параметры теста для прохождения (с перемешиванием)
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TakeTestDto>> GetTestForTake(int id)
    {
        int studentId = GetCurrentUserId();

        var test = await _db.Tests
            .Include(t => t.Lecture)
            .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
            .Include(t => t.Attempts.Where(a => a.StudentId == studentId))
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive);

        if (test == null) return NotFound(new { message = "Тест не найден или не активен" });

        if (studentId > 0 && !await _accessService.CanStudentAccessLectureAsync(studentId, test.LectureId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Доступ к тестированию заблокирован" });
        }

        int attemptsCount = test.Attempts.Count;
        if (attemptsCount >= test.MaxAttempts)
        {
            return BadRequest(new { message = $"Исчерпан лимит попыток ({test.MaxAttempts}). Обратитесь к преподавателю." });
        }

        var questions = test.Questions.OrderBy(q => q.OrderIndex).ToList();
        if (test.ShuffleQuestions)
        {
            questions = questions.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        var dto = new TakeTestDto
        {
            TestId = test.Id,
            Title = test.Title,
            PassingScorePercent = test.PassingScorePercent,
            TimeLimitSeconds = test.TimeLimitMinutes.HasValue ? test.TimeLimitMinutes.Value * 60 : null,
            AttemptNumber = attemptsCount + 1,
            Questions = questions.Select(q =>
            {
                var options = q.Options.ToList();
                if (test.ShuffleOptions)
                {
                    options = options.OrderBy(_ => Random.Shared.Next()).ToList();
                }

                var qDto = new TestQuestionDto
                {
                    Id = q.Id,
                    Text = q.Text,
                    Type = q.Type,
                    Points = q.Points,
                    ImageUrl = q.ImageUrl,
                    Options = options.Select(o => new OptionDto
                    {
                        Id = o.Id,
                        Text = o.Text,
                        MatchingKey = o.MatchingKey,
                        ImageUrl = o.ImageUrl
                    }).ToList()
                };

                if (q.Type == QuestionType.Matching)
                {
                    qDto.MatchingTargets = q.Options.Where(o => !string.IsNullOrEmpty(o.MatchingKey))
                        .Select(o => o.MatchingKey!)
                        .Distinct()
                        .OrderBy(_ => Random.Shared.Next())
                        .ToList();
                }

                return qDto;
            }).ToList()
        };

        return Ok(dto);
    }

    /// <summary>
    /// Отправить ответы на тест и получить расчет результата
    /// </summary>
    [HttpPost("submit")]
    public async Task<ActionResult<SubmitTestResponseDto>> SubmitTest([FromBody] SubmitTestRequestDto input)
    {
        int studentId = GetCurrentUserId();

        var test = await _db.Tests
            .Include(t => t.Lecture)
                .ThenInclude(l => l.Topic)
            .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
            .Include(t => t.Attempts.Where(a => a.StudentId == studentId))
            .FirstOrDefaultAsync(t => t.Id == input.TestId);

        if (test == null) return NotFound(new { message = "Тест не найден" });

        if (studentId > 0 && !await _accessService.CanStudentAccessLectureAsync(studentId, test.LectureId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Доступ заблокирован" });
        }

        int attemptsCount = test.Attempts.Count;
        if (attemptsCount >= test.MaxAttempts)
        {
            return BadRequest(new { message = "Превышен лимит попыток сдачи теста" });
        }

        var attempt = new TestAttempt
        {
            StudentId = studentId,
            TestId = test.Id,
            AttemptNumber = attemptsCount + 1,
            StartedAt = DateTime.UtcNow.AddMinutes(-(test.TimeLimitMinutes ?? 10)),
            CompletedAt = DateTime.UtcNow,
            TotalQuestions = test.Questions.Count,
            CorrectQuestions = 0,
            Answers = new List<TestAttemptAnswer>()
        };

        foreach (var q in test.Questions)
        {
            input.Answers.TryGetValue(q.Id, out var givenAnswer);
            givenAnswer ??= string.Empty;

            var grade = _gradingService.GradeTestQuestion(q, givenAnswer);

            if (grade.IsCorrect) attempt.CorrectQuestions++;

            attempt.Answers.Add(new TestAttemptAnswer
            {
                TestQuestionId = q.Id,
                GivenAnswer = givenAnswer,
                IsCorrect = grade.IsCorrect,
                PointsEarned = grade.PointsEarned
            });
        }

        attempt.ScorePercent = attempt.TotalQuestions > 0
            ? (int)Math.Round((double)attempt.CorrectQuestions / attempt.TotalQuestions * 100)
            : 0;

        attempt.IsPassed = attempt.ScorePercent >= test.PassingScorePercent;

        _db.TestAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        // Обновляем статус завершения лекции (проверяет вопросы и тест)
        await _accessService.UpdateLectureCompletionStatusAsync(studentId, test.LectureId);

        // Проверяем, открылась ли следующая лекция
        var allDisciplineLectures = await _db.Lectures
            .Include(l => l.Topic)
            .Where(l => l.Topic.DisciplineId == test.Lecture.Topic.DisciplineId)
            .OrderBy(l => l.Topic.OrderIndex)
            .ThenBy(l => l.Topic.Id)
            .ThenBy(l => l.OrderIndex)
            .ThenBy(l => l.Id)
            .Select(l => l.Id)
            .ToListAsync();

        int currentIndex = allDisciplineLectures.IndexOf(test.LectureId);
        int? nextId = null;
        bool nextUnlocked = false;

        if (currentIndex >= 0 && currentIndex < allDisciplineLectures.Count - 1)
        {
            nextId = allDisciplineLectures[currentIndex + 1];
            nextUnlocked = await _accessService.CanStudentAccessLectureAsync(studentId, nextId.Value);
        }

        await _eventLogger.LogActionAsync(
            studentId,
            "API_TEST_SUBMITTED",
            $"Тест '{test.Title}' сдан через API. Результат: {attempt.ScorePercent}%. Сдано: {attempt.IsPassed}",
            User.Identity?.Name,
            test.Lecture.Title
        );

        return Ok(new SubmitTestResponseDto
        {
            AttemptId = attempt.Id,
            TotalQuestions = attempt.TotalQuestions,
            CorrectQuestions = attempt.CorrectQuestions,
            ScorePercent = attempt.ScorePercent,
            PassingScorePercent = test.PassingScorePercent,
            IsPassed = attempt.IsPassed,
            AttemptsRemaining = Math.Max(0, test.MaxAttempts - (attemptsCount + 1)),
            NextLectureUnlocked = nextUnlocked,
            NextLectureId = nextId
        });
    }
}
