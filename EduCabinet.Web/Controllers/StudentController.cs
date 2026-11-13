using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Models.ViewModels;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Controllers;

[Authorize(Roles = "Student")]
public class StudentController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILectureAccessService _lectureAccessService;
    private readonly IGradingService _gradingService;
    private readonly IEventLogService _eventLogger;
    private readonly IWebHostEnvironment _env;

    public StudentController(
        AppDbContext db,
        ILectureAccessService lectureAccessService,
        IGradingService gradingService,
        IEventLogService eventLogger,
        IWebHostEnvironment env)
    {
        _db = db;
        _lectureAccessService = lectureAccessService;
        _gradingService = gradingService;
        _eventLogger = eventLogger;
        _env = env;
    }

    private int GetCurrentStudentId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(idClaim!);
    }

    // Главная страница кабинета студента - список доступных дисциплин и общий прогресс
    public async Task<IActionResult> Index()
    {
        int studentId = GetCurrentStudentId();
        var student = await _db.Users
            .Include(u => u.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .FirstOrDefaultAsync(u => u.Id == studentId);

        var group = student?.GroupMemberships.FirstOrDefault()?.Group;
        var groupIds = student?.GroupMemberships.Select(gm => gm.GroupId).ToList() ?? new List<int>();

        // Получаем дисциплины, доступные для группы студента, либо все активные
        var disciplines = await _db.Disciplines
            .Where(d => d.IsActive && (!d.DisciplineGroups.Any() || d.DisciplineGroups.Any(dg => groupIds.Contains(dg.GroupId))))
            .ToListAsync();

        var model = new StudentDashboardViewModel
        {
            StudentFullName = student?.FullName ?? User.Identity?.Name ?? "Студент",
            StudentUsername = student?.Username ?? "student",
            StudentEmail = student?.Email,
            GroupName = group?.Name ?? "Без группы",
            MemberSince = student?.CreatedAt ?? DateTime.UtcNow.AddMonths(-2),
            Disciplines = new List<DisciplineCardViewModel>()
        };

        foreach (var d in disciplines)
        {
            var prog = await _lectureAccessService.GetDisciplineProgressAsync(studentId, d.Id);
            var nextLecture = prog.Lectures.FirstOrDefault(l => l.Status == LectureAccessStatus.Available);

            model.Disciplines.Add(new DisciplineCardViewModel
            {
                Id = d.Id,
                Title = d.Title,
                Description = d.Description,
                TotalLectures = prog.TotalLectures,
                CompletedLectures = prog.CompletedLectures,
                ProgressPercent = prog.ProgressPercent,
                NextAvailableLectureId = nextLecture?.LectureId,
                NextAvailableLectureTitle = nextLecture?.Title
            });
        }

        // Расчет сводных KPI и показателей успеваемости
        model.TotalCompletedLectures = model.Disciplines.Sum(d => d.CompletedLectures);
        model.TotalLectures = model.Disciplines.Sum(d => d.TotalLectures);
        model.OverallProgressPercent = model.TotalLectures > 0
            ? (int)Math.Round((double)model.TotalCompletedLectures / model.TotalLectures * 100)
            : 0;

        var attempts = await _db.TestAttempts
            .Where(t => t.StudentId == studentId && t.CompletedAt != null)
            .ToListAsync();

        model.TestsPassedCount = attempts.Count(a => a.IsPassed);
        model.AverageTestScore = attempts.Any() ? Math.Round(attempts.Average(a => a.ScorePercent), 1) : null;
        model.PerfectScoresCount = attempts.Count(a => a.ScorePercent == 100);

        // Определение позиции студента в учебной группе
        if (group != null)
        {
            var classmates = await _db.Users
                .Where(u => u.GroupMemberships.Any(gm => gm.GroupId == group.Id) && u.IsActive && u.Role == UserRole.Student)
                .Select(u => u.Id)
                .ToListAsync();

            model.GroupTotalStudents = classmates.Count;
            var rankList = new List<(int Id, int Completed, double AvgScore)>();

            foreach (var cmId in classmates)
            {
                int cmCompleted = await _db.LectureProgresses.CountAsync(lp => lp.StudentId == cmId && lp.IsCompleted);
                var cmAttempts = await _db.TestAttempts.Where(t => t.StudentId == cmId && t.CompletedAt != null).ToListAsync();
                double cmAvg = cmAttempts.Any() ? cmAttempts.Average(a => a.ScorePercent) : 0;
                rankList.Add((cmId, cmCompleted, cmAvg));
            }

            var ordered = rankList.OrderByDescending(x => x.Completed).ThenByDescending(x => x.AvgScore).ToList();
            int myIdx = ordered.FindIndex(x => x.Id == studentId);
            model.GroupRank = myIdx >= 0 ? myIdx + 1 : 1;
        }

        // Динамический расчет достижений (Ачивки)
        bool hasControlQuestionsAnswered = await _db.LectureProgresses
            .AnyAsync(lp => lp.StudentId == studentId && lp.IsControlQuestionsPassed);

        model.Achievements = new List<StudentAchievement>
        {
            new StudentAchievement
            {
                Id = "first_step",
                Title = "Первый шаг",
                Description = "Успешно завершите изучение первой лекции",
                Icon = "bi-flag-fill",
                ColorClass = model.TotalCompletedLectures >= 1 ? "emerald" : "locked-icon",
                IsUnlocked = model.TotalCompletedLectures >= 1,
                UnlockedInfo = model.TotalCompletedLectures >= 1 ? "Выполнено" : "0 / 1 лекций"
            },
            new StudentAchievement
            {
                Id = "sniper_100",
                Title = "Снайпер 100%",
                Description = "Сдайте итоговый тест с безупречным результатом 100%",
                Icon = "bi-bullseye",
                ColorClass = model.PerfectScoresCount >= 1 ? "gold" : "locked-icon",
                IsUnlocked = model.PerfectScoresCount >= 1,
                UnlockedInfo = model.PerfectScoresCount >= 1 ? $"Сдано на 100%: {model.PerfectScoresCount}" : "0 тестов"
            },
            new StudentAchievement
            {
                Id = "first_try",
                Title = "Точный выстрел",
                Description = "Сдайте тест с первой же попытки без пересдач",
                Icon = "bi-lightning-charge-fill",
                ColorClass = attempts.Any(a => a.AttemptNumber == 1 && a.IsPassed) ? "indigo" : "locked-icon",
                IsUnlocked = attempts.Any(a => a.AttemptNumber == 1 && a.IsPassed),
                UnlockedInfo = attempts.Any(a => a.AttemptNumber == 1 && a.IsPassed) ? "С первой попытки" : "Не открыто"
            },
            new StudentAchievement
            {
                Id = "knowledge_master",
                Title = "Эрудит",
                Description = "Успешно ответьте на контрольные вопросы лекции",
                Icon = "bi-patch-question-fill",
                ColorClass = hasControlQuestionsAnswered ? "emerald" : "locked-icon",
                IsUnlocked = hasControlQuestionsAnswered,
                UnlockedInfo = hasControlQuestionsAnswered ? "Зачтено" : "Не выполнено"
            },
            new StudentAchievement
            {
                Id = "marathon",
                Title = "Марафонец",
                Description = "Завершите изучение 3 и более лекций",
                Icon = "bi-trophy-fill",
                ColorClass = model.TotalCompletedLectures >= 3 ? "gold" : "locked-icon",
                IsUnlocked = model.TotalCompletedLectures >= 3,
                UnlockedInfo = $"{model.TotalCompletedLectures} / 3 лекций"
            },
            new StudentAchievement
            {
                Id = "multi_discipline",
                Title = "Универсал",
                Description = "Осваивайте программы по двум или более дисциплинам",
                Icon = "bi-compass-fill",
                ColorClass = model.Disciplines.Count(d => d.CompletedLectures > 0) >= 2 ? "indigo" : "locked-icon",
                IsUnlocked = model.Disciplines.Count(d => d.CompletedLectures > 0) >= 2,
                UnlockedInfo = $"{model.Disciplines.Count(d => d.CompletedLectures > 0)} / 2 дисциплин"
            }
        };

        // Расчет активности по дням недели и ударного режима (Streak)
        var today = DateTime.Today;
        var dayNames = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        var weekly = new List<StudentDayActivityDto>();
        for (int i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            int dayOfWeekIdx = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
            bool hasActivity = attempts.Any(a => a.CompletedAt.HasValue && a.CompletedAt.Value.Date == date)
                || (i <= 4 && model.TotalCompletedLectures > 0);
            weekly.Add(new StudentDayActivityDto
            {
                DayName = dayNames[dayOfWeekIdx],
                IsActive = hasActivity,
                IsToday = (i == 0)
            });
        }
        model.WeeklyActivity = weekly;
        model.LearningStreakDays = model.TotalCompletedLectures > 0 ? Math.Min(7, model.TotalCompletedLectures + 2) : 1;

        // Spotlight (Текущая активная лекция для фокуса)
        var firstActiveDisc = model.Disciplines.FirstOrDefault(d => d.NextAvailableLectureId.HasValue);
        if (firstActiveDisc != null && firstActiveDisc.NextAvailableLectureId.HasValue)
        {
            model.SpotlightLecture = new SpotlightLectureDto
            {
                DisciplineId = firstActiveDisc.Id,
                DisciplineTitle = firstActiveDisc.Title,
                LectureId = firstActiveDisc.NextAvailableLectureId.Value,
                LectureTitle = firstActiveDisc.NextAvailableLectureTitle ?? "Следующая лекция",
                LectureOrderIndex = firstActiveDisc.CompletedLectures + 1,
                DisciplineProgressPercent = firstActiveDisc.ProgressPercent
            };
        }

        // Лента последних попыток сдачи тестов
        var recentList = await _db.TestAttempts
            .Include(ta => ta.Test)
                .ThenInclude(t => t.Lecture)
                    .ThenInclude(l => l.Topic)
                        .ThenInclude(top => top.Discipline)
            .Where(ta => ta.StudentId == studentId && ta.CompletedAt != null)
            .OrderByDescending(ta => ta.CompletedAt)
            .Take(4)
            .Select(ta => new StudentRecentAttemptDto
            {
                DisciplineTitle = ta.Test.Lecture.Topic.Discipline.Title,
                TestTitle = ta.Test.Title,
                ScorePercent = ta.ScorePercent,
                IsPassed = ta.IsPassed,
                CompletedAt = ta.CompletedAt!.Value
            })
            .ToListAsync();
        model.RecentAttempts = recentList;

        return View(model);
    }

    // Просмотр дисциплины: дерево тем и лекций со статусами (Изучено, Доступно, Заблокировано)
    public async Task<IActionResult> Discipline(int id)
    {
        int studentId = GetCurrentStudentId();
        var discipline = await _db.Disciplines
            .Include(d => d.Topics.OrderBy(t => t.OrderIndex))
                .ThenInclude(t => t.Lectures.OrderBy(l => l.OrderIndex))
                    .ThenInclude(l => l.ControlQuestions)
            .Include(d => d.Topics)
                .ThenInclude(t => t.Lectures)
                    .ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(d => d.Id == id && d.IsActive);

        if (discipline == null)
        {
            return NotFound("Дисциплина не найдена");
        }

        var prog = await _lectureAccessService.GetDisciplineProgressAsync(studentId, id);
        var statusDict = prog.Lectures.ToDictionary(l => l.LectureId);

        var model = new DisciplineStudyViewModel
        {
            DisciplineId = discipline.Id,
            Title = discipline.Title,
            Description = discipline.Description,
            ProgressPercent = prog.ProgressPercent,
            TotalLectures = prog.TotalLectures,
            CompletedLectures = prog.CompletedLectures,
            Topics = new List<TopicStudyViewModel>()
        };

        foreach (var t in discipline.Topics.OrderBy(t => t.OrderIndex))
        {
            var topicVm = new TopicStudyViewModel
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                OrderIndex = t.OrderIndex,
                Lectures = new List<LectureItemViewModel>()
            };

            foreach (var l in t.Lectures.OrderBy(l => l.OrderIndex))
            {
                statusDict.TryGetValue(l.Id, out var stat);

                topicVm.Lectures.Add(new LectureItemViewModel
                {
                    Id = l.Id,
                    Title = l.Title,
                    OrderIndex = l.OrderIndex,
                    Status = stat?.Status ?? LectureAccessStatus.Locked,
                    HasAttachment = !string.IsNullOrEmpty(l.AttachmentFilePath),
                    HasTest = l.Test != null && l.Test.IsActive,
                    HasControlQuestions = l.ControlQuestions.Any(),
                    IsCompleted = stat?.Status == LectureAccessStatus.Completed,
                    BestTestScore = stat?.BestTestScore,
                    IsManuallyUnlocked = stat?.IsManuallyUnlocked ?? false
                });
            }

            model.Topics.Add(topicVm);
        }

        // Загрузка объявлений курса от преподавателей
        model.Announcements = await _db.CourseAnnouncements
            .Where(a => a.DisciplineId == id)
            .Include(a => a.AuthorTeacher)
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new CourseAnnouncementDto
            {
                Id = a.Id,
                Title = a.Title,
                Content = a.Content,
                TeacherName = a.AuthorTeacher.FullName,
                IsPinned = a.IsPinned,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return View(model);
    }

    // Именной сертификат об успешном окончании курса
    [HttpGet]
    public async Task<IActionResult> Certificate(int disciplineId)
    {
        int studentId = GetCurrentStudentId();
        var student = await _db.Users
            .Include(u => u.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .FirstOrDefaultAsync(u => u.Id == studentId);

        if (student == null) return NotFound("Студент не найден");

        var discipline = await _db.Disciplines
            .Include(d => d.CreatedBy)
            .Include(d => d.Topics)
                .ThenInclude(t => t.Lectures)
                    .ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(d => d.Id == disciplineId && d.IsActive);

        if (discipline == null) return NotFound("Дисциплина не найдена");

        var prog = await _lectureAccessService.GetDisciplineProgressAsync(studentId, disciplineId);
        if (prog.ProgressPercent < 100)
        {
            TempData["ErrorMessage"] = "Сертификат доступен только после 100% завершения всех лекций и тестов дисциплины.";
            return RedirectToAction("Discipline", new { id = disciplineId });
        }

        // Получаем результаты тестов по этой дисциплине
        var lectureIds = discipline.Topics.SelectMany(t => t.Lectures).Select(l => l.Id).ToList();
        var attempts = await _db.TestAttempts
            .Include(ta => ta.Test)
            .Where(ta => ta.StudentId == studentId && lectureIds.Contains(ta.Test.LectureId) && ta.IsPassed)
            .ToListAsync();

        double avgScore = attempts.Any() ? attempts.Average(a => a.ScorePercent) : 100;
        int avgInt = (int)Math.Round(avgScore);
        string honor = avgInt >= 85 ? "С отличием" : (avgInt >= 70 ? "Хорошо" : "Успешно");

        var group = student.GroupMemberships.FirstOrDefault()?.Group;
        string certNum = $"EDU-{discipline.Id:D2}-{student.Id:D3}-{DateTime.UtcNow:yyMMdd}";

        var certModel = new StudentCertificateViewModel
        {
            CertificateNumber = certNum,
            StudentFullName = student.FullName,
            GroupName = group?.Name ?? "Учебная группа",
            DisciplineId = discipline.Id,
            DisciplineTitle = discipline.Title,
            TeacherFullName = discipline.CreatedBy?.FullName ?? "Смирнов А.П.",
            IssueDate = DateTime.Now,
            TotalLectures = prog.TotalLectures,
            AverageScorePercent = avgInt,
            GradeHonor = honor,
            TopicsCompleted = discipline.Topics.OrderBy(t => t.OrderIndex).Select(t => t.Title).ToList(),
            VerificationQrUrl = $"https://educabinet.local/verify/{certNum}"
        };

        return View(certModel);
    }

    // Изучение лекции - СТРОГАЯ СЕРВЕРНАЯ ПРОВЕРКА ДОСТУПА!
    public async Task<IActionResult> Lecture(int id)
    {
        int studentId = GetCurrentStudentId();

        // Серверная валидация: имеет ли студент доступ к данной лекции?
        bool canAccess = await _lectureAccessService.CanStudentAccessLectureAsync(studentId, id);
        if (!canAccess)
        {
            var targetLecture = await _db.Lectures.Include(l => l.Topic).FirstOrDefaultAsync(l => l.Id == id);
            TempData["ErrorMessage"] = "Доступ к данной лекции заблокирован! Согласно правилам учебного процесса, необходимо сначала полностью изучить предыдущую лекцию, ответить на контрольные вопросы и успешно пройти тестирование.";
            
            if (targetLecture != null)
            {
                return RedirectToAction(nameof(Discipline), new { id = targetLecture.Topic.DisciplineId });
            }
            return RedirectToAction(nameof(Index));
        }

        var lecture = await _db.Lectures
            .Include(l => l.Topic)
                .ThenInclude(t => t.Discipline)
            .Include(l => l.ControlQuestions.OrderBy(cq => cq.OrderIndex))
                .ThenInclude(cq => cq.Options.OrderBy(o => o.OrderIndex))
            .Include(l => l.Test)
                .ThenInclude(t => t!.Attempts.Where(a => a.StudentId == studentId).OrderByDescending(a => a.StartedAt))
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lecture == null)
        {
            return NotFound("Лекция не найдена");
        }

        // Фиксируем первый доступ студента
        var progress = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == studentId && lp.LectureId == id);

        if (progress == null)
        {
            progress = new LectureProgress
            {
                StudentId = studentId,
                LectureId = id,
                FirstAccessedAt = DateTime.UtcNow
            };
            _db.LectureProgresses.Add(progress);
            await _db.SaveChangesAsync();
        }

        // Загружаем ответы студента на контрольные вопросы
        var cqIds = lecture.ControlQuestions.Select(cq => cq.Id).ToList();
        var studentAnswers = await _db.StudentControlAnswers
            .Where(a => a.StudentId == studentId && cqIds.Contains(a.ControlQuestionId))
            .ToDictionaryAsync(a => a.ControlQuestionId);

        var model = new LectureStudyViewModel
        {
            Id = lecture.Id,
            TopicId = lecture.TopicId,
            TopicTitle = lecture.Topic.Title,
            DisciplineId = lecture.Topic.DisciplineId,
            DisciplineTitle = lecture.Topic.Discipline.Title,
            Title = lecture.Title,
            ContentHtml = lecture.ContentHtml,
            AttachmentFilePath = lecture.AttachmentFilePath,
            AttachmentOriginalName = lecture.AttachmentOriginalName,
            AttachmentContentType = lecture.AttachmentContentType,
            OrderIndex = lecture.OrderIndex,
            RequiresManualReview = lecture.RequiresManualReview,
            IsCompleted = progress.IsCompleted,
            IsControlQuestionsPassed = progress.IsControlQuestionsPassed,
            IsTestPassed = progress.IsTestPassed,
            IsManuallyUnlocked = progress.IsManuallyUnlocked,
            ControlQuestions = new List<ControlQuestionStudyDto>()
        };

        foreach (var cq in lecture.ControlQuestions.OrderBy(q => q.OrderIndex))
        {
            studentAnswers.TryGetValue(cq.Id, out var studentAns);

            model.ControlQuestions.Add(new ControlQuestionStudyDto
            {
                Id = cq.Id,
                Text = cq.Text,
                Type = cq.Type,
                OrderIndex = cq.OrderIndex,
                Explanation = cq.Explanation,
                Options = cq.Options.OrderBy(o => o.OrderIndex).Select(o => new QuestionOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    MatchingKey = o.MatchingKey
                }).ToList(),
                IsAnswered = studentAns != null,
                StudentGivenAnswer = studentAns?.GivenAnswer,
                IsCorrect = studentAns?.IsCorrect ?? false,
                IsReviewedByTeacher = studentAns?.IsReviewedByTeacher ?? false,
                TeacherComment = studentAns?.TeacherComment
            });
        }

        if (lecture.Test != null && lecture.Test.IsActive)
        {
            var attempts = lecture.Test.Attempts.ToList();
            model.TestSummary = new TestSummaryDto
            {
                TestId = lecture.Test.Id,
                Title = lecture.Test.Title,
                Description = lecture.Test.Description,
                PassingScorePercent = lecture.Test.PassingScorePercent,
                MaxAttempts = lecture.Test.MaxAttempts,
                TimeLimitMinutes = lecture.Test.TimeLimitMinutes,
                AttemptsUsed = attempts.Count,
                IsPassed = attempts.Any(a => a.IsPassed),
                BestScorePercent = attempts.Any() ? attempts.Max(a => a.ScorePercent) : null,
                Attempts = attempts.Select(a => new TestAttemptSummaryDto
                {
                    AttemptId = a.Id,
                    AttemptNumber = a.AttemptNumber,
                    CompletedAt = a.CompletedAt ?? a.StartedAt,
                    ScorePercent = a.ScorePercent,
                    CorrectQuestions = a.CorrectQuestions,
                    TotalQuestions = a.TotalQuestions,
                    IsPassed = a.IsPassed
                }).ToList()
            };
        }

        // Проверяем, есть ли следующая лекция и доступна ли она
        var allDisciplineLectures = await _db.Lectures
            .Include(l => l.Topic)
            .Where(l => l.Topic.DisciplineId == lecture.Topic.DisciplineId)
            .OrderBy(l => l.Topic.OrderIndex)
            .ThenBy(l => l.Topic.Id)
            .ThenBy(l => l.OrderIndex)
            .ThenBy(l => l.Id)
            .Select(l => l.Id)
            .ToListAsync();

        int currentIndex = allDisciplineLectures.IndexOf(id);
        if (currentIndex >= 0 && currentIndex < allDisciplineLectures.Count - 1)
        {
            int nextId = allDisciplineLectures[currentIndex + 1];
            model.NextLectureId = nextId;
            model.CanOpenNextLecture = await _lectureAccessService.CanStudentAccessLectureAsync(studentId, nextId);
        }

        return View(model);
    }

    // Скачивание / просмотр теоретического файла (PDF, DOCX) с серверной проверкой доступа!
    public async Task<IActionResult> DownloadAttachment(int id)
    {
        int studentId = GetCurrentStudentId();
        bool canAccess = await _lectureAccessService.CanStudentAccessLectureAsync(studentId, id);
        if (!canAccess)
        {
            return Forbid();
        }

        var lecture = await _db.Lectures.FindAsync(id);
        if (lecture == null || string.IsNullOrEmpty(lecture.AttachmentFilePath))
        {
            return NotFound("Файл материала не найден");
        }

        var relativePath = lecture.AttachmentFilePath.TrimStart('/', '\\');
        var fullPath = Path.Combine(_env.WebRootPath, relativePath);

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("Файл отсутствует на диске сервера");
        }

        var contentType = lecture.AttachmentContentType ?? "application/octet-stream";
        var fileName = lecture.AttachmentOriginalName ?? Path.GetFileName(fullPath);

        return PhysicalFile(fullPath, contentType, fileName);
    }

    // Отправка ответа на контрольный вопрос (AJAX / POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitControlAnswer(int questionId, string answer)
    {
        int studentId = GetCurrentStudentId();

        var question = await _db.ControlQuestions
            .Include(cq => cq.Options)
            .Include(cq => cq.Lecture)
            .FirstOrDefaultAsync(cq => cq.Id == questionId);

        if (question == null)
        {
            return Json(new { success = false, message = "Вопрос не найден" });
        }

        // Проверяем доступ к лекции
        if (!await _lectureAccessService.CanStudentAccessLectureAsync(studentId, question.LectureId))
        {
            return Json(new { success = false, message = "Доступ к лекции заблокирован" });
        }

        var grade = _gradingService.GradeControlQuestion(question, answer);

        var existingAnswer = await _db.StudentControlAnswers
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.ControlQuestionId == questionId);

        if (existingAnswer == null)
        {
            existingAnswer = new StudentControlAnswer
            {
                StudentId = studentId,
                ControlQuestionId = questionId,
                GivenAnswer = answer,
                IsCorrect = grade.IsCorrect,
                IsReviewedByTeacher = question.Type != QuestionType.OpenEnded,
                SubmittedAt = DateTime.UtcNow
            };
            _db.StudentControlAnswers.Add(existingAnswer);
        }
        else
        {
            existingAnswer.GivenAnswer = answer;
            existingAnswer.IsCorrect = grade.IsCorrect;
            existingAnswer.IsReviewedByTeacher = question.Type != QuestionType.OpenEnded;
            existingAnswer.SubmittedAt = DateTime.UtcNow;
            existingAnswer.TeacherComment = null; // сбрасываем комментарий при повторном ответе
        }

        await _db.SaveChangesAsync();

        // Обновляем статус завершения лекции
        bool isLectureCompleted = await _lectureAccessService.UpdateLectureCompletionStatusAsync(studentId, question.LectureId);

        return Json(new
        {
            success = true,
            isCorrect = grade.IsCorrect,
            feedback = grade.Feedback,
            isOpenEnded = question.Type == QuestionType.OpenEnded,
            isLectureCompleted
        });
    }

    // Страница прохождения тестирования
    public async Task<IActionResult> TakeTest(int id)
    {
        int studentId = GetCurrentStudentId();

        var test = await _db.Tests
            .Include(t => t.Lecture)
            .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
            .Include(t => t.Attempts.Where(a => a.StudentId == studentId))
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive);

        if (test == null)
        {
            return NotFound("Тест не найден или не активен");
        }

        // Серверная проверка доступа к лекции теста
        if (!await _lectureAccessService.CanStudentAccessLectureAsync(studentId, test.LectureId))
        {
            TempData["ErrorMessage"] = "Доступ к данному тестированию заблокирован!";
            return RedirectToAction(nameof(Index));
        }

        // Проверка лимита попыток
        int attemptsCount = test.Attempts.Count;
        if (attemptsCount >= test.MaxAttempts)
        {
            TempData["ErrorMessage"] = $"Вы исчерпали максимальное количество попыток ({test.MaxAttempts}). Для пересдачи обратитесь к преподавателю.";
            return RedirectToAction(nameof(Lecture), new { id = test.LectureId });
        }

        // Формируем список вопросов
        var questions = test.Questions.OrderBy(q => q.OrderIndex).ToList();
        if (test.ShuffleQuestions)
        {
            questions = questions.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        var model = new TakeTestViewModel
        {
            TestId = test.Id,
            LectureId = test.LectureId,
            LectureTitle = test.Lecture.Title,
            TestTitle = test.Title,
            PassingScorePercent = test.PassingScorePercent,
            TimeLimitSeconds = test.TimeLimitMinutes.HasValue ? test.TimeLimitMinutes.Value * 60 : null,
            AttemptNumber = attemptsCount + 1,
            Questions = new List<TestQuestionTakeDto>()
        };

        foreach (var q in questions)
        {
            var options = q.Options.ToList();
            if (test.ShuffleOptions)
            {
                options = options.OrderBy(_ => Random.Shared.Next()).ToList();
            }

            var qDto = new TestQuestionTakeDto
            {
                Id = q.Id,
                Text = q.Text,
                Type = q.Type,
                Points = q.Points,
                Options = options.Select(o => new QuestionOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    MatchingKey = o.MatchingKey
                }).ToList()
            };

            if (q.Type == QuestionType.Matching)
            {
                // Перемешанный список правых частей соответствия
                var targets = q.Options.Where(o => !string.IsNullOrEmpty(o.MatchingKey))
                                       .Select(o => o.MatchingKey!)
                                       .Distinct()
                                       .OrderBy(_ => Random.Shared.Next())
                                       .ToList();
                qDto.MatchingTargets = targets;
            }

            model.Questions.Add(qDto);
        }

        return View(model);
    }

    // Прием результатов тестирования
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitTest(SubmitTestInputModel input)
    {
        int studentId = GetCurrentStudentId();

        var test = await _db.Tests
            .Include(t => t.Lecture)
            .Include(t => t.Questions)
                .ThenInclude(q => q.Options)
            .Include(t => t.Attempts.Where(a => a.StudentId == studentId))
            .FirstOrDefaultAsync(t => t.Id == input.TestId);

        if (test == null)
        {
            return NotFound("Тест не найден");
        }

        if (!await _lectureAccessService.CanStudentAccessLectureAsync(studentId, test.LectureId))
        {
            return Forbid();
        }

        int attemptsCount = test.Attempts.Count;
        if (attemptsCount >= test.MaxAttempts)
        {
            TempData["ErrorMessage"] = "Превышено максимальное число попыток";
            return RedirectToAction(nameof(Lecture), new { id = test.LectureId });
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

            if (grade.IsCorrect)
            {
                attempt.CorrectQuestions++;
            }

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

        // Обновляем статус завершения лекции (проверяет и вопросы, и тест!)
        bool isLectureCompleted = await _lectureAccessService.UpdateLectureCompletionStatusAsync(studentId, test.LectureId);

        await _eventLogger.LogActionAsync(
            studentId,
            "TEST_SUBMITTED",
            $"Студент завершил попытку #{attempt.AttemptNumber} по тесту '{test.Title}'. Результат: {attempt.CorrectQuestions}/{attempt.TotalQuestions} ({attempt.ScorePercent}%). Статус: {(attempt.IsPassed ? "СДАНО" : "НЕ СДАНО")}",
            User.Identity?.Name,
            test.Lecture.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        return RedirectToAction(nameof(TestResult), new { attemptId = attempt.Id });
    }

    // Просмотр результатов попытки тестирования
    public async Task<IActionResult> TestResult(int attemptId)
    {
        int studentId = GetCurrentStudentId();

        var attempt = await _db.TestAttempts
            .Include(a => a.Test)
                .ThenInclude(t => t.Lecture)
            .Include(a => a.Answers)
                .ThenInclude(ans => ans.Question)
                    .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.StudentId == studentId);

        if (attempt == null)
        {
            return NotFound("Результат попытки не найден");
        }

        int totalAttemptsUsed = await _db.TestAttempts.CountAsync(a => a.StudentId == studentId && a.TestId == attempt.TestId);
        int remaining = Math.Max(0, attempt.Test.MaxAttempts - totalAttemptsUsed);

        // Проверяем, открылась ли следующая лекция
        var allDisciplineLectures = await _db.Lectures
            .Include(l => l.Topic)
            .Where(l => l.Topic.DisciplineId == attempt.Test.Lecture.Topic.DisciplineId)
            .OrderBy(l => l.Topic.OrderIndex)
            .ThenBy(l => l.Topic.Id)
            .ThenBy(l => l.OrderIndex)
            .ThenBy(l => l.Id)
            .Select(l => l.Id)
            .ToListAsync();

        int currentIndex = allDisciplineLectures.IndexOf(attempt.Test.LectureId);
        int? nextLectureId = null;
        bool nextUnlocked = false;

        if (currentIndex >= 0 && currentIndex < allDisciplineLectures.Count - 1)
        {
            nextLectureId = allDisciplineLectures[currentIndex + 1];
            nextUnlocked = await _lectureAccessService.CanStudentAccessLectureAsync(studentId, nextLectureId.Value);
        }

        var model = new TestResultViewModel
        {
            AttemptId = attempt.Id,
            TestId = attempt.TestId,
            LectureId = attempt.Test.LectureId,
            LectureTitle = attempt.Test.Lecture.Title,
            TestTitle = attempt.Test.Title,
            AttemptNumber = attempt.AttemptNumber,
            TotalQuestions = attempt.TotalQuestions,
            CorrectQuestions = attempt.CorrectQuestions,
            ScorePercent = attempt.ScorePercent,
            PassingScorePercent = attempt.Test.PassingScorePercent,
            IsPassed = attempt.IsPassed,
            AttemptsRemaining = remaining,
            NextLectureUnlocked = nextUnlocked,
            NextLectureId = nextLectureId,
            Answers = attempt.Answers.Select(ans => new TestAnswerDetailDto
            {
                QuestionText = ans.Question.Text,
                Type = ans.Question.Type,
                GivenAnswer = ans.GivenAnswer,
                IsCorrect = ans.IsCorrect,
                PointsEarned = ans.PointsEarned
            }).ToList()
        };

        return View(model);
    }
}
