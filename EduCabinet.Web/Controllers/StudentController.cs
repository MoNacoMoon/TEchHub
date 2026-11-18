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
    private readonly INotificationService _notificationService;
    private readonly IDocumentContentExtractorService _documentExtractor;

    public StudentController(
        AppDbContext db,
        ILectureAccessService lectureAccessService,
        IGradingService gradingService,
        IEventLogService eventLogger,
        IWebHostEnvironment env,
        INotificationService notificationService,
        IDocumentContentExtractorService documentExtractor)
    {
        _db = db;
        _lectureAccessService = lectureAccessService;
        _gradingService = gradingService;
        _eventLogger = eventLogger;
        _env = env;
        _notificationService = notificationService;
        _documentExtractor = documentExtractor;
    }

    private int GetCurrentStudentId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idClaim, out int id) ? id : 0;
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

        // Расчет горящих дедлайнов и заданий студента
        var activeDisciplineIds = disciplines.Select(d => d.Id).ToList();
        var deadlineLectures = await _db.Lectures
            .Include(l => l.Topic)
                .ThenInclude(t => t.Discipline)
            .Where(l => activeDisciplineIds.Contains(l.Topic.DisciplineId) && l.RequiresReportSubmission && l.ReportDeadline != null)
            .ToListAsync();

        if (deadlineLectures.Any())
        {
            var deadlineLectureIds = deadlineLectures.Select(l => l.Id).ToList();
            var studentSubmissions = await _db.StudentReportSubmissions
                .Where(s => s.StudentId == studentId && deadlineLectureIds.Contains(s.LectureId))
                .ToListAsync();

            var studentLectureProgresses = await _db.LectureProgresses
                .Where(lp => lp.StudentId == studentId && deadlineLectureIds.Contains(lp.LectureId))
                .ToListAsync();

            var now = DateTime.UtcNow;
            var deadlineItems = new List<StudentDeadlineViewModel>();

            foreach (var lec in deadlineLectures)
            {
                var prog = studentLectureProgresses.FirstOrDefault(p => p.LectureId == lec.Id);
                var sub = studentSubmissions.FirstOrDefault(s => s.LectureId == lec.Id);

                // Если уже зачтено - задание закрыто, скрываем из горящих
                if (prog?.IsReportApproved == true || sub?.Status == ReportSubmissionStatus.Approved)
                    continue;

                var deadline = lec.ReportDeadline!.Value;
                var timeLeft = deadline - now;
                bool isOverdue = timeLeft < TimeSpan.Zero;
                bool isUrgent = !isOverdue && timeLeft <= TimeSpan.FromHours(48);

                string statusBadgeText;
                string statusBadgeClass;

                if (isOverdue)
                {
                    var over = TimeSpan.Zero - timeLeft;
                    string dur = over.TotalDays >= 1 ? $"{(int)over.TotalDays} дн." : $"{(int)over.TotalHours} ч.";
                    statusBadgeText = $"Просрочен на {dur}";
                    statusBadgeClass = "bg-danger text-white";
                }
                else if (isUrgent)
                {
                    string dur = timeLeft.TotalHours >= 1 ? $"{(int)timeLeft.TotalHours} ч." : $"{(int)timeLeft.TotalMinutes} мин.";
                    statusBadgeText = $"🔥 Осталось: {dur}";
                    statusBadgeClass = "bg-warning text-dark";
                }
                else
                {
                    statusBadgeText = $"⏳ До {deadline.ToLocalTime():dd.MM.yyyy HH:mm}";
                    statusBadgeClass = "bg-secondary-subtle text-secondary";
                }

                string subStatusText = "Не сдан";
                string subBadgeClass = "badge bg-secondary";

                if (sub != null)
                {
                    if (sub.Status == ReportSubmissionStatus.Pending)
                    {
                        subStatusText = "На проверке";
                        subBadgeClass = "badge bg-warning text-dark";
                    }
                    else if (sub.Status == ReportSubmissionStatus.NeedsRevision)
                    {
                        subStatusText = "На доработке";
                        subBadgeClass = "badge bg-danger";
                    }
                }

                deadlineItems.Add(new StudentDeadlineViewModel
                {
                    LectureId = lec.Id,
                    LectureTitle = lec.Title,
                    DisciplineTitle = lec.Topic.Discipline.Title,
                    Deadline = deadline,
                    TimeLeft = timeLeft,
                    IsOverdue = isOverdue,
                    IsUrgent = isUrgent,
                    StatusBadgeText = statusBadgeText,
                    StatusBadgeClass = statusBadgeClass,
                    SubmissionStatusText = subStatusText,
                    SubmissionBadgeClass = subBadgeClass
                });
            }

            model.Deadlines = deadlineItems
                .OrderByDescending(d => d.IsOverdue)
                .ThenBy(d => d.Deadline)
                .Take(6)
                .ToList();
        }

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
                        .ThenInclude(t => t!.Questions)
            .FirstOrDefaultAsync(d => d.Id == id && d.IsActive);

        if (discipline == null)
        {
            return NotFound("Дисциплина не найдена");
        }

        var prog = await _lectureAccessService.GetDisciplineProgressAsync(studentId, id);
        var statusDict = prog.Lectures.ToDictionary(l => l.LectureId);

        var allDisciplineLectureIds = discipline.Topics.SelectMany(t => t.Lectures.Select(l => l.Id)).ToList();
        var studentReports = await _db.StudentReportSubmissions
            .Where(r => r.StudentId == studentId && allDisciplineLectureIds.Contains(r.LectureId))
            .ToDictionaryAsync(r => r.LectureId);

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
                studentReports.TryGetValue(l.Id, out var rep);

                topicVm.Lectures.Add(new LectureItemViewModel
                {
                    Id = l.Id,
                    Title = l.Title,
                    OrderIndex = l.OrderIndex,
                    Status = stat?.Status ?? LectureAccessStatus.Locked,
                    HasAttachment = !string.IsNullOrEmpty(l.AttachmentFilePath),
                    AttachmentOriginalName = l.AttachmentOriginalName,
                    HasTest = l.Test != null && l.Test.IsActive && l.Test.Questions.Any(),
                    HasControlQuestions = l.ControlQuestions.Any(),
                    RequiresReportSubmission = l.RequiresReportSubmission,
                    IsReportApproved = stat?.IsReportApproved ?? false,
                    ReportStatus = rep?.Status,
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
                .ThenInclude(t => t!.Questions)
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

        StudentReportSubmissionDto? reportSubmissionDto = null;
        if (lecture.RequiresReportSubmission)
        {
            var submission = await _db.StudentReportSubmissions
                .Include(s => s.ReviewedByTeacher)
                .Where(s => s.LectureId == id && s.StudentId == studentId)
                .OrderByDescending(s => s.SubmittedAt)
                .FirstOrDefaultAsync();

            if (submission != null)
            {
                reportSubmissionDto = new StudentReportSubmissionDto
                {
                    Id = submission.Id,
                    LectureId = submission.LectureId,
                    OriginalFileName = submission.OriginalFileName,
                    FileSizeBytes = submission.FileSizeBytes,
                    StudentComment = submission.StudentComment,
                    SubmittedAt = submission.SubmittedAt,
                    Status = submission.Status,
                    Grade = submission.Grade,
                    TeacherFeedback = submission.TeacherFeedback,
                    ReviewedAt = submission.ReviewedAt,
                    ReviewedByTeacherName = submission.ReviewedByTeacher?.FullName,
                    ReviewedFilePath = submission.ReviewedFilePath,
                    ReviewedFileName = submission.ReviewedFileName,
                    ReviewedFileSizeBytes = submission.ReviewedFileSizeBytes
                };
            }
        }

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
            RequiresReportSubmission = lecture.RequiresReportSubmission,
            ReportInstructions = lecture.ReportInstructions,
            MaxReportScore = lecture.MaxReportScore,
            IsReportApproved = progress.IsReportApproved,
            ReportGrade = progress.ReportGrade,
            CurrentReportSubmission = reportSubmissionDto,
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
                ImageUrl = cq.ImageUrl,
                Options = cq.Options.OrderBy(o => o.OrderIndex).Select(o => new QuestionOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    MatchingKey = o.MatchingKey,
                    ImageUrl = o.ImageUrl
                }).ToList(),
                IsAnswered = studentAns != null,
                StudentGivenAnswer = studentAns?.GivenAnswer,
                IsCorrect = studentAns?.IsCorrect ?? false,
                IsReviewedByTeacher = studentAns?.IsReviewedByTeacher ?? false,
                TeacherComment = studentAns?.TeacherComment
            });
        }

        // Извлечение текстового содержимого прикрепленного документа/презентации (.docx, .pptx, .txt, .md)
        if (!string.IsNullOrEmpty(lecture.AttachmentFilePath))
        {
            var relativePath = lecture.AttachmentFilePath.TrimStart('/', '\\');
            var fullDiskPath = Path.Combine(_env.WebRootPath, relativePath);
            model.ExtractedDocumentHtml = _documentExtractor.ExtractContentHtml(fullDiskPath, lecture.AttachmentOriginalName);
        }

        if (lecture.Test != null && lecture.Test.IsActive && lecture.Test.Questions.Any())
        {
            var attempts = lecture.Test.Attempts.ToList();
            model.TestSummary = new TestSummaryDto
            {
                TestId = lecture.Test.Id,
                Title = lecture.Test.Title,
                Description = lecture.Test.Description,
                PassingScorePercent = lecture.Test.PassingScorePercent,
                TotalQuestions = lecture.Test.Questions.Count,
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

    // Подтверждение изучения лекции студентом (для тем без тестов, вопросов и отчетов)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteReadingLecture(int id)
    {
        int studentId = GetCurrentStudentId();
        bool canAccess = await _lectureAccessService.CanStudentAccessLectureAsync(studentId, id);
        if (!canAccess)
        {
            TempData["ErrorMessage"] = "У вас нет доступа к этой лекции!";
            return RedirectToAction(nameof(Index));
        }

        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .Include(l => l.ControlQuestions)
            .Include(l => l.Test)
                .ThenInclude(t => t!.Questions)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lecture == null)
        {
            return NotFound("Лекция не найдена");
        }

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
        }

        if (!lecture.ControlQuestions.Any())
        {
            progress.IsControlQuestionsPassed = true;
        }

        if (lecture.Test == null || !lecture.Test.IsActive || !lecture.Test.Questions.Any())
        {
            progress.IsTestPassed = true;
        }

        if (!lecture.RequiresReportSubmission)
        {
            progress.IsReportApproved = true;
        }

        bool wasCompleted = progress.IsCompleted;
        bool isAllCompleted = progress.IsControlQuestionsPassed && progress.IsTestPassed && progress.IsReportApproved;

        if (isAllCompleted)
        {
            progress.IsCompleted = true;
            if (!wasCompleted)
            {
                progress.CompletedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            // Проверяем следующую лекцию и отправляем уведомление об открытии доступа
            var allDisciplineLectures = await _db.Lectures
                .Include(l => l.Topic)
                .Where(l => l.Topic.DisciplineId == lecture.Topic.DisciplineId)
                .OrderBy(l => l.Topic.OrderIndex)
                .ThenBy(l => l.Topic.Id)
                .ThenBy(l => l.OrderIndex)
                .ThenBy(l => l.Id)
                .ToListAsync();

            int currentIndex = allDisciplineLectures.FindIndex(l => l.Id == id);
            if (currentIndex >= 0 && currentIndex < allDisciplineLectures.Count - 1)
            {
                var nextLecture = allDisciplineLectures[currentIndex + 1];
                await _notificationService.CreateNotificationAsync(
                    studentId,
                    "Новая лекция доступна! 🎉",
                    $"Вы успешно завершили изучение лекции «{lecture.Title}». Вам открыт доступ к следующей лекции: «{nextLecture.Title}».",
                    NotificationType.Success,
                    $"/Student/Lecture/{nextLecture.Id}"
                );
            }

            TempData["SuccessMessage"] = "Поздравляем! Вы успешно завершили изучение лекции.";
        }
        else
        {
            await _db.SaveChangesAsync();
            TempData["InfoMessage"] = "Материал прочитан. Для завершения лекции выполните оставшиеся задания.";
        }

        return RedirectToAction(nameof(Lecture), new { id });
    }

    // Отправка отчета студентом (Лабораторная / Практическая работа)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReport(int lectureId, IFormFile? reportFile, string? studentComment)
    {
        int studentId = GetCurrentStudentId();
        bool canAccess = await _lectureAccessService.CanStudentAccessLectureAsync(studentId, lectureId);
        if (!canAccess)
        {
            TempData["ErrorMessage"] = "У вас нет доступа к этой лекции!";
            return RedirectToAction(nameof(Index));
        }

        var lecture = await _db.Lectures.Include(l => l.Topic).FirstOrDefaultAsync(l => l.Id == lectureId);
        if (lecture == null || !lecture.RequiresReportSubmission)
        {
            TempData["ErrorMessage"] = "Для данной лекции не требуется сдача отчёта.";
            return RedirectToAction(nameof(Lecture), new { id = lectureId });
        }

        if (reportFile == null || reportFile.Length == 0)
        {
            TempData["ErrorMessage"] = "Пожалуйста, выберите файл отчёта для отправки.";
            return RedirectToAction(nameof(Lecture), new { id = lectureId });
        }

        // Максимальный размер 50 МБ
        const long maxSizeBytes = 50 * 1024 * 1024;
        if (reportFile.Length > maxSizeBytes)
        {
            TempData["ErrorMessage"] = "Размер файла превышает допустимый предел 50 МБ.";
            return RedirectToAction(nameof(Lecture), new { id = lectureId });
        }

        var ext = Path.GetExtension(reportFile.FileName).ToLowerInvariant();
        var forbiddenExts = new[] { ".exe", ".bat", ".cmd", ".sh", ".com", ".msi", ".dll", ".so", ".scr", ".pif", ".vbs", ".js", ".php", ".asp", ".aspx" };
        if (forbiddenExts.Contains(ext))
        {
            TempData["ErrorMessage"] = $"Загрузка исполняемых файлов ({ext}) запрещена правилами безопасности.";
            return RedirectToAction(nameof(Lecture), new { id = lectureId });
        }

        var uploadsReportsDir = Path.Combine(_env.WebRootPath, "uploads", "reports");
        if (!Directory.Exists(uploadsReportsDir))
        {
            Directory.CreateDirectory(uploadsReportsDir);
        }

        var originalName = Path.GetFileName(reportFile.FileName);
        var uniqueFileName = $"rep_{Guid.NewGuid():N}_{originalName}";
        var fullPath = Path.Combine(uploadsReportsDir, uniqueFileName);

        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await reportFile.CopyToAsync(stream);
        }

        // Если запущено на постоянном томе Amvera (/data)
        if (Directory.Exists("/data"))
        {
            try
            {
                var persistentDir = "/data/uploads/reports";
                if (!Directory.Exists(persistentDir)) Directory.CreateDirectory(persistentDir);
                var pPath = Path.Combine(persistentDir, uniqueFileName);
                System.IO.File.Copy(fullPath, pPath, true);
            }
            catch { }
        }

        var existingSubmission = await _db.StudentReportSubmissions
            .FirstOrDefaultAsync(s => s.LectureId == lectureId && s.StudentId == studentId);

        if (existingSubmission == null)
        {
            existingSubmission = new StudentReportSubmission
            {
                LectureId = lectureId,
                StudentId = studentId,
                FilePath = $"/uploads/reports/{uniqueFileName}",
                OriginalFileName = originalName,
                FileSizeBytes = reportFile.Length,
                ContentType = reportFile.ContentType,
                StudentComment = studentComment?.Trim(),
                SubmittedAt = DateTime.UtcNow,
                Status = ReportSubmissionStatus.Pending
            };
            _db.StudentReportSubmissions.Add(existingSubmission);
        }
        else
        {
            existingSubmission.FilePath = $"/uploads/reports/{uniqueFileName}";
            existingSubmission.OriginalFileName = originalName;
            existingSubmission.FileSizeBytes = reportFile.Length;
            existingSubmission.ContentType = reportFile.ContentType;
            existingSubmission.StudentComment = studentComment?.Trim();
            existingSubmission.SubmittedAt = DateTime.UtcNow;
            existingSubmission.Status = ReportSubmissionStatus.Pending;
            existingSubmission.Grade = null;
            existingSubmission.TeacherFeedback = null;
            existingSubmission.ReviewedAt = null;
            existingSubmission.ReviewedByTeacherId = null;
        }

        await _db.SaveChangesAsync();

        // Обновляем прогресс
        await _lectureAccessService.UpdateLectureCompletionStatusAsync(studentId, lectureId);

        var student = await _db.Users.FindAsync(studentId);
        await _eventLogger.LogActionAsync(
            studentId,
            "REPORT_SUBMITTED",
            $"Студент '{student?.FullName}' отправил отчет по лекции '{lecture.Title}' ({originalName}, {reportFile.Length / 1024} КБ)",
            student?.FullName,
            lecture.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        // Интерактивное уведомление преподавателям курса
        try
        {
            await _notificationService.NotifyDisciplineTeachersAsync(
                lecture.Topic.DisciplineId,
                "📥 Сдан новый отчет",
                $"Студент {student?.FullName ?? "Студент"} отправил отчет по лекции «{lecture.Title}».",
                Models.Entities.NotificationType.Info,
                $"/Teacher/StudentReports?disciplineId={lecture.Topic.DisciplineId}"
            );
        }
        catch { }

        TempData["SuccessMessage"] = "Отчёт успешно загружен и отправлен преподавателю на проверку!";
        return RedirectToAction(nameof(Lecture), new { id = lectureId });
    }

    // Скачивание своего отчета студентом
    [HttpGet]
    public async Task<IActionResult> DownloadReport(int id)
    {
        int studentId = GetCurrentStudentId();
        var submission = await _db.StudentReportSubmissions
            .Include(s => s.Lecture)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (submission == null) return NotFound("Отчёт не найден");
        if (submission.StudentId != studentId) return Forbid();

        string relativePath = submission.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string filePath = Path.Combine(_env.WebRootPath, relativePath);

        if (!System.IO.File.Exists(filePath) && Directory.Exists("/data"))
        {
            string pPath = Path.Combine("/data", relativePath);
            if (System.IO.File.Exists(pPath)) filePath = pPath;
        }

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("Файл отчёта не найден на сервере");
        }

        var contentType = submission.ContentType ?? "application/octet-stream";
        return PhysicalFile(filePath, contentType, submission.OriginalFileName);
    }

    // Скачивание файла с правками от преподавателя
    [HttpGet]
    public async Task<IActionResult> DownloadTeacherReviewedFile(int id)
    {
        int studentId = GetCurrentStudentId();
        var submission = await _db.StudentReportSubmissions
            .Include(s => s.Lecture)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (submission == null || string.IsNullOrEmpty(submission.ReviewedFilePath)) return NotFound("Файл с правками не найден");
        if (submission.StudentId != studentId) return Forbid();

        string relativePath = submission.ReviewedFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string filePath = Path.Combine(_env.WebRootPath, relativePath);

        if (!System.IO.File.Exists(filePath) && Directory.Exists("/data"))
        {
            string pPath = Path.Combine("/data", relativePath);
            if (System.IO.File.Exists(pPath)) filePath = pPath;
        }

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("Файл отсутствует на диске сервера");
        }

        return PhysicalFile(filePath, "application/octet-stream", submission.ReviewedFileName ?? "Reviewed_File");
    }

    // Единый трекер отчётов и практических работ студента
    [HttpGet]
    public async Task<IActionResult> MyReports(ReportSubmissionStatus? status)
    {
        int studentId = GetCurrentStudentId();

        var allSubmissions = await _db.StudentReportSubmissions
            .Include(r => r.Lecture)
                .ThenInclude(l => l.Topic)
                    .ThenInclude(t => t.Discipline)
            .Include(r => r.ReviewedByTeacher)
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync();

        var filteredReports = status.HasValue
            ? allSubmissions.Where(r => r.Status == status.Value).ToList()
            : allSubmissions;

        var vm = new StudentReportsListViewModel
        {
            SelectedStatus = status,
            TotalCount = allSubmissions.Count,
            PendingCount = allSubmissions.Count(s => s.Status == ReportSubmissionStatus.Pending),
            NeedsRevisionCount = allSubmissions.Count(s => s.Status == ReportSubmissionStatus.NeedsRevision),
            ApprovedCount = allSubmissions.Count(s => s.Status == ReportSubmissionStatus.Approved),
            Reports = filteredReports.Select(r => new StudentReportItemDto
            {
                Id = r.Id,
                LectureId = r.LectureId,
                LectureTitle = r.Lecture.Title,
                DisciplineId = r.Lecture.Topic.DisciplineId,
                DisciplineTitle = r.Lecture.Topic.Discipline.Title,
                OriginalFileName = r.OriginalFileName,
                FileSizeBytes = r.FileSizeBytes,
                StudentComment = r.StudentComment,
                SubmittedAt = r.SubmittedAt,
                Status = r.Status,
                Grade = r.Grade,
                MaxScore = r.Lecture.MaxReportScore ?? 100,
                ReportDeadline = r.Lecture.ReportDeadline,
                TeacherFeedback = r.TeacherFeedback,
                ReviewedAt = r.ReviewedAt,
                ReviewedByTeacherName = r.ReviewedByTeacher?.FullName,
                ReviewedFilePath = r.ReviewedFilePath,
                ReviewedFileName = r.ReviewedFileName,
                ReviewedFileSizeBytes = r.ReviewedFileSizeBytes
            }).ToList()
        };

        return View(vm);
    }

    // Электронная зачётная книжка студента
    [HttpGet]
    public async Task<IActionResult> Gradebook()
    {
        int studentId = GetCurrentStudentId();
        var student = await _db.Users
            .Include(u => u.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .FirstOrDefaultAsync(u => u.Id == studentId);

        if (student == null) return NotFound();

        var groupIds = student.GroupMemberships.Select(gm => gm.GroupId).ToList();

        var disciplines = await _db.Disciplines
            .Include(d => d.CreatedBy)
            .Include(d => d.DisciplineGroups)
            .Include(d => d.Topics)
                .ThenInclude(t => t.Lectures)
                    .ThenInclude(l => l.Test)
            .Where(d => d.IsActive && d.DisciplineGroups.Any(dg => groupIds.Contains(dg.GroupId)))
            .OrderBy(d => d.Title)
            .ToListAsync();

        var vm = new StudentGradebookViewModel
        {
            StudentFullName = student.FullName,
            GroupName = student.GroupMemberships.FirstOrDefault()?.Group.Name ?? "Без группы",
            TotalDisciplinesCount = disciplines.Count,
            Disciplines = new List<DisciplineGradebookItemDto>()
        };

        var allAttempts = await _db.TestAttempts
            .Where(t => t.StudentId == studentId && t.CompletedAt != null)
            .ToListAsync();

        var allReports = await _db.StudentReportSubmissions
            .Include(r => r.Lecture)
            .Where(r => r.StudentId == studentId)
            .ToListAsync();

        int totalCompletedDisciplines = 0;
        List<double> subjectAverageScores = new();

        foreach (var d in disciplines)
        {
            var prog = await _lectureAccessService.GetDisciplineProgressAsync(studentId, d.Id);
            if (prog.ProgressPercent >= 100) totalCompletedDisciplines++;

            var dLectureIds = d.Topics.SelectMany(t => t.Lectures.Select(l => l.Id)).ToList();
            var dTestIds = d.Topics.SelectMany(t => t.Lectures.Where(l => l.Test != null).Select(l => l.Test!.Id)).ToList();

            var dAttempts = allAttempts.Where(a => dTestIds.Contains(a.TestId)).ToList();
            double? avgScore = dAttempts.Any() ? Math.Round(dAttempts.Average(a => a.ScorePercent), 1) : null;
            if (avgScore.HasValue) subjectAverageScores.Add(avgScore.Value);

            var dReports = allReports.Where(r => dLectureIds.Contains(r.LectureId)).ToList();
            var dLecturesWithReports = d.Topics.SelectMany(t => t.Lectures.Where(l => l.RequiresReportSubmission)).ToList();

            var item = new DisciplineGradebookItemDto
            {
                DisciplineId = d.Id,
                Title = d.Title,
                Description = d.Description,
                TeacherName = d.CreatedBy?.FullName ?? "Преподаватель",
                ProgressPercent = prog.ProgressPercent,
                CompletedLectures = prog.CompletedLectures,
                TotalLectures = prog.TotalLectures,
                AverageTestScore = avgScore,
                TestsPassedCount = dAttempts.Where(a => a.IsPassed).Select(a => a.TestId).Distinct().Count(),
                TotalTestsCount = dTestIds.Count,
                ReportsApprovedCount = dReports.Count(r => r.Status == ReportSubmissionStatus.Approved),
                TotalReportsCount = dLecturesWithReports.Count,
                TotalReportScoreEarned = dReports.Where(r => r.Status == ReportSubmissionStatus.Approved).Sum(r => r.Grade ?? 0),
                TotalReportScorePossible = dLecturesWithReports.Sum(l => l.MaxReportScore ?? 100)
            };

            vm.Disciplines.Add(item);
        }

        vm.CompletedDisciplinesCount = totalCompletedDisciplines;
        vm.OverallAverageScore = subjectAverageScores.Any() ? Math.Round(subjectAverageScores.Average(), 1) : 0;
        vm.TotalTestsPassed = allAttempts.Where(a => a.IsPassed).Select(a => a.TestId).Distinct().Count();
        vm.TotalReportsApproved = allReports.Count(r => r.Status == ReportSubmissionStatus.Approved);

        return View(vm);
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
                ImageUrl = q.ImageUrl,
                Options = options.Select(o => new QuestionOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    MatchingKey = o.MatchingKey,
                    ImageUrl = o.ImageUrl
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

        // Уведомление студенту об открытии следующего задания
        if (isLectureCompleted)
        {
            try
            {
                var allDisciplineLectures = await _db.Lectures
                    .Include(l => l.Topic)
                    .Where(l => l.Topic.DisciplineId == test.Lecture.Topic.DisciplineId)
                    .OrderBy(l => l.Topic.OrderIndex)
                    .ThenBy(l => l.OrderIndex)
                    .ToListAsync();

                int currentIndex = allDisciplineLectures.FindIndex(l => l.Id == test.LectureId);
                if (currentIndex >= 0 && currentIndex < allDisciplineLectures.Count - 1)
                {
                    var nextLec = allDisciplineLectures[currentIndex + 1];
                    await _notificationService.CreateNotificationAsync(
                        studentId,
                        "🎯 Открыто следующее задание!",
                        $"Вы успешно освоили лекцию «{test.Lecture.Title}». Вам открыт доступ к следующей лекции «{nextLec.Title}»!",
                        NotificationType.Success,
                        $"/Student/Lecture/{nextLec.Id}"
                    );
                }
                else
                {
                    await _notificationService.CreateNotificationAsync(
                        studentId,
                        "🏆 Курс успешно освоен!",
                        $"Поздравляем! Вы завершили изучение всех лекций дисциплины «{test.Lecture.Topic.Discipline?.Title ?? "курса"}».",
                        NotificationType.Success,
                        $"/Student/Discipline/{test.Lecture.Topic.DisciplineId}"
                    );
                }
            }
            catch { }
        }

        return RedirectToAction(nameof(TestResult), new { attemptId = attempt.Id });
    }

    // Просмотр результатов попытки тестирования
    public async Task<IActionResult> TestResult(int attemptId)
    {
        int studentId = GetCurrentStudentId();

        var attempt = await _db.TestAttempts
            .Include(a => a.Test)
                .ThenInclude(t => t.Lecture)
                    .ThenInclude(l => l.Topic)
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

        // Проверяем, открылась ли следующая лекция (полная защита от null)
        int? nextLectureId = null;
        bool nextUnlocked = false;

        try
        {
            int? disciplineId = attempt.Test.Lecture?.Topic?.DisciplineId;
            if (!disciplineId.HasValue && attempt.Test?.Lecture != null)
            {
                disciplineId = await _db.Topics
                    .Where(t => t.Id == attempt.Test.Lecture.TopicId)
                    .Select(t => (int?)t.DisciplineId)
                    .FirstOrDefaultAsync();
            }

            if (disciplineId.HasValue)
            {
                var allDisciplineLectures = await _db.Lectures
                    .Where(l => l.Topic.DisciplineId == disciplineId.Value)
                    .OrderBy(l => l.Topic.OrderIndex)
                    .ThenBy(l => l.Topic.Id)
                    .ThenBy(l => l.OrderIndex)
                    .ThenBy(l => l.Id)
                    .Select(l => l.Id)
                    .ToListAsync();

                int currentIndex = allDisciplineLectures.IndexOf(attempt.Test?.LectureId ?? 0);
                if (currentIndex >= 0 && currentIndex < allDisciplineLectures.Count - 1)
                {
                    nextLectureId = allDisciplineLectures[currentIndex + 1];
                    nextUnlocked = await _lectureAccessService.CanStudentAccessLectureAsync(studentId, nextLectureId.Value);
                }
            }
        }
        catch (Exception ex)
        {
            await _eventLogger.LogActionAsync(studentId, "TEST_RESULT_NEXT_LECTURE_WARN", $"Предупреждение при определении следующей лекции: {ex.Message}");
        }

        var model = new TestResultViewModel
        {
            AttemptId = attempt.Id,
            TestId = attempt.TestId,
            LectureId = attempt.Test?.LectureId ?? 0,
            LectureTitle = attempt.Test?.Lecture?.Title ?? "Лекция",
            TestTitle = attempt.Test?.Title ?? "Тест",
            AttemptNumber = attempt.AttemptNumber,
            TotalQuestions = attempt.TotalQuestions,
            CorrectQuestions = attempt.CorrectQuestions,
            ScorePercent = attempt.ScorePercent,
            PassingScorePercent = attempt.Test?.PassingScorePercent ?? 70,
            IsPassed = attempt.IsPassed,
            AttemptsRemaining = remaining,
            NextLectureUnlocked = nextUnlocked,
            NextLectureId = nextLectureId,
            Answers = attempt.Answers.Select(ans => new TestAnswerDetailDto
            {
                QuestionText = ans.Question?.Text ?? "Вопрос",
                Type = ans.Question?.Type ?? QuestionType.SingleChoice,
                ImageUrl = ans.Question?.ImageUrl,
                GivenAnswer = ans.GivenAnswer,
                IsCorrect = ans.IsCorrect,
                PointsEarned = ans.PointsEarned
            }).ToList()
        };

        return View(model);
    }
}
