using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Services;

public class LectureAccessService : ILectureAccessService
{
    private readonly AppDbContext _db;
    private readonly IEventLogService _eventLogger;

    public LectureAccessService(AppDbContext db, IEventLogService eventLogger)
    {
        _db = db;
        _eventLogger = eventLogger;
    }

    public async Task<bool> CanStudentAccessLectureAsync(int studentId, int lectureId)
    {
        var status = await GetLectureStatusAsync(studentId, lectureId);
        return status != LectureAccessStatus.Locked;
    }

    public async Task<LectureAccessStatus> GetLectureStatusAsync(int studentId, int lectureId)
    {
        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .FirstOrDefaultAsync(l => l.Id == lectureId);

        if (lecture == null) return LectureAccessStatus.Locked;

        int disciplineId = lecture.Topic.DisciplineId;

        // Получаем все лекции дисциплины в правильном порядке
        var allLectures = await _db.Lectures
            .Include(l => l.Topic)
            .Where(l => l.Topic.DisciplineId == disciplineId)
            .OrderBy(l => l.Topic.OrderIndex)
            .ThenBy(l => l.Topic.Id)
            .ThenBy(l => l.OrderIndex)
            .ThenBy(l => l.Id)
            .ToListAsync();

        var lectureIds = allLectures.Select(l => l.Id).ToList();
        int targetIndex = lectureIds.IndexOf(lectureId);
        if (targetIndex < 0) return LectureAccessStatus.Locked;

        // Загружаем прогресс студента по дисциплине
        var progresses = await _db.LectureProgresses
            .Where(lp => lp.StudentId == studentId && lectureIds.Contains(lp.LectureId))
            .ToDictionaryAsync(lp => lp.LectureId);

        // Проверяем статус целевой лекции
        progresses.TryGetValue(lectureId, out var currentProgress);
        if (currentProgress != null && currentProgress.IsCompleted)
        {
            return LectureAccessStatus.Completed;
        }

        // Если это самая первая лекция дисциплины - она всегда доступна
        if (targetIndex == 0)
        {
            return LectureAccessStatus.Available;
        }

        // Если лекция открыта преподавателем вручную
        if (currentProgress != null && currentProgress.IsManuallyUnlocked)
        {
            return LectureAccessStatus.Available;
        }

        // Проверяем, завершены ли ВСЕ предыдущие лекции
        for (int i = 0; i < targetIndex; i++)
        {
            int prevLectureId = lectureIds[i];
            if (!progresses.TryGetValue(prevLectureId, out var prevProgress) || 
                (!prevProgress.IsCompleted && !prevProgress.IsManuallyUnlocked))
            {
                // Предыдущая лекция не пройдена и не открыта вручную -> блокируем текущую
                return LectureAccessStatus.Locked;
            }
        }

        // Все предыдущие лекции пройдены -> текущая доступна
        return LectureAccessStatus.Available;
    }

    public async Task<DisciplineProgressInfo> GetDisciplineProgressAsync(int studentId, int disciplineId)
    {
        var discipline = await _db.Disciplines
            .Include(d => d.Topics)
                .ThenInclude(t => t.Lectures)
                    .ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(d => d.Id == disciplineId);

        if (discipline == null)
        {
            return new DisciplineProgressInfo { DisciplineId = disciplineId };
        }

        var allLectures = discipline.Topics
            .OrderBy(t => t.OrderIndex)
            .ThenBy(t => t.Id)
            .SelectMany(t => t.Lectures.OrderBy(l => l.OrderIndex).ThenBy(l => l.Id))
            .ToList();

        var lectureIds = allLectures.Select(l => l.Id).ToList();

        var progresses = await _db.LectureProgresses
            .Where(lp => lp.StudentId == studentId && lectureIds.Contains(lp.LectureId))
            .ToDictionaryAsync(lp => lp.LectureId);

        var testAttempts = await _db.TestAttempts
            .Where(ta => ta.StudentId == studentId && lectureIds.Contains(ta.Test.LectureId))
            .GroupBy(ta => ta.Test.LectureId)
            .ToDictionaryAsync(
                g => g.Key,
                g => new { AttemptsCount = g.Count(), BestScore = g.Max(a => (int?)a.ScorePercent) }
            );

        var result = new DisciplineProgressInfo
        {
            DisciplineId = disciplineId,
            Title = discipline.Title,
            TotalLectures = allLectures.Count,
            CompletedLectures = 0,
            Lectures = new List<LectureStatusInfo>()
        };

        bool allPreviousCompleted = true;

        for (int i = 0; i < allLectures.Count; i++)
        {
            var lec = allLectures[i];
            progresses.TryGetValue(lec.Id, out var prog);

            testAttempts.TryGetValue(lec.Id, out var testInfo);

            var statusInfo = new LectureStatusInfo
            {
                LectureId = lec.Id,
                Title = lec.Title,
                TopicId = lec.TopicId,
                OrderIndex = lec.OrderIndex,
                IsControlQuestionsPassed = prog?.IsControlQuestionsPassed ?? false,
                IsTestPassed = prog?.IsTestPassed ?? false,
                IsManuallyUnlocked = prog?.IsManuallyUnlocked ?? false,
                RequiresReportSubmission = lec.RequiresReportSubmission,
                IsReportApproved = prog?.IsReportApproved ?? false,
                ReportGrade = prog?.ReportGrade,
                BestTestScore = testInfo?.BestScore,
                TestAttemptsCount = testInfo?.AttemptsCount ?? 0,
                MaxTestAttempts = lec.Test?.MaxAttempts ?? 0
            };

            if (prog != null && prog.IsCompleted)
            {
                statusInfo.Status = LectureAccessStatus.Completed;
                result.CompletedLectures++;
            }
            else if (i == 0 || (prog != null && prog.IsManuallyUnlocked) || allPreviousCompleted)
            {
                statusInfo.Status = LectureAccessStatus.Available;
            }
            else
            {
                statusInfo.Status = LectureAccessStatus.Locked;
            }

            if (!statusInfo.IsManuallyUnlocked && (prog == null || !prog.IsCompleted))
            {
                allPreviousCompleted = false;
            }

            result.Lectures.Add(statusInfo);
        }

        result.ProgressPercent = result.TotalLectures > 0 
            ? (int)Math.Round((double)result.CompletedLectures / result.TotalLectures * 100) 
            : 0;

        return result;
    }

