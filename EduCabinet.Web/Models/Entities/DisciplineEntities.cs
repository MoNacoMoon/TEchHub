namespace EduCabinet.Web.Models.Entities;

public class Discipline
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
    public ICollection<DisciplineGroup> DisciplineGroups { get; set; } = new List<DisciplineGroup>();
    public ICollection<CourseAnnouncement> Announcements { get; set; } = new List<CourseAnnouncement>();
}

public class CourseAnnouncement
{
    public int Id { get; set; }
    public int DisciplineId { get; set; }
    public Discipline Discipline { get; set; } = null!;

    public int AuthorTeacherId { get; set; }
    public User AuthorTeacher { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Topic
{
    public int Id { get; set; }
    public int DisciplineId { get; set; }
    public Discipline Discipline { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OrderIndex { get; set; } = 1;

    public ICollection<Lecture> Lectures { get; set; } = new List<Lecture>();
}

public class Lecture
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ContentHtml { get; set; } = string.Empty; // Теоретический материал

    public string? AttachmentFilePath { get; set; } // Путь к файлу на сервере
    public string? AttachmentOriginalName { get; set; } // Оригинальное имя (PDF, DOCX)
    public string? AttachmentContentType { get; set; }

    public int OrderIndex { get; set; } = 1;
    public bool RequiresManualReview { get; set; } = false; // Требуется проверка преподавателем развёрнутых ответов
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<ControlQuestion> ControlQuestions { get; set; } = new List<ControlQuestion>();
    public Test? Test { get; set; }
    public ICollection<LectureProgress> Progresses { get; set; } = new List<LectureProgress>();
}

public class LectureProgress
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;

    public int LectureId { get; set; }
    public Lecture Lecture { get; set; } = null!;

    public bool IsControlQuestionsPassed { get; set; } = false;
    public bool IsTestPassed { get; set; } = false;
    public bool IsCompleted { get; set; } = false;

    public bool IsManuallyUnlocked { get; set; } = false; // Ручное открытие преподавателем
    public string? ManualUnlockReason { get; set; }
    public DateTime? ManuallyUnlockedAt { get; set; }

    public DateTime? FirstAccessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
