namespace EduCabinet.Web.Models.Entities;

public enum QuestionType
{
    SingleChoice = 1,   // Выбор одного правильного ответа
    MultipleChoice = 2, // Выбор нескольких правильных ответов
    ShortText = 3,      // Ввод краткого текстового ответа
    Matching = 4,       // На установление соответствия
    FillBlank = 5,      // Ввод пропущенного слова или термина
    OpenEnded = 6       // Развернутый ответ (ручная проверка преподавателем)
}

public class ControlQuestion
{
    public int Id { get; set; }
    public int LectureId { get; set; }
    public Lecture Lecture { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public int OrderIndex { get; set; } = 1;
    public string? Explanation { get; set; } // Пояснение/подсказка
    public string? CorrectTextAnswer { get; set; } // Для ShortText и FillBlank (основной правильный ответ)
    public string? SynonymsCsv { get; set; } // Допустимые варианты через запятую

    public ICollection<ControlQuestionOption> Options { get; set; } = new List<ControlQuestionOption>();
    public ICollection<StudentControlAnswer> StudentAnswers { get; set; } = new List<StudentControlAnswer>();
}

public class ControlQuestionOption
{
    public int Id { get; set; }
    public int ControlQuestionId { get; set; }
    public ControlQuestion ControlQuestion { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; } = false;
    public string? MatchingKey { get; set; } // Правая часть соответствия для типа Matching
    public int OrderIndex { get; set; } = 1;
}

public class StudentControlAnswer
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;

    public int ControlQuestionId { get; set; }
    public ControlQuestion ControlQuestion { get; set; } = null!;

    public string GivenAnswer { get; set; } = string.Empty; // Текст ответа или JSON с выбранными ID/соответствиями
    public bool IsCorrect { get; set; } = false;
    public bool IsReviewedByTeacher { get; set; } = false; // Для OpenEnded
    public string? TeacherComment { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public class Test
{
    public int Id { get; set; }
    public int LectureId { get; set; }
    public Lecture Lecture { get; set; } = null!;

    public string Title { get; set; } = "Тест к лекции";
    public string? Description { get; set; }
    public int PassingScorePercent { get; set; } = 70; // По умолчанию 70%
    public int MaxAttempts { get; set; } = 3; // По умолчанию 3 попытки
    public int? TimeLimitMinutes { get; set; } = 15; // Лимит времени в минутах (null = без лимита)
    public bool ShuffleQuestions { get; set; } = true;
    public bool ShuffleOptions { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public ICollection<TestQuestion> Questions { get; set; } = new List<TestQuestion>();
    public ICollection<TestAttempt> Attempts { get; set; } = new List<TestAttempt>();
}

public class TestQuestion
{
    public int Id { get; set; }
    public int TestId { get; set; }
    public Test Test { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;
    public int OrderIndex { get; set; } = 1;
    public int Points { get; set; } = 1;
    public string? CorrectTextAnswer { get; set; }
    public string? SynonymsCsv { get; set; }

    public ICollection<TestQuestionOption> Options { get; set; } = new List<TestQuestionOption>();
    public ICollection<TestAttemptAnswer> AttemptAnswers { get; set; } = new List<TestAttemptAnswer>();
}

public class TestQuestionOption
{
    public int Id { get; set; }
    public int TestQuestionId { get; set; }
    public TestQuestion TestQuestion { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; } = false;
    public string? MatchingKey { get; set; } // Соответствие
    public int OrderIndex { get; set; } = 1;
}

public class TestAttempt
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;

    public int TestId { get; set; }
    public Test Test { get; set; } = null!;

    public int AttemptNumber { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public int TotalQuestions { get; set; } = 0;
    public int CorrectQuestions { get; set; } = 0;
    public int ScorePercent { get; set; } = 0;
    public bool IsPassed { get; set; } = false;

    public ICollection<TestAttemptAnswer> Answers { get; set; } = new List<TestAttemptAnswer>();
}

public class TestAttemptAnswer
{
    public int Id { get; set; }
    public int TestAttemptId { get; set; }
    public TestAttempt Attempt { get; set; } = null!;

    public int TestQuestionId { get; set; }
    public TestQuestion Question { get; set; } = null!;

    public string GivenAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; } = false;
    public int PointsEarned { get; set; } = 0;
}