    public async Task<bool> UpdateLectureCompletionStatusAsync(int studentId, int lectureId)
    {
        var lecture = await _db.Lectures
            .Include(l => l.ControlQuestions)
            .Include(l => l.Test)
                .ThenInclude(t => t!.Questions)
            .FirstOrDefaultAsync(l => l.Id == lectureId);

        if (lecture == null) return false;

        var progress = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == studentId && lp.LectureId == lectureId);

        if (progress == null)
        {
            progress = new LectureProgress
            {
                StudentId = studentId,
                LectureId = lectureId,
                FirstAccessedAt = DateTime.UtcNow
            };
            _db.LectureProgresses.Add(progress);
        }

        // 1. Проверяем контрольные вопросы
        bool questionsPassed = true;
        if (lecture.ControlQuestions.Any())
        {
            var studentAnswers = await _db.StudentControlAnswers
                .Where(a => a.StudentId == studentId && lecture.ControlQuestions.Select(cq => cq.Id).Contains(a.ControlQuestionId))
                .ToListAsync();

            // Должны быть ответы на ВСЕ контрольные вопросы
            if (studentAnswers.Count < lecture.ControlQuestions.Count)
            {
                questionsPassed = false;
            }
            else
            {
                foreach (var q in lecture.ControlQuestions)
                {
                    var ans = studentAnswers.FirstOrDefault(a => a.ControlQuestionId == q.Id);
                    if (ans == null)
                    {
                        questionsPassed = false;
                        break;
                    }

                    if (q.Type == QuestionType.OpenEnded)
                    {
                        // Развернутый ответ: если требует проверки, проверяем флаг IsReviewedByTeacher
                        if (lecture.RequiresManualReview && (!ans.IsReviewedByTeacher || !ans.IsCorrect))
                        {
                            questionsPassed = false;
                            break;
                        }
                    }
                    else
                    {
                        if (!ans.IsCorrect)
                        {
                            questionsPassed = false;
                            break;
                        }
                    }
                }
            }
        }
        progress.IsControlQuestionsPassed = questionsPassed;

        // 2. Проверяем тест к лекции (только если тест существует, активен и в нем есть вопросы или попытки)
        bool testPassed = true;
        if (lecture.Test != null && lecture.Test.IsActive)
        {
            bool hasQuestions = lecture.Test.Questions.Any();
            bool hasAttempts = await _db.TestAttempts.AnyAsync(a => a.TestId == lecture.Test.Id);

            if (hasQuestions || hasAttempts)
            {
                var successfulAttempt = await _db.TestAttempts
                    .AnyAsync(a => a.StudentId == studentId && a.TestId == lecture.Test.Id && a.IsPassed);

                testPassed = successfulAttempt;
            }
        }
        progress.IsTestPassed = testPassed;

        // 3. Проверяем сдачу отчета
        bool reportApproved = true;
        if (lecture.RequiresReportSubmission)
        {
            var approvedSubmission = await _db.StudentReportSubmissions
                .Where(s => s.LectureId == lectureId && s.StudentId == studentId && s.Status == ReportSubmissionStatus.Approved)
                .OrderByDescending(s => s.ReviewedAt ?? s.SubmittedAt)
                .FirstOrDefaultAsync();

            reportApproved = (approvedSubmission != null);
            progress.IsReportApproved = reportApproved;
            progress.ReportGrade = approvedSubmission?.Grade;
        }
        else
        {
            progress.IsReportApproved = true;
        }

        // 4. Итоговое завершение лекции
        bool wasCompleted = progress.IsCompleted;
        progress.IsCompleted = progress.IsControlQuestionsPassed && progress.IsTestPassed && progress.IsReportApproved;

        if (progress.IsCompleted && !wasCompleted)
        {
            progress.CompletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return progress.IsCompleted;
    }

    public async Task<bool> ManuallyUnlockLectureAsync(int studentId, int lectureId, string reason, int teacherUserId, string? ipAddress)
    {
        var lecture = await _db.Lectures.FindAsync(lectureId);
        var student = await _db.Users.FindAsync(studentId);
        if (lecture == null || student == null) return false;

        var progress = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == studentId && lp.LectureId == lectureId);

        if (progress == null)
        {
            progress = new LectureProgress
            {
                StudentId = studentId,
                LectureId = lectureId,
                FirstAccessedAt = DateTime.UtcNow
            };
            _db.LectureProgresses.Add(progress);
        }

        progress.IsManuallyUnlocked = true;
        progress.ManualUnlockReason = reason;
        progress.ManuallyUnlockedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            teacherUserId,
            "MANUAL_UNLOCK",
            $"Ручное открытие лекции '{lecture.Title}' (ID: {lecture.Id}) студенту '{student.FullName}' ({student.Username}). Причина: {reason}",
            student.FullName,
            lecture.Title,
            ipAddress
        );

        return true;
    }
}
