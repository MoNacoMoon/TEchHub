using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Models.ViewModels;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Controllers;

[Authorize(Roles = "Teacher,Admin")]
public class TeacherController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILectureAccessService _lectureAccessService;
    private readonly IEventLogService _eventLogger;
    private readonly IWebHostEnvironment _env;

    public TeacherController(
        AppDbContext db,
        ILectureAccessService lectureAccessService,
        IEventLogService eventLogger,
        IWebHostEnvironment env)
    {
        _db = db;
        _lectureAccessService = lectureAccessService;
        _eventLogger = eventLogger;
        _env = env;
    }

    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".zip", ".rar", ".7z", ".txt", ".png", ".jpg", ".jpeg"
    };

    private const long MaxAttachmentSizeBytes = 50 * 1024 * 1024; // 50 МБ
    private const long MaxBackupSizeBytes = 100 * 1024 * 1024; // 100 МБ

    private static bool IsAttachmentAllowed(IFormFile file, out string? error)
    {
        if (file.Length > MaxAttachmentSizeBytes)
        {
            error = "Размер файла не должен превышать 50 МБ.";
            return false;
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedAttachmentExtensions.Contains(ext))
        {
            error = "Недопустимый тип файла. Разрешены только документы (PDF, Word, Excel, PowerPoint), архивы и изображения.";
            return false;
        }

        error = null;
        return true;
    }

    private int GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idClaim, out int id) ? id : 0;
    }

    // Главная панель преподавателя
    public async Task<IActionResult> Index()
    {
        try
        {
            int teacherId = GetCurrentUserId();

            var disciplines = await _db.Disciplines
                .Include(d => d.Topics)
                    .ThenInclude(t => t.Lectures)
                .Include(d => d.DisciplineGroups)
                .ToListAsync();

            int disciplinesCount = disciplines.Count;
            int topicsCount = disciplines.SelectMany(d => d.Topics ?? Enumerable.Empty<Topic>()).Count();
            int lecturesCount = disciplines.SelectMany(d => d.Topics ?? Enumerable.Empty<Topic>()).SelectMany(t => t.Lectures ?? Enumerable.Empty<Lecture>()).Count();
            int studentsCount = await _db.Users.CountAsync(u => u.Role == UserRole.Student);

            int pendingReviews = 0;
            try
            {
                pendingReviews = await _db.StudentControlAnswers
                    .Include(a => a.ControlQuestion)
                    .CountAsync(a => a.ControlQuestion != null && a.ControlQuestion.Type == QuestionType.OpenEnded && !a.IsReviewedByTeacher);
            }
            catch { }

            var recentLogs = await _db.EventLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(10)
                .ToListAsync();

            var model = new TeacherDashboardViewModel
            {
                DisciplinesCount = disciplinesCount,
                TopicsCount = topicsCount,
                LecturesCount = lecturesCount,
                StudentsCount = studentsCount,
                PendingReviewsCount = pendingReviews,
                RecentLogs = recentLogs,
                Disciplines = disciplines.Select(d => new DisciplineSummaryDto
                {
                    Id = d.Id,
                    Title = d.Title,
                    TopicsCount = d.Topics?.Count ?? 0,
                    LecturesCount = d.Topics?.SelectMany(t => t.Lectures ?? Enumerable.Empty<Lecture>()).Count() ?? 0,
                    EnrolledStudentsCount = d.DisciplineGroups?.Count ?? 0
                }).ToList()
            };

            return View(model);
        }
        catch (Exception ex)
        {
            await _eventLogger.LogActionAsync(null, "TEACHER_INDEX_ERROR", ex.Message);
            return View(new TeacherDashboardViewModel());
        }
    }

    // ==========================================
    // УПРАВЛЕНИЕ ДИСЦИПЛИНАМИ
    // ==========================================

    public async Task<IActionResult> Disciplines()
    {
        var list = await _db.Disciplines
            .Include(d => d.Topics)
                .ThenInclude(t => t.Lectures)
            .Include(d => d.DisciplineGroups)
                .ThenInclude(dg => dg.Group)
            .OrderBy(d => d.Title)
            .ToListAsync();

        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> CreateDiscipline()
    {
        var model = new DisciplineEditViewModel
        {
            AvailableGroups = await _db.StudentGroups.OrderBy(g => g.Name).ToListAsync()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDiscipline(DisciplineEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableGroups = await _db.StudentGroups.OrderBy(g => g.Name).ToListAsync();
            return View(model);
        }

        var discipline = new Discipline
        {
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            CreatedByUserId = GetCurrentUserId(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _db.Disciplines.Add(discipline);
        await _db.SaveChangesAsync();

        if (model.SelectedGroupIds != null && model.SelectedGroupIds.Any())
        {
            foreach (var gid in model.SelectedGroupIds)
            {
                _db.DisciplineGroups.Add(new DisciplineGroup
                {
                    DisciplineId = discipline.Id,
                    GroupId = gid
                });
            }
            await _db.SaveChangesAsync();
        }

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "CREATE_DISCIPLINE",
            $"Создана дисциплина '{discipline.Title}'",
            null,
            discipline.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = $"Дисциплина «{discipline.Title}» успешно создана!";
        return RedirectToAction(nameof(Disciplines));
    }

    [HttpGet]
    public async Task<IActionResult> EditDiscipline(int id)
    {
        var discipline = await _db.Disciplines
            .Include(d => d.DisciplineGroups)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (discipline == null) return NotFound();

        var model = new DisciplineEditViewModel
        {
            Id = discipline.Id,
            Title = discipline.Title,
            Description = discipline.Description,
            SelectedGroupIds = discipline.DisciplineGroups.Select(dg => dg.GroupId).ToList(),
            AvailableGroups = await _db.StudentGroups.OrderBy(g => g.Name).ToListAsync()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDiscipline(DisciplineEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableGroups = await _db.StudentGroups.OrderBy(g => g.Name).ToListAsync();
            return View(model);
        }

        var discipline = await _db.Disciplines
            .Include(d => d.DisciplineGroups)
            .FirstOrDefaultAsync(d => d.Id == model.Id);

        if (discipline == null) return NotFound();

        discipline.Title = model.Title.Trim();
        discipline.Description = model.Description.Trim();

        _db.DisciplineGroups.RemoveRange(discipline.DisciplineGroups);
        if (model.SelectedGroupIds != null)
        {
            foreach (var gid in model.SelectedGroupIds)
            {
                _db.DisciplineGroups.Add(new DisciplineGroup { DisciplineId = discipline.Id, GroupId = gid });
            }
        }

        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "UPDATE_DISCIPLINE",
            $"Обновлена дисциплина '{discipline.Title}'",
            null,
            discipline.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = "Дисциплина успешно обновлена";
        return RedirectToAction(nameof(Disciplines));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDiscipline(int id)
    {
        var discipline = await _db.Disciplines.FindAsync(id);
        if (discipline != null)
        {
            _db.Disciplines.Remove(discipline);
            await _db.SaveChangesAsync();

            await _eventLogger.LogActionAsync(
                GetCurrentUserId(),
                "DELETE_DISCIPLINE",
                $"Удалена дисциплина '{discipline.Title}'",
                null,
                discipline.Title,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Дисциплина удалена";
        }
        return RedirectToAction(nameof(Disciplines));
    }

    // ==========================================
    // УПРАВЛЕНИЕ ТЕМАМИ
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> CreateTopic(int disciplineId)
    {
        var discipline = await _db.Disciplines.FindAsync(disciplineId);
        if (discipline == null) return NotFound();

        int maxOrder = await _db.Topics.Where(t => t.DisciplineId == disciplineId).MaxAsync(t => (int?)t.OrderIndex) ?? 0;

        var model = new TopicEditViewModel
        {
            DisciplineId = disciplineId,
            DisciplineTitle = discipline.Title,
            OrderIndex = maxOrder + 1
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTopic(TopicEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var topic = new Topic
        {
            DisciplineId = model.DisciplineId,
            Title = model.Title.Trim(),
            Description = model.Description?.Trim(),
            OrderIndex = model.OrderIndex
        };

        _db.Topics.Add(topic);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Тема успешно добавлена";
        return RedirectToAction(nameof(DisciplineDetails), new { id = model.DisciplineId });
    }

    [HttpGet]
    public async Task<IActionResult> EditTopic(int id)
    {
        var topic = await _db.Topics.Include(t => t.Discipline).FirstOrDefaultAsync(t => t.Id == id);
        if (topic == null) return NotFound();

        var model = new TopicEditViewModel
        {
            Id = topic.Id,
            DisciplineId = topic.DisciplineId,
            DisciplineTitle = topic.Discipline.Title,
            Title = topic.Title,
            Description = topic.Description,
            OrderIndex = topic.OrderIndex
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTopic(TopicEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var topic = await _db.Topics.FindAsync(model.Id);
        if (topic == null) return NotFound();

        topic.Title = model.Title.Trim();
        topic.Description = model.Description?.Trim();
        topic.OrderIndex = model.OrderIndex;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Тема обновлена";
        return RedirectToAction(nameof(DisciplineDetails), new { id = topic.DisciplineId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTopic(int id)
    {
        var topic = await _db.Topics.FindAsync(id);
        if (topic != null)
        {
            int discId = topic.DisciplineId;
            _db.Topics.Remove(topic);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Тема удалена";
            return RedirectToAction(nameof(DisciplineDetails), new { id = discId });
        }
        return RedirectToAction(nameof(Disciplines));
    }

    // Детальный просмотр дисциплины (дерево тем и лекций для редактирования)
    public async Task<IActionResult> DisciplineDetails(int id)
    {
        var discipline = await _db.Disciplines
            .Include(d => d.Topics.OrderBy(t => t.OrderIndex))
                .ThenInclude(t => t.Lectures.OrderBy(l => l.OrderIndex))
                    .ThenInclude(l => l.ControlQuestions)
            .Include(d => d.Topics)
                .ThenInclude(t => t.Lectures)
                    .ThenInclude(l => l.Test)
            .Include(d => d.Announcements.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.CreatedAt))
                .ThenInclude(a => a.AuthorTeacher)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (discipline == null) return NotFound();

        return View(discipline);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAnnouncement(int disciplineId, string title, string content, bool isPinned = true)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
        {
            TempData["ErrorMessage"] = "Заполните заголовок и текст объявления";
            return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
        }

        var announcement = new CourseAnnouncement
        {
            DisciplineId = disciplineId,
            AuthorTeacherId = GetCurrentUserId(),
            Title = title.Trim(),
            Content = content.Trim(),
            IsPinned = isPinned,
            CreatedAt = DateTime.UtcNow
        };

        _db.CourseAnnouncements.Add(announcement);
        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "CREATE_ANNOUNCEMENT",
            $"Создано объявление '{announcement.Title}'",
            null,
            announcement.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = "Объявление успешно опубликовано для студентов";
        return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAnnouncement(int id, int disciplineId)
    {
        var ann = await _db.CourseAnnouncements.FindAsync(id);
        if (ann != null)
        {
            _db.CourseAnnouncements.Remove(ann);
            await _db.SaveChangesAsync();

            await _eventLogger.LogActionAsync(
                GetCurrentUserId(),
                "DELETE_ANNOUNCEMENT",
                $"Удалено объявление #{id}",
                null,
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Объявление удалено";
        }
        return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
    }

    // ==========================================
    // УПРАВЛЕНИЕ ЛЕКЦИЯМИ И МАТЕРИАЛАМИ
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> CreateLecture(int topicId)
    {
        var topic = await _db.Topics.Include(t => t.Discipline).FirstOrDefaultAsync(t => t.Id == topicId);
        if (topic == null) return NotFound();

        int maxOrder = await _db.Lectures.Where(l => l.TopicId == topicId).MaxAsync(l => (int?)l.OrderIndex) ?? 0;

        var model = new LectureEditViewModel
        {
            TopicId = topicId,
            TopicTitle = topic.Title,
            DisciplineId = topic.DisciplineId,
            OrderIndex = maxOrder + 1,
            RequiresManualReview = true,
            RequiresReportSubmission = false,
            MaxReportScore = 100
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLecture(LectureEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var lecture = new Lecture
        {
            TopicId = model.TopicId,
            Title = model.Title.Trim(),
            Description = model.Description?.Trim(),
            ContentHtml = model.ContentHtml,
            OrderIndex = model.OrderIndex,
            RequiresManualReview = model.RequiresManualReview,
            RequiresReportSubmission = model.RequiresReportSubmission,
            ReportInstructions = model.ReportInstructions?.Trim(),
            MaxReportScore = model.MaxReportScore,
            CreatedAt = DateTime.UtcNow
        };

        // Обработка загрузки файла материала (PDF, DOCX и т.д.)
        if (model.AttachmentFile != null && model.AttachmentFile.Length > 0)
        {
            if (!IsAttachmentAllowed(model.AttachmentFile, out var uploadError))
            {
                ModelState.AddModelError(nameof(model.AttachmentFile), uploadError!);
                ViewBag.Topics = await _db.Topics.Where(t => t.DisciplineId == model.DisciplineId).OrderBy(t => t.OrderIndex).ToListAsync();
                return View(model);
            }

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

            var originalName = Path.GetFileName(model.AttachmentFile.FileName);
            var safeExt = Path.GetExtension(originalName);
            var uniqueFileName = $"{Guid.NewGuid():N}{safeExt}";
            var fullPath = Path.Combine(uploadsDir, uniqueFileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await model.AttachmentFile.CopyToAsync(stream);
            }

            lecture.AttachmentFilePath = $"/uploads/{uniqueFileName}";
            lecture.AttachmentOriginalName = originalName;
            lecture.AttachmentContentType = model.AttachmentFile.ContentType;
        }

        _db.Lectures.Add(lecture);
        await _db.SaveChangesAsync();

        // Автоматически создаем пустой тест к лекции
        var test = new Test
        {
            LectureId = lecture.Id,
            Title = $"Тест по теме: {lecture.Title}",
            PassingScorePercent = 70,
            MaxAttempts = 3,
            TimeLimitMinutes = 15,
            ShuffleQuestions = true,
            ShuffleOptions = true,
            IsActive = true
        };
        _db.Tests.Add(test);
        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "CREATE_LECTURE",
            $"Создана лекция '{lecture.Title}' с вложением '{lecture.AttachmentOriginalName ?? "нет"}'",
            null,
            lecture.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = "Лекция успешно создана! Теперь вы можете добавить контрольные вопросы и настроить тест.";
        return RedirectToAction(nameof(LectureDetails), new { id = lecture.Id });
    }

    [HttpGet]
    public async Task<IActionResult> EditLecture(int id)
    {
        var lecture = await _db.Lectures.Include(l => l.Topic).FirstOrDefaultAsync(l => l.Id == id);
        if (lecture == null) return NotFound();

        var model = new LectureEditViewModel
        {
            Id = lecture.Id,
            TopicId = lecture.TopicId,
            TopicTitle = lecture.Topic.Title,
            DisciplineId = lecture.Topic.DisciplineId,
            Title = lecture.Title,
            Description = lecture.Description,
            ContentHtml = lecture.ContentHtml,
            OrderIndex = lecture.OrderIndex,
            RequiresManualReview = lecture.RequiresManualReview,
            RequiresReportSubmission = lecture.RequiresReportSubmission,
            ReportInstructions = lecture.ReportInstructions,
            MaxReportScore = lecture.MaxReportScore,
            ExistingAttachmentName = lecture.AttachmentOriginalName,
            ExistingAttachmentPath = lecture.AttachmentFilePath
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditLecture(LectureEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var lecture = await _db.Lectures.FindAsync(model.Id);
        if (lecture == null) return NotFound();

        lecture.Title = model.Title.Trim();
        lecture.Description = model.Description?.Trim();
        lecture.ContentHtml = model.ContentHtml;
        lecture.OrderIndex = model.OrderIndex;
        lecture.RequiresManualReview = model.RequiresManualReview;
        lecture.RequiresReportSubmission = model.RequiresReportSubmission;
        lecture.ReportInstructions = model.ReportInstructions?.Trim();
        lecture.MaxReportScore = model.MaxReportScore;
        lecture.UpdatedAt = DateTime.UtcNow;

        // Если загружен новый файл для замены
        if (model.AttachmentFile != null && model.AttachmentFile.Length > 0)
        {
            if (!IsAttachmentAllowed(model.AttachmentFile, out var uploadError))
            {
                ModelState.AddModelError(nameof(model.AttachmentFile), uploadError!);
                model.ExistingAttachmentName = lecture.AttachmentOriginalName;
                model.ExistingAttachmentPath = lecture.AttachmentFilePath;
                return View(model);
            }

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

            var originalName = Path.GetFileName(model.AttachmentFile.FileName);
            var safeExt = Path.GetExtension(originalName);
            var uniqueFileName = $"{Guid.NewGuid():N}{safeExt}";
            var fullPath = Path.Combine(uploadsDir, uniqueFileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await model.AttachmentFile.CopyToAsync(stream);
            }

            lecture.AttachmentFilePath = $"/uploads/{uniqueFileName}";
            lecture.AttachmentOriginalName = originalName;
            lecture.AttachmentContentType = model.AttachmentFile.ContentType;

            await _eventLogger.LogActionAsync(
                GetCurrentUserId(),
                "UPDATE_MATERIAL",
                $"Заменен учебный материал лекции '{lecture.Title}' на файл '{originalName}'",
                null,
                lecture.Title,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Лекция успешно обновлена";
        return RedirectToAction(nameof(LectureDetails), new { id = lecture.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLecture(int id)
    {
        var lecture = await _db.Lectures.Include(l => l.Topic).FirstOrDefaultAsync(l => l.Id == id);
        if (lecture != null)
        {
            int discId = lecture.Topic.DisciplineId;
            _db.Lectures.Remove(lecture);
            await _db.SaveChangesAsync();

            await _eventLogger.LogActionAsync(
                GetCurrentUserId(),
                "DELETE_LECTURE",
                $"Удалена лекция '{lecture.Title}'",
                null,
                lecture.Title,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Лекция удалена";
            return RedirectToAction(nameof(DisciplineDetails), new { id = discId });
        }
        return RedirectToAction(nameof(Disciplines));
    }

    // Детали лекции: просмотр материала, списка контрольных вопросов и параметров теста
    public async Task<IActionResult> LectureDetails(int id)
    {
        var lecture = await _db.Lectures
            .Include(l => l.Topic)
                .ThenInclude(t => t.Discipline)
            .Include(l => l.ControlQuestions.OrderBy(cq => cq.OrderIndex))
                .ThenInclude(cq => cq.Options)
            .Include(l => l.Test)
                .ThenInclude(t => t!.Questions)
                    .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lecture == null) return NotFound();

        return View(lecture);
    }

    // ==========================================
    // КОНТРОЛЬНЫЕ ВОПРОСЫ
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> CreateControlQuestion(int lectureId)
    {
        var lecture = await _db.Lectures.FindAsync(lectureId);
        if (lecture == null) return NotFound();

        int maxOrder = await _db.ControlQuestions.Where(q => q.LectureId == lectureId).MaxAsync(q => (int?)q.OrderIndex) ?? 0;

        var model = new QuestionCreateEditViewModel
        {
            LectureId = lectureId,
            LectureTitle = lecture.Title,
            OrderIndex = maxOrder + 1,
            Type = QuestionType.SingleChoice,
            Options = new List<QuestionOptionInputDto>
            {
                new QuestionOptionInputDto { OrderIndex = 1, IsCorrect = true, Text = "Вариант 1" },
                new QuestionOptionInputDto { OrderIndex = 2, IsCorrect = false, Text = "Вариант 2" }
            }
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateControlQuestion(QuestionCreateEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        string? imgUrl = null;
        if (model.ImageFile != null && model.ImageFile.Length > 0)
        {
            imgUrl = await SaveQuestionImageAsync(model.ImageFile);
        }
        else if (!string.IsNullOrWhiteSpace(model.ImageUrl))
        {
            imgUrl = model.ImageUrl.Trim();
        }

        var cq = new ControlQuestion
        {
            LectureId = model.LectureId,
            Text = model.Text.Trim(),
            Type = model.Type,
            OrderIndex = model.OrderIndex,
            ImageUrl = imgUrl,
            Explanation = model.Explanation?.Trim(),
            CorrectTextAnswer = model.CorrectTextAnswer?.Trim(),
            SynonymsCsv = model.SynonymsCsv?.Trim()
        };

        _db.ControlQuestions.Add(cq);
        await _db.SaveChangesAsync();

        if (model.Options != null && (model.Type == QuestionType.SingleChoice || model.Type == QuestionType.MultipleChoice || model.Type == QuestionType.Matching))
        {
            int idx = 1;
            foreach (var opt in model.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text) || !string.IsNullOrWhiteSpace(o.ImageUrl)))
            {
                _db.ControlQuestionOptions.Add(new ControlQuestionOption
                {
                    ControlQuestionId = cq.Id,
                    Text = opt.Text?.Trim() ?? string.Empty,
                    IsCorrect = opt.IsCorrect,
                    MatchingKey = opt.MatchingKey?.Trim(),
                    ImageUrl = opt.ImageUrl?.Trim(),
                    OrderIndex = idx++
                });
            }
            await _db.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = "Контрольный вопрос успешно добавлен";
        return RedirectToAction(nameof(LectureDetails), new { id = model.LectureId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteControlQuestion(int id)
    {
        var q = await _db.ControlQuestions.FindAsync(id);
        if (q != null)
        {
            int lecId = q.LectureId;
            _db.ControlQuestions.Remove(q);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Вопрос удален";
            return RedirectToAction(nameof(LectureDetails), new { id = lecId });
        }
        return RedirectToAction(nameof(Disciplines));
    }

    // ==========================================
    // ТЕСТЫ И ВОПРОСЫ ТЕСТА
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> EditTest(int lectureId)
    {
        var lecture = await _db.Lectures
            .Include(l => l.Test)
                .ThenInclude(t => t!.Questions)
                    .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == lectureId);

        if (lecture == null) return NotFound();

        var test = lecture.Test;
        if (test == null)
        {
            test = new Test
            {
                LectureId = lectureId,
                Title = $"Тест по лекции {lecture.Title}",
                PassingScorePercent = 70,
                MaxAttempts = 3,
                TimeLimitMinutes = 15,
                IsActive = true
            };
            _db.Tests.Add(test);
            await _db.SaveChangesAsync();
        }

        var model = new TestEditViewModel
        {
            Id = test.Id,
            LectureId = lecture.Id,
            LectureTitle = lecture.Title,
            Title = test.Title,
            Description = test.Description,
            PassingScorePercent = test.PassingScorePercent,
            MaxAttempts = test.MaxAttempts,
            TimeLimitMinutes = test.TimeLimitMinutes,
            ShuffleQuestions = test.ShuffleQuestions,
            ShuffleOptions = test.ShuffleOptions,
            IsActive = test.IsActive,
            Questions = test.Questions.OrderBy(q => q.OrderIndex).Select(q => new TestQuestionSummaryDto
            {
                Id = q.Id,
                Text = q.Text,
                Type = q.Type,
                Points = q.Points,
                OrderIndex = q.OrderIndex,
                OptionsCount = q.Options.Count,
                ImageUrl = q.ImageUrl
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTest(TestEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var test = await _db.Tests.FindAsync(model.Id);
        if (test == null) return NotFound();

        test.Title = model.Title.Trim();
        test.Description = model.Description?.Trim();
        test.PassingScorePercent = model.PassingScorePercent;
        test.MaxAttempts = model.MaxAttempts;
        test.TimeLimitMinutes = model.TimeLimitMinutes > 0 ? model.TimeLimitMinutes : null;
        test.ShuffleQuestions = model.ShuffleQuestions;
        test.ShuffleOptions = model.ShuffleOptions;
        test.IsActive = model.IsActive;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Параметры теста сохранены";
        return RedirectToAction(nameof(LectureDetails), new { id = test.LectureId });
    }

    // Предпросмотр теста для преподавателя (проверка корректности вопросов и ответов)
    [HttpGet]
    public async Task<IActionResult> PreviewTest(int id)
    {
        var test = await _db.Tests
            .Include(t => t.Lecture)
                .ThenInclude(l => l.Topic)
                    .ThenInclude(top => top.Discipline)
            .Include(t => t.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Options.OrderBy(o => o.OrderIndex))
            .FirstOrDefaultAsync(t => t.Id == id);

        if (test == null) return NotFound("Тест не найден");

        return View(test);
    }

    private async Task<string?> SaveQuestionImageAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) return null;

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext)) return null;

        if (file.Length > 15 * 1024 * 1024) return null;

        var uniqueFileName = $"q_{Guid.NewGuid():N}{ext}";
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var targetPath = Path.Combine(uploadsDir, uniqueFileName);
        using (var stream = new FileStream(targetPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        if (Directory.Exists("/data"))
        {
            try
            {
                var persistentUploads = "/data/uploads";
                if (!Directory.Exists(persistentUploads)) Directory.CreateDirectory(persistentUploads);
                var pPath = Path.Combine(persistentUploads, uniqueFileName);
                System.IO.File.Copy(targetPath, pPath, true);
            }
            catch { }
        }

        return $"/uploads/{uniqueFileName}";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadTestImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "Файл не выбран" });
        }

        var url = await SaveQuestionImageAsync(file);
        if (string.IsNullOrEmpty(url))
        {
            return BadRequest(new { success = false, message = "Недопустимый формат или размер (разрешены PNG, JPG, GIF, WebP, SVG до 15 МБ)" });
        }

        return Json(new { success = true, url });
    }

    [HttpGet]
    public async Task<IActionResult> CreateTestQuestion(int testId)
    {
        var test = await _db.Tests.Include(t => t.Lecture).FirstOrDefaultAsync(t => t.Id == testId);
        if (test == null) return NotFound();

        int maxOrder = await _db.TestQuestions.Where(q => q.TestId == testId).MaxAsync(q => (int?)q.OrderIndex) ?? 0;

        ViewBag.Test = test;
        var model = new QuestionCreateEditViewModel
        {
            Id = 0,
            TestId = testId,
            LectureId = test.LectureId,
            LectureTitle = test.Title,
            OrderIndex = maxOrder + 1,
            Points = 1,
            Type = QuestionType.SingleChoice,
            Options = new List<QuestionOptionInputDto>
            {
                new QuestionOptionInputDto { OrderIndex = 1, IsCorrect = true, Text = "Вариант 1" },
                new QuestionOptionInputDto { OrderIndex = 2, IsCorrect = false, Text = "Вариант 2" }
            }
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTestQuestion(int testId, QuestionCreateEditViewModel model)
    {
        var test = await _db.Tests.FindAsync(testId);
        if (test == null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.Text))
        {
            ModelState.AddModelError("Text", "Введите текст вопроса");
            ViewBag.Test = test;
            return View(model);
        }

        string? imgUrl = null;
        if (model.ImageFile != null && model.ImageFile.Length > 0)
        {
            imgUrl = await SaveQuestionImageAsync(model.ImageFile);
        }
        else if (!string.IsNullOrWhiteSpace(model.ImageUrl))
        {
            imgUrl = model.ImageUrl.Trim();
        }

        var tq = new TestQuestion
        {
            TestId = testId,
            Text = model.Text.Trim(),
            Type = model.Type,
            OrderIndex = model.OrderIndex,
            Points = model.Points > 0 ? model.Points : 1,
            ImageUrl = imgUrl,
            CorrectTextAnswer = model.CorrectTextAnswer?.Trim(),
            SynonymsCsv = model.SynonymsCsv?.Trim()
        };

        _db.TestQuestions.Add(tq);
        await _db.SaveChangesAsync();

        if (model.Options != null && (model.Type == QuestionType.SingleChoice || model.Type == QuestionType.MultipleChoice || model.Type == QuestionType.Matching))
        {
            int idx = 1;
            foreach (var opt in model.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text) || !string.IsNullOrWhiteSpace(o.ImageUrl)))
            {
                _db.TestQuestionOptions.Add(new TestQuestionOption
                {
                    TestQuestionId = tq.Id,
                    Text = opt.Text?.Trim() ?? string.Empty,
                    IsCorrect = opt.IsCorrect,
                    MatchingKey = opt.MatchingKey?.Trim(),
                    ImageUrl = opt.ImageUrl?.Trim(),
                    OrderIndex = idx++
                });
            }
            await _db.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = "Вопрос теста добавлен";
        return RedirectToAction(nameof(EditTest), new { lectureId = test.LectureId });
    }

    [HttpGet]
    public async Task<IActionResult> EditTestQuestion(int id)
    {
        var q = await _db.TestQuestions
            .Include(tq => tq.Test)
                .ThenInclude(t => t.Lecture)
            .Include(tq => tq.Options)
            .FirstOrDefaultAsync(tq => tq.Id == id);

        if (q == null) return NotFound("Вопрос теста не найден");

        ViewBag.Test = q.Test;
        var model = new QuestionCreateEditViewModel
        {
            Id = q.Id,
            TestId = q.TestId,
            LectureId = q.Test.LectureId,
            LectureTitle = q.Test.Title,
            Text = q.Text,
            Type = q.Type,
            OrderIndex = q.OrderIndex,
            Points = q.Points,
            ImageUrl = q.ImageUrl,
            CorrectTextAnswer = q.CorrectTextAnswer,
            SynonymsCsv = q.SynonymsCsv,
            Options = q.Options.OrderBy(o => o.OrderIndex).Select(o => new QuestionOptionInputDto
            {
                Id = o.Id,
                Text = o.Text,
                IsCorrect = o.IsCorrect,
                MatchingKey = o.MatchingKey,
                OrderIndex = o.OrderIndex,
                ImageUrl = o.ImageUrl
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTestQuestion(int id, QuestionCreateEditViewModel model)
    {
        var q = await _db.TestQuestions
            .Include(tq => tq.Test)
            .Include(tq => tq.Options)
            .FirstOrDefaultAsync(tq => tq.Id == id);

        if (q == null) return NotFound("Вопрос теста не найден");

        if (string.IsNullOrWhiteSpace(model.Text))
        {
            ModelState.AddModelError("Text", "Введите текст вопроса");
            ViewBag.Test = q.Test;
            return View(model);
        }

        q.Text = model.Text.Trim();
        q.Type = model.Type;
        q.OrderIndex = model.OrderIndex;
        q.Points = model.Points > 0 ? model.Points : 1;
        q.CorrectTextAnswer = model.CorrectTextAnswer?.Trim();
        q.SynonymsCsv = model.SynonymsCsv?.Trim();

        if (model.RemoveImage)
        {
            q.ImageUrl = null;
        }
        else if (model.ImageFile != null && model.ImageFile.Length > 0)
        {
            var newUrl = await SaveQuestionImageAsync(model.ImageFile);
            if (!string.IsNullOrEmpty(newUrl))
            {
                q.ImageUrl = newUrl;
            }
        }
        else if (!string.IsNullOrWhiteSpace(model.ImageUrl))
        {
            q.ImageUrl = model.ImageUrl.Trim();
        }

        _db.TestQuestionOptions.RemoveRange(q.Options);

        if (model.Options != null && (model.Type == QuestionType.SingleChoice || model.Type == QuestionType.MultipleChoice || model.Type == QuestionType.Matching))
        {
            int idx = 1;
            foreach (var opt in model.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text) || !string.IsNullOrWhiteSpace(o.ImageUrl)))
            {
                _db.TestQuestionOptions.Add(new TestQuestionOption
                {
                    TestQuestionId = q.Id,
                    Text = opt.Text?.Trim() ?? string.Empty,
                    IsCorrect = opt.IsCorrect,
                    MatchingKey = opt.MatchingKey?.Trim(),
                    ImageUrl = opt.ImageUrl?.Trim(),
                    OrderIndex = idx++
                });
            }
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Вопрос теста сохранен";
        return RedirectToAction(nameof(EditTest), new { lectureId = q.Test.LectureId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTestQuestion(int id)
    {
        var q = await _db.TestQuestions.Include(q => q.Test).FirstOrDefaultAsync(q => q.Id == id);
        if (q != null)
        {
            int lecId = q.Test.LectureId;
            _db.TestQuestions.Remove(q);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Вопрос теста удален";
            return RedirectToAction(nameof(EditTest), new { lectureId = lecId });
        }
        return RedirectToAction(nameof(Disciplines));
    }

    // ==========================================
    // ПРОВЕРКА РАЗВЁРНУТЫХ ОТВЕТОВ ПРЕПОДАВАТЕЛЕМ
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> ManualReviews()
    {
        var pendingAnswers = await _db.StudentControlAnswers
            .Include(a => a.Student)
                .ThenInclude(s => s.GroupMemberships)
                    .ThenInclude(gm => gm.Group)
            .Include(a => a.ControlQuestion)
                .ThenInclude(cq => cq.Lecture)
            .Where(a => a.ControlQuestion.Type == QuestionType.OpenEnded && !a.IsReviewedByTeacher)
            .OrderBy(a => a.SubmittedAt)
            .ToListAsync();

        var list = pendingAnswers.Select(a => new ManualReviewItemViewModel
        {
            AnswerId = a.Id,
            StudentId = a.StudentId,
            StudentFullName = a.Student.FullName,
            GroupName = a.Student.GroupMemberships.FirstOrDefault()?.Group.Name ?? "Без группы",
            LectureId = a.ControlQuestion.LectureId,
            LectureTitle = a.ControlQuestion.Lecture.Title,
            QuestionText = a.ControlQuestion.Text,
            StudentAnswer = a.GivenAnswer,
            SubmittedAt = a.SubmittedAt
        }).ToList();

        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GradeAnswer(ManualReviewGradeInputModel model)
    {
        var answer = await _db.StudentControlAnswers
            .Include(a => a.Student)
            .Include(a => a.ControlQuestion)
                .ThenInclude(cq => cq.Lecture)
            .FirstOrDefaultAsync(a => a.Id == model.AnswerId);

        if (answer == null) return NotFound();

        answer.IsReviewedByTeacher = true;
        answer.IsCorrect = model.IsCorrect;
        answer.TeacherComment = model.TeacherComment?.Trim();

        await _db.SaveChangesAsync();

        // Пересчитываем статус завершения лекции для студента
        await _lectureAccessService.UpdateLectureCompletionStatusAsync(answer.StudentId, answer.ControlQuestion.LectureId);

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "GRADE_ANSWER",
            $"Преподаватель проверил ответ студента {answer.Student.FullName} по лекции '{answer.ControlQuestion.Lecture.Title}'. Статус: {(model.IsCorrect ? "ЗАЧТЕНО" : "НЕ ЗАЧТЕНО")}. Комментарий: {model.TeacherComment ?? "нет"}",
            answer.Student.FullName,
            answer.ControlQuestion.Lecture.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = $"Ответ студента {answer.Student.FullName} проверен ({(model.IsCorrect ? "Зачтено" : "Отправлено на доработку")})";
        return RedirectToAction(nameof(ManualReviews));
    }

    // ==========================================
    // ПРОВЕРКА ОТЧЕТОВ СТУДЕНТОВ
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> StudentReports(int? disciplineId, int? groupId, ReportSubmissionStatus? status, string? search)
    {
        var disciplines = await _db.Disciplines.OrderBy(d => d.Title).ToListAsync();
        var groups = await _db.StudentGroups.OrderBy(g => g.Name).ToListAsync();

        var query = _db.StudentReportSubmissions
            .Include(r => r.Student)
                .ThenInclude(s => s.GroupMemberships)
                    .ThenInclude(gm => gm.Group)
            .Include(r => r.Lecture)
                .ThenInclude(l => l.Topic)
                    .ThenInclude(t => t.Discipline)
            .Include(r => r.ReviewedByTeacher)
            .AsQueryable();

        if (disciplineId.HasValue && disciplineId.Value > 0)
        {
            query = query.Where(r => r.Lecture.Topic.DisciplineId == disciplineId.Value);
        }

        if (groupId.HasValue && groupId.Value > 0)
        {
            query = query.Where(r => r.Student.GroupMemberships.Any(gm => gm.GroupId == groupId.Value));
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(r => r.Student.FullName.ToLower().Contains(searchLower) ||
                                     r.Student.Username.ToLower().Contains(searchLower) ||
                                     r.OriginalFileName.ToLower().Contains(searchLower) ||
                                     r.Lecture.Title.ToLower().Contains(searchLower));
        }

        var reports = await query
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync();

        var allQuery = _db.StudentReportSubmissions.AsQueryable();
        if (disciplineId.HasValue && disciplineId.Value > 0)
        {
            allQuery = allQuery.Where(r => r.Lecture.Topic.DisciplineId == disciplineId.Value);
        }
        if (groupId.HasValue && groupId.Value > 0)
        {
            allQuery = allQuery.Where(r => r.Student.GroupMemberships.Any(gm => gm.GroupId == groupId.Value));
        }

        var counts = await allQuery
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var vm = new TeacherReportsViewModel
        {
            SelectedDisciplineId = disciplineId,
            SelectedGroupId = groupId,
            SelectedStatus = status,
            SearchQuery = search,
            Disciplines = disciplines,
            Groups = groups,
            TotalCount = counts.Sum(c => c.Count),
            PendingCount = counts.FirstOrDefault(c => c.Status == ReportSubmissionStatus.Pending)?.Count ?? 0,
            ApprovedCount = counts.FirstOrDefault(c => c.Status == ReportSubmissionStatus.Approved)?.Count ?? 0,
            NeedsRevisionCount = counts.FirstOrDefault(c => c.Status == ReportSubmissionStatus.NeedsRevision)?.Count ?? 0,
            RejectedCount = counts.FirstOrDefault(c => c.Status == ReportSubmissionStatus.Rejected)?.Count ?? 0,
            Reports = reports.Select(r => new TeacherReportItemViewModel
            {
                Id = r.Id,
                LectureId = r.LectureId,
                LectureTitle = r.Lecture.Title,
                DisciplineId = r.Lecture.Topic.DisciplineId,
                DisciplineTitle = r.Lecture.Topic.Discipline.Title,
                StudentId = r.StudentId,
                StudentFullName = r.Student.FullName,
                StudentGroupName = r.Student.GroupMemberships.FirstOrDefault()?.Group.Name ?? "Без группы",
                OriginalFileName = r.OriginalFileName,
                FileSizeBytes = r.FileSizeBytes,
                StudentComment = r.StudentComment,
                SubmittedAt = r.SubmittedAt,
                Status = r.Status,
                Grade = r.Grade,
                MaxScore = r.Lecture.MaxReportScore ?? 100,
                TeacherFeedback = r.TeacherFeedback,
                ReviewedAt = r.ReviewedAt,
                ReviewedByTeacherName = r.ReviewedByTeacher?.FullName
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GradeReport(GradeReportInputModel model)
    {
        var report = await _db.StudentReportSubmissions
            .Include(r => r.Student)
            .Include(r => r.Lecture)
            .FirstOrDefaultAsync(r => r.Id == model.ReportId);

        if (report == null) return NotFound("Отчёт не найден");

        int teacherId = GetCurrentUserId();
        var teacher = await _db.Users.FindAsync(teacherId);

        report.Status = model.Status;
        report.Grade = model.Grade;
        report.TeacherFeedback = model.TeacherFeedback?.Trim();
        report.ReviewedAt = DateTime.UtcNow;
        report.ReviewedByTeacherId = teacherId;

        await _db.SaveChangesAsync();

        // Пересчитываем статус завершения лекции для студента
        await _lectureAccessService.UpdateLectureCompletionStatusAsync(report.StudentId, report.LectureId);

        string statusLabel = model.Status switch
        {
            ReportSubmissionStatus.Approved => "ЗАЧТЕНО",
            ReportSubmissionStatus.NeedsRevision => "НА ДОРАБОТКУ",
            ReportSubmissionStatus.Rejected => "НЕ ЗАЧТЕНО",
            _ => "НА ПРОВЕРКЕ"
        };

        await _eventLogger.LogActionAsync(
            teacherId,
            "REPORT_GRADED",
            $"Преподаватель {teacher?.FullName} проверил отчет студента {report.Student.FullName} по лекции '{report.Lecture.Title}'. Результат: {statusLabel}. Балл: {report.Grade ?? 0}/{report.Lecture.MaxReportScore ?? 100}. Замечания: {report.TeacherFeedback ?? "нет"}",
            report.Student.FullName,
            report.Lecture.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = $"Отчёт студента {report.Student.FullName} проверен (статус: {statusLabel})";
        return RedirectToAction(nameof(StudentReports));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadStudentReport(int id)
    {
        var report = await _db.StudentReportSubmissions
            .FirstOrDefaultAsync(r => r.Id == id);

        if (report == null) return NotFound("Отчёт не найден");

        string relativePath = report.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string filePath = Path.Combine(_env.WebRootPath, relativePath);

        if (!System.IO.File.Exists(filePath) && Directory.Exists("/data"))
        {
            string pPath = Path.Combine("/data", relativePath);
            if (System.IO.File.Exists(pPath)) filePath = pPath;
        }

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("Файл отчёта не найден на диске");
        }

        var contentType = report.ContentType ?? "application/octet-stream";
        return PhysicalFile(filePath, contentType, report.OriginalFileName);
    }

    // ==========================================
    // ОТЧЕТЫ, СТАТИСТИКА И РУЧНОЕ ОТКРЫТИЕ ЛЕКЦИЙ
    // ==========================================

    public async Task<IActionResult> Reports(int? disciplineId, int? groupId)
    {
        var disciplines = await _db.Disciplines.OrderBy(d => d.Title).ToListAsync();
        var groups = await _db.StudentGroups.OrderBy(g => g.Name).ToListAsync();

        int selectedDisciplineId = disciplineId ?? disciplines.FirstOrDefault()?.Id ?? 0;

        var model = new GroupProgressReportViewModel
        {
            SelectedDisciplineId = selectedDisciplineId,
            SelectedGroupId = groupId,
            Disciplines = disciplines,
            Groups = groups
        };

        if (selectedDisciplineId == 0)
        {
            return View(model);
        }

        var discipline = await _db.Disciplines
            .Include(d => d.Topics.OrderBy(t => t.OrderIndex))
                .ThenInclude(t => t.Lectures.OrderBy(l => l.OrderIndex))
                    .ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(d => d.Id == selectedDisciplineId);

        if (discipline == null) return View(model);

        model.DisciplineTitle = discipline.Title;

        var allLectures = discipline.Topics
            .OrderBy(t => t.OrderIndex)
            .SelectMany(t => t.Lectures.OrderBy(l => l.OrderIndex))
            .ToList();

        model.Lectures = allLectures.Select(l => new LectureReportColumnDto
        {
            Id = l.Id,
            Title = l.Title,
            OrderIndex = l.OrderIndex
        }).ToList();

        // Получаем список студентов
        var studentsQuery = _db.Users
            .Include(u => u.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .Where(u => u.Role == UserRole.Student && u.IsActive);

        if (groupId.HasValue)
        {
            studentsQuery = studentsQuery.Where(u => u.GroupMemberships.Any(gm => gm.GroupId == groupId.Value));
        }

        var students = await studentsQuery.OrderBy(u => u.FullName).ToListAsync();
        var studentIds = students.Select(s => s.Id).ToList();

        // Загружаем весь прогресс и попытки тестов
        var progresses = await _db.LectureProgresses
            .Where(lp => studentIds.Contains(lp.StudentId))
            .ToListAsync();

        var attempts = await _db.TestAttempts
            .Where(ta => studentIds.Contains(ta.StudentId))
            .ToListAsync();

        foreach (var s in students)
        {
            var studentProgressList = progresses.Where(p => p.StudentId == s.Id).ToList();
            var row = new StudentProgressRowDto
            {
                StudentId = s.Id,
                FullName = s.FullName,
                GroupName = s.GroupMemberships.FirstOrDefault()?.Group.Name ?? "Без группы",
                LectureStatuses = new Dictionary<int, StudentLectureStatusDto>()
            };

            int completedCount = 0;
            string lastCompleted = "Нет";
            string blockedAt = "";
            string blockedReason = "";
            bool foundBlocked = false;

            for (int i = 0; i < allLectures.Count; i++)
            {
                var lec = allLectures[i];
                var prog = studentProgressList.FirstOrDefault(p => p.LectureId == lec.Id);
                var lecAttempts = lec.Test != null 
                    ? attempts.Where(a => a.StudentId == s.Id && a.TestId == lec.Test.Id).ToList() 
                    : new List<TestAttempt>();

                bool isCompleted = prog?.IsCompleted ?? false;
                bool isManuallyUnlocked = prog?.IsManuallyUnlocked ?? false;

                if (isCompleted)
                {
                    completedCount++;
                    lastCompleted = lec.Title;
                }
                else if (!foundBlocked && !isManuallyUnlocked)
                {
                    foundBlocked = true;
                    blockedAt = lec.Title;

                    if (prog == null)
                    {
                        blockedReason = "Не приступал к изучению";
                    }
                    else if (!prog.IsControlQuestionsPassed)
                    {
                        blockedReason = "Не сданы контрольные вопросы";
                    }
                    else if (!prog.IsTestPassed)
                    {
                        int used = lecAttempts.Count;
                        int maxAtt = lec.Test?.MaxAttempts ?? 3;
                        blockedReason = used >= maxAtt ? $"Исчерпаны попытки теста ({used}/{maxAtt})" : $"Тест не сдан (лучший балл: {lecAttempts.Max(a => (int?)a.ScorePercent) ?? 0}%)";
                    }
                }

                row.LectureStatuses[lec.Id] = new StudentLectureStatusDto
                {
                    IsCompleted = isCompleted,
                    IsManuallyUnlocked = isManuallyUnlocked,
                    IsControlQuestionsPassed = prog?.IsControlQuestionsPassed ?? false,
                    IsTestPassed = prog?.IsTestPassed ?? false,
                    TestScorePercent = lecAttempts.Any() ? lecAttempts.Max(a => a.ScorePercent) : null,
                    AttemptsCount = lecAttempts.Count
                };
            }

            row.CompletedLecturesCount = completedCount;
            row.ProgressPercent = allLectures.Count > 0 ? (int)Math.Round((double)completedCount / allLectures.Count * 100) : 0;
            model.Students.Add(row);

            if (completedCount < allLectures.Count)
            {
                model.IncompleteStudents.Add(new StudentIncompleteDto
                {
                    StudentFullName = s.FullName,
                    GroupName = row.GroupName,
                    LastCompletedLecture = lastCompleted,
                    BlockedAtLecture = string.IsNullOrEmpty(blockedAt) ? allLectures.FirstOrDefault()?.Title ?? "" : blockedAt,
                    Reason = string.IsNullOrEmpty(blockedReason) ? "В процессе обучения" : blockedReason
                });
            }
        }

        // Анализ сложных вопросов тестов по дисциплине
        var testIds = allLectures.Where(l => l.Test != null).Select(l => l.Test!.Id).ToList();
        if (testIds.Any())
        {
            var testAttemptAnswers = await _db.TestAttemptAnswers
                .Include(taa => taa.Question)
                    .ThenInclude(tq => tq.Test)
                        .ThenInclude(t => t.Lecture)
                .Where(taa => testIds.Contains(taa.Question.TestId))
                .ToListAsync();

            model.DifficultQuestions = testAttemptAnswers
                .GroupBy(taa => taa.TestQuestionId)
                .Select(g =>
                {
                    var first = g.First();
                    int total = g.Count();
                    int incorrect = g.Count(a => !a.IsCorrect);
                    double errRate = total > 0 ? Math.Round((double)incorrect / total * 100, 1) : 0;
                    return new QuestionDifficultyDto
                    {
                        QuestionId = g.Key,
                        QuestionText = first.Question.Text,
                        TestTitle = first.Question.Test?.Title ?? "Тест",
                        LectureTitle = first.Question.Test?.Lecture?.Title ?? "Лекция",
                        TotalAnswers = total,
                        IncorrectAnswers = incorrect,
                        ErrorRatePercent = errRate
                    };
                })
                .Where(q => q.TotalAnswers >= 1)
                .OrderByDescending(q => q.ErrorRatePercent)
                .Take(5)
                .ToList();
        }

        return View(model);
    }

    // Экспорт матрицы успеваемости группы в CSV/Excel
    [HttpGet]
    public async Task<IActionResult> ExportGroupReport(int disciplineId, int? groupId)
    {
        var discipline = await _db.Disciplines
            .Include(d => d.Topics.OrderBy(t => t.OrderIndex))
                .ThenInclude(t => t.Lectures.OrderBy(l => l.OrderIndex))
            .FirstOrDefaultAsync(d => d.Id == disciplineId);

        if (discipline == null) return NotFound();

        var allLectures = discipline.Topics
            .OrderBy(t => t.OrderIndex)
            .SelectMany(t => t.Lectures.OrderBy(l => l.OrderIndex))
            .ToList();

        var studentsQuery = _db.Users
            .Include(u => u.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .Where(u => u.Role == UserRole.Student && u.IsActive);

        if (groupId.HasValue)
        {
            studentsQuery = studentsQuery.Where(u => u.GroupMemberships.Any(gm => gm.GroupId == groupId.Value));
        }

        var students = await studentsQuery.OrderBy(u => u.FullName).ToListAsync();

        var sb = new System.Text.StringBuilder();
        sb.Append("№;ФИО обучающегося;Группа;Пройдено лекций;Процент освоения");
        foreach (var lec in allLectures)
        {
            sb.Append($";Лекция {lec.OrderIndex}: {lec.Title.Replace(";", ",")}");
        }
        sb.AppendLine();

        int num = 1;
        foreach (var s in students)
        {
            var prog = await _lectureAccessService.GetDisciplineProgressAsync(s.Id, disciplineId);
            string gName = s.GroupMemberships.FirstOrDefault()?.Group.Name ?? "Без группы";
            sb.Append($"{num++};\"{s.FullName}\";\"{gName}\";{prog.CompletedLectures}/{prog.TotalLectures};{prog.ProgressPercent}%");

            foreach (var lec in allLectures)
            {
                var lp = prog.Lectures.FirstOrDefault(l => l.LectureId == lec.Id);
                string cell = "Закрыто";
                if (lp != null)
                {
                    if (lp.Status == LectureAccessStatus.Completed)
                    {
                        cell = lp.BestTestScore.HasValue ? $"Сдано ({lp.BestTestScore}%)" : "Изучено";
                    }
                    else if (lp.Status == LectureAccessStatus.Available)
                    {
                        cell = "Доступно";
                    }
                    else if (lp.IsManuallyUnlocked)
                    {
                        cell = "Открыто вручную";
                    }
                }
                sb.Append($";\"{cell}\"");
            }
            sb.AppendLine();
        }

        var encoding = new System.Text.UTF8Encoding(true); // UTF-8 with BOM
        byte[] bytes = encoding.GetBytes(sb.ToString());
        string safeDiscTitle = string.Concat(discipline.Title.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        string fileName = $"Успеваемость_{safeDiscTitle}_{DateTime.Now:yyyyMMdd_HHmm}.csv";

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    // Ручное открытие лекции преподавателем отдельному студенту
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ManualUnlock(ManualUnlockInputModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Необходимо указать студента, лекцию и причину ручного открытия";
            return RedirectToAction(nameof(Reports));
        }

        bool success = await _lectureAccessService.ManuallyUnlockLectureAsync(
            model.StudentId,
            model.LectureId,
            model.Reason,
            GetCurrentUserId(),
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        if (success)
        {
            TempData["SuccessMessage"] = "Лекция успешно открыта студенту вручную. Событие зарегистрировано в журнале аудита.";
        }
        else
        {
            TempData["ErrorMessage"] = "Ошибка при открытии лекции";
        }

        return RedirectToAction(nameof(Reports));
    }

    // Сброс попыток тестирования студента преподавателем
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAttempts(ResetAttemptsInputModel model)
    {
        var test = await _db.Tests.Include(t => t.Lecture).FirstOrDefaultAsync(t => t.Id == model.TestId);
        var student = await _db.Users.FindAsync(model.StudentId);

        if (test == null || student == null) return NotFound();

        var attempts = await _db.TestAttempts
            .Where(a => a.StudentId == model.StudentId && a.TestId == model.TestId)
            .ToListAsync();

        _db.TestAttempts.RemoveRange(attempts);

        // Сбрасываем флаг сданного теста в прогрессе лекции
        var prog = await _db.LectureProgresses.FirstOrDefaultAsync(lp => lp.StudentId == model.StudentId && lp.LectureId == test.LectureId);
        if (prog != null)
        {
            prog.IsTestPassed = false;
            prog.IsCompleted = false;
        }

        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "RESET_ATTEMPTS",
            $"Преподаватель сбросил {attempts.Count} попыток по тесту '{test.Title}' для студента {student.FullName}. Причина: {model.Reason ?? "по запросу"}",
            student.FullName,
            test.Lecture.Title,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = $"Попытки тестирования для студента {student.FullName} успешно сброшены";
        return RedirectToAction(nameof(Reports));
    }

    // Журнал аудита / событий
    public async Task<IActionResult> AuditLog()
    {
        var logs = await _db.EventLogs
            .Include(l => l.User)
            .OrderByDescending(l => l.Timestamp)
            .Take(100)
            .ToListAsync();

        return View(logs);
    }

    // Управление студентами и группами (с Радаром активности и досье)
    public async Task<IActionResult> Students(int? course = null)
    {
        var groups = await _db.StudentGroups
            .Include(g => g.Memberships)
                .ThenInclude(m => m.Student)
            .OrderBy(g => g.CourseNumber)
            .ThenBy(g => g.Name)
            .ToListAsync();

        var studentIds = groups.SelectMany(g => g.Memberships).Select(m => m.StudentId).Distinct().ToList();

        var progresses = await _db.LectureProgresses
            .Where(lp => studentIds.Contains(lp.StudentId) && lp.IsCompleted)
            .ToListAsync();

        var attempts = await _db.TestAttempts
            .Include(ta => ta.Test)
                .ThenInclude(t => t.Lecture)
            .Where(ta => studentIds.Contains(ta.StudentId) && ta.CompletedAt != null)
            .OrderByDescending(ta => ta.CompletedAt)
            .ToListAsync();

        var model = new TeacherStudentsPageViewModel
        {
            SelectedCourse = course.GetValueOrDefault(0)
        };

        foreach (var g in groups)
        {
            var gDto = new GroupWithStudentsDto
            {
                GroupId = g.Id,
                GroupName = g.Name,
                Description = g.Description,
                InviteCode = g.InviteCode,
                CourseNumber = g.CourseNumber,
                StudyDurationYears = g.StudyDurationYears,
                AdmissionYear = g.AdmissionYear
            };

            foreach (var m in g.Memberships.OrderBy(m => m.Student.FullName))
            {
                int sId = m.StudentId;
                int completedLecs = progresses.Count(p => p.StudentId == sId);
                var studentAttempts = attempts.Where(a => a.StudentId == sId).ToList();
                int testsPassed = studentAttempts.Count(a => a.IsPassed);
                double? avgScore = studentAttempts.Any() ? Math.Round(studentAttempts.Average(a => a.ScorePercent), 1) : null;

                string radarStatus;
                string radarBadge;

                bool hasFailedUnresolved = studentAttempts.Any(a => !a.IsPassed && a.AttemptNumber >= 3);
                if (hasFailedUnresolved)
                {
                    radarStatus = "Требует внимания";
                    radarBadge = "bg-danger";
                }
                else if (avgScore.HasValue && avgScore.Value >= 85 && completedLecs >= 1)
                {
                    radarStatus = "Отличный темп";
                    radarBadge = "bg-success";
                }
                else if (avgScore.HasValue && avgScore.Value >= 65)
                {
                    radarStatus = "В графике";
                    radarBadge = "bg-primary";
                }
                else
                {
                    radarStatus = "Отстает";
                    radarBadge = "bg-warning text-dark";
                }

                gDto.Students.Add(new StudentCardProfileDto
                {
                    Id = m.Student.Id,
                    FullName = m.Student.FullName,
                    Username = m.Student.Username,
                    Email = m.Student.Email,
                    IsEmailConfirmed = m.Student.IsEmailConfirmed,
                    CompletedLecturesCount = completedLecs,
                    TestsPassedCount = testsPassed,
                    AverageScorePercent = avgScore,
                    RadarStatus = radarStatus,
                    RadarBadgeClass = radarBadge,
                    RecentAttempts = studentAttempts.Take(5).Select(a => new StudentAttemptHistoryDto
                    {
                        TestTitle = a.Test?.Title ?? "Тест",
                        LectureTitle = a.Test?.Lecture?.Title ?? "Лекция",
                        AttemptNumber = a.AttemptNumber,
                        ScorePercent = a.ScorePercent,
                        IsPassed = a.IsPassed,
                        CompletedAt = a.CompletedAt
                    }).ToList()
                });
            }

            model.Groups.Add(gDto);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGroup(string name, string? description, int? courseNumber, int? studyDurationYears, string? inviteCode)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var trimmedName = name.Trim();
            int finalCourse = courseNumber.GetValueOrDefault(1);
            if (!courseNumber.HasValue && trimmedName.Length >= 1 && char.IsDigit(trimmedName[0]))
            {
                int d = trimmedName[0] - '0';
                if (d >= 1 && d <= 4) finalCourse = d;
            }

            int finalDuration = studyDurationYears.GetValueOrDefault(4);
            if (finalDuration != 3 && finalDuration != 4) finalDuration = 4;

            string finalCode;
            if (!string.IsNullOrWhiteSpace(inviteCode))
            {
                finalCode = System.Text.RegularExpressions.Regex.Replace(inviteCode.Trim().ToUpper(), @"\s+", "-");
            }
            else
            {
                finalCode = Services.InviteCodeGenerator.GenerateCode(trimmedName);
            }

            // Гарантируем уникальность
            while (await _db.StudentGroups.AnyAsync(g => g.InviteCode.ToUpper() == finalCode))
            {
                finalCode = Services.InviteCodeGenerator.GenerateCode(trimmedName);
            }

            var group = new StudentGroup
            {
                Name = trimmedName,
                Description = description?.Trim(),
                InviteCode = finalCode,
                CourseNumber = finalCourse,
                StudyDurationYears = finalDuration,
                AdmissionYear = DateTime.Now.Year
            };
            _db.StudentGroups.Add(group);
            await _db.SaveChangesAsync();

            await _eventLogger.LogActionAsync(
                null,
                "GROUP_CREATE",
                $"Создана учебная группа {group.Name} ({group.CourseNumber} курс, {group.StudyDurationYears} года обучения, код: {group.InviteCode})",
                User.Identity?.Name ?? "Преподаватель",
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = $"Группа {group.Name} ({group.CourseNumber} курс, {group.StudyDurationYears} года обучения) успешно создана! Инвайт-код: {group.InviteCode}";
        }
        return RedirectToAction(nameof(Students));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var group = await _db.StudentGroups
            .Include(g => g.Memberships)
            .Include(g => g.DisciplineGroups)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group != null)
        {
            string groupName = group.Name;
            _db.StudentGroups.Remove(group);
            await _db.SaveChangesAsync();

            await _eventLogger.LogActionAsync(
                null,
                "GROUP_DELETE",
                $"Удалена учебная группа {groupName}",
                User.Identity?.Name ?? "Преподаватель",
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = $"Группа {groupName} успешно удалена.";
        }
        return RedirectToAction(nameof(Students));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetStudentPassword(int studentId, string? newPassword)
    {
        var student = await _db.Users.FirstOrDefaultAsync(u => u.Id == studentId && u.Role == UserRole.Student);
        if (student == null)
        {
            TempData["ErrorMessage"] = "Студент не найден.";
            return RedirectToAction(nameof(Students));
        }

        var passwordToSet = !string.IsNullOrWhiteSpace(newPassword)
            ? newPassword.Trim()
            : $"Edu{System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999)}!";

        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
        student.PasswordHash = hasher.HashPassword(student, passwordToSet);
        student.PasswordResetCode = null;
        student.PasswordResetExpiresAt = null;
        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "TEACHER_RESET_PASSWORD",
            $"Преподаватель сбросил пароль для студента {student.FullName} ({student.Username})",
            User.Identity?.Name ?? "Преподаватель",
            null,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = $"Пароль для студента «{student.FullName}» успешно обновлен на: {passwordToSet}";
        return RedirectToAction(nameof(Students));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmStudentEmail(int studentId)
    {
        var student = await _db.Users.FirstOrDefaultAsync(u => u.Id == studentId && u.Role == UserRole.Student);
        if (student == null)
        {
            TempData["ErrorMessage"] = "Студент не найден.";
            return RedirectToAction(nameof(Students));
        }

        student.IsEmailConfirmed = true;
        student.EmailConfirmationCode = null;
        student.EmailConfirmationExpiresAt = null;
        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "TEACHER_CONFIRM_EMAIL",
            $"Преподаватель вручную подтвердил адрес почты {student.Email} для студента {student.FullName}",
            User.Identity?.Name ?? "Преподаватель",
            null,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = $"Почта студента «{student.FullName}» ({student.Email}) успешно подтверждена преподавателем!";
        return RedirectToAction(nameof(Students));
    }

    // ==========================================
    // РЕЗЕРВНОЕ КОПИРОВАНИЕ И ВОССТАНОВЛЕНИЕ
    // ==========================================

    [HttpGet]
    public IActionResult Backup()
    {
        return View();
    }

    // Скачать резервную копию базы данных SQLite
    [HttpGet]
    public IActionResult DownloadDatabaseBackup()
    {
        // Имя файла базы данных SQLite
        string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "educabinet.db");
        if (!System.IO.File.Exists(dbPath))
        {
            TempData["ErrorMessage"] = "Файл базы данных educabinet.db не найден на диске";
            return RedirectToAction(nameof(Backup));
        }

        string backupFileName = $"EduCabinet_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        byte[] bytes = System.IO.File.ReadAllBytes(dbPath);

        return File(bytes, "application/octet-stream", backupFileName);
    }

    // Скачать полный архив системы (БД + uploads)
    [HttpGet]
    public async Task<IActionResult> DownloadFullBackup()
    {
        string zipFileName = $"EduCabinet_FullBackup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var tempZipPath = Path.Combine(Path.GetTempPath(), zipFileName);

        if (System.IO.File.Exists(tempZipPath)) System.IO.File.Delete(tempZipPath);

        using (var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
        {
            string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "educabinet.db");
            if (System.IO.File.Exists(dbPath))
            {
                archive.CreateEntryFromFile(dbPath, "educabinet.db");
            }

            string uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            if (Directory.Exists(uploadsDir))
            {
                foreach (var file in Directory.GetFiles(uploadsDir))
                {
                    archive.CreateEntryFromFile(file, Path.Combine("uploads", Path.GetFileName(file)));
                }
            }
        }

        byte[] zipBytes = await System.IO.File.ReadAllBytesAsync(tempZipPath);
        System.IO.File.Delete(tempZipPath);

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "BACKUP_CREATED",
            $"Создана и выгружена полная резервная копия системы ({zipFileName})",
            null,
            null,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        return File(zipBytes, "application/zip", zipFileName);
    }

    // Восстановление базы данных из резервной копии
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreDatabase(IFormFile backupFile)
    {
        if (backupFile == null || backupFile.Length == 0)
        {
            TempData["ErrorMessage"] = "Выберите файл резервной копии (.db или .zip)";
            return RedirectToAction(nameof(Backup));
        }

        if (backupFile.Length > MaxBackupSizeBytes)
        {
            TempData["ErrorMessage"] = "Размер файла резервной копии не должен превышать 100 МБ.";
            return RedirectToAction(nameof(Backup));
        }

        var ext = Path.GetExtension(backupFile.FileName);
        if (!string.Equals(ext, ".db", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "Недопустимый формат файла. Разрешены только файлы с расширениями .db и .zip.";
            return RedirectToAction(nameof(Backup));
        }

        try
        {
            string currentDb = Path.Combine(Directory.GetCurrentDirectory(), "educabinet.db");
            string backupDb = Path.Combine(Directory.GetCurrentDirectory(), $"educabinet_pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.bak");

            // Делаем снимок перед заменой
            if (System.IO.File.Exists(currentDb))
            {
                System.IO.File.Copy(currentDb, backupDb, true);
            }

            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tempZip = Path.GetTempFileName();
                using (var fs = new FileStream(tempZip, FileMode.Create))
                {
                    await backupFile.CopyToAsync(fs);
                }

                using (var archive = ZipFile.OpenRead(tempZip))
                {
                    var dbEntry = archive.GetEntry("educabinet.db");
                    if (dbEntry != null)
                    {
                        dbEntry.ExtractToFile(currentDb, overwrite: true);
                    }

                    string uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                    if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("uploads/") && !string.IsNullOrEmpty(entry.Name))
                        {
                            var targetPath = Path.Combine(uploadsDir, entry.Name);
                            entry.ExtractToFile(targetPath, overwrite: true);
                        }
                    }
                }
                System.IO.File.Delete(tempZip);
            }
            else
            {
                using (var fs = new FileStream(currentDb, FileMode.Create))
                {
                    await backupFile.CopyToAsync(fs);
                }
            }

            await _eventLogger.LogActionAsync(
                GetCurrentUserId(),
                "RESTORE_EXECUTED",
                $"Выполнено восстановление системы из резервной копии '{backupFile.FileName}'",
                null,
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Система успешно восстановлена из резервной копии!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Ошибка восстановления: {ex.Message}";
        }

        return RedirectToAction(nameof(Backup));
    }

    // ==========================================
    // ИНВАЙТЫ ПРЕПОДАВАТЕЛЕЙ (ТОЛЬКО ДЛЯ АДМИНИСТРАТОРА)
    // ==========================================

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> StaffInvites()
    {
        var invites = await _db.SystemInviteTokens
            .Include(t => t.UsedByUser)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return View(invites);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTeacherInvite(string? note)
    {
        var randomCode = System.Security.Cryptography.RandomNumberGenerator.GetString(
            "ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6);
        var tokenString = $"TEACH-TECHHUB-{randomCode}";

        var invite = new SystemInviteToken
        {
            Token = tokenString,
            TargetRole = UserRole.Teacher,
            Note = string.IsNullOrWhiteSpace(note) ? "Инвайт преподавателя" : note.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsUsed = false
        };

        _db.SystemInviteTokens.Add(invite);
        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            GetCurrentUserId(),
            "INVITE_CREATE",
            $"Администратор создал инвайт-код для преподавателя: {tokenString}",
            User.Identity?.Name ?? "Admin",
            null,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        TempData["SuccessMessage"] = $"Инвайт-код для преподавателя успешно создан: {tokenString}";
        return RedirectToAction(nameof(StaffInvites));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStaffInvite(int id)
    {
        var invite = await _db.SystemInviteTokens.FindAsync(id);
        if (invite != null)
        {
            _db.SystemInviteTokens.Remove(invite);
            await _db.SaveChangesAsync();

            await _eventLogger.LogActionAsync(
                GetCurrentUserId(),
                "INVITE_DELETE",
                $"Администратор удалил инвайт-код {invite.Token}",
                User.Identity?.Name ?? "Admin",
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Инвайт успешно удален.";
        }
        return RedirectToAction(nameof(StaffInvites));
    }
}
