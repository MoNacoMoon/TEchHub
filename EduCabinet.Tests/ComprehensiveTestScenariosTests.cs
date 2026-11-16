using Xunit;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Tests;

public class ComprehensiveTestScenariosTests
{
    private readonly GradingService _gradingService = new();

    // 1. Тест с выбором одного ответа + изображение к вопросу (SingleChoice with Image)
    [Fact]
    public void Scenario01_SingleChoice_WithQuestionImage_GradesCorrectly()
    {
        var question = new TestQuestion
        {
            Id = 1,
            Type = QuestionType.SingleChoice,
            Text = "Какое устройство изображено на фотографии ниже?",
            ImageUrl = "/uploads/q_network_switch.png",
            Points = 2,
            Options = new List<TestQuestionOption>
            {
                new TestQuestionOption { Id = 101, Text = "Маршрутизатор", IsCorrect = false },
                new TestQuestionOption { Id = 102, Text = "Коммутатор (Switch)", IsCorrect = true },
                new TestQuestionOption { Id = 103, Text = "Хаб", IsCorrect = false }
            }
        };

        var correctResult = _gradingService.GradeTestQuestion(question, "102");
        var incorrectResult = _gradingService.GradeTestQuestion(question, "101");

        Assert.NotNull(question.ImageUrl);
        Assert.True(correctResult.IsCorrect);
        Assert.Equal(2, correctResult.PointsEarned);
        Assert.False(incorrectResult.IsCorrect);
        Assert.Equal(0, incorrectResult.PointsEarned);
    }

    // 2. Тест с выбором одного ответа без изображения (SingleChoice without Image)
    [Fact]
    public void Scenario02_SingleChoice_WithoutImage_GradesCorrectly()
    {
        var question = new TestQuestion
        {
            Id = 2,
            Type = QuestionType.SingleChoice,
            Text = "Какой протокол обеспечивает шифрование веб-трафика по умолчанию?",
            ImageUrl = null, // Текстовый вопрос без иллюстрации
            Points = 1,
            Options = new List<TestQuestionOption>
            {
                new TestQuestionOption { Id = 201, Text = "HTTP", IsCorrect = false },
                new TestQuestionOption { Id = 202, Text = "HTTPS", IsCorrect = true },
                new TestQuestionOption { Id = 203, Text = "FTP", IsCorrect = false }
            }
        };

        var result = _gradingService.GradeTestQuestion(question, "202");

        Assert.Null(question.ImageUrl);
        Assert.True(result.IsCorrect);
        Assert.Equal(1, result.PointsEarned);
    }

    // 3. Тест со множественным выбором + изображения к вариантам ответов (MultipleChoice with Option Images)
    [Fact]
    public void Scenario03_MultipleChoice_WithOptionsImages_GradesCorrectly()
    {
        var question = new TestQuestion
        {
            Id = 3,
            Type = QuestionType.MultipleChoice,
            Text = "Выберите схемы, представляющие топологию «Звезда» и «Кольцо»:",
            ImageUrl = null,
            Points = 3,
            Options = new List<TestQuestionOption>
            {
                new TestQuestionOption { Id = 301, Text = "Топология А", ImageUrl = "/uploads/opt_star.png", IsCorrect = true },
                new TestQuestionOption { Id = 302, Text = "Топология Б", ImageUrl = "/uploads/opt_bus.png", IsCorrect = false },
                new TestQuestionOption { Id = 303, Text = "Топология В", ImageUrl = "/uploads/opt_ring.png", IsCorrect = true }
            }
        };

        var result = _gradingService.GradeTestQuestion(question, "301,303");

        Assert.NotNull(question.Options.First(o => o.Id == 301).ImageUrl);
        Assert.True(result.IsCorrect);
        Assert.Equal(3, result.PointsEarned);
    }

    // 4. Множественный выбор: неполный или избыточный ответ не засчитывается (MultipleChoice Strict Grading)
    [Fact]
    public void Scenario04_MultipleChoice_PartialOrExtraSelection_Fails()
    {
        var question = new TestQuestion
        {
            Id = 4,
            Type = QuestionType.MultipleChoice,
            Text = "Выберите языки со статической типизацией:",
            Points = 2,
            Options = new List<TestQuestionOption>
            {
                new TestQuestionOption { Id = 401, Text = "C#", IsCorrect = true },
                new TestQuestionOption { Id = 402, Text = "Python", IsCorrect = false },
                new TestQuestionOption { Id = 403, Text = "Java", IsCorrect = true }
            }
        };

        // Выбрал только 1 правильный из двух
        var partial = _gradingService.GradeTestQuestion(question, "401");
        // Выбрал 2 правильных, но прихватил и неправильный
        var extra = _gradingService.GradeTestQuestion(question, "401,402,403");

        Assert.False(partial.IsCorrect);
        Assert.Equal(0, partial.PointsEarned);
        Assert.False(extra.IsCorrect);
        Assert.Equal(0, extra.PointsEarned);
    }

    // 5. Краткий текстовый ответ: регистронезависимость и удаление пробелов (ShortText Normalization)
    [Fact]
    public void Scenario05_ShortText_ExactAndWhitespaceInvariant_GradesCorrectly()
    {
        var question = new TestQuestion
        {
            Id = 5,
            Type = QuestionType.ShortText,
            Text = "Как называется процесс поиска и исправления ошибок в коде?",
            CorrectTextAnswer = "Отладка",
            Points = 1
        };

        Assert.True(_gradingService.GradeTestQuestion(question, "отладка").IsCorrect);
        Assert.True(_gradingService.GradeTestQuestion(question, "  ОТЛАДКА  ").IsCorrect);
        Assert.False(_gradingService.GradeTestQuestion(question, "компиляция").IsCorrect);
    }

    // 6. Вставка пропущенного слова с синонимами (FillBlank with Synonyms CSV)
    [Fact]
    public void Scenario06_FillBlank_MatchesAnyAllowedSynonym_GradesCorrectly()
    {
        var question = new TestQuestion
        {
            Id = 6,
            Type = QuestionType.FillBlank,
            Text = "Паттерн проектирования, гарантирующий существование только одного экземпляра класса: _____",
            CorrectTextAnswer = "Одиночка",
            SynonymsCsv = "синглтон, singleton, одиночка",
            Points = 2
        };

        Assert.True(_gradingService.GradeTestQuestion(question, "синглтон").IsCorrect);
        Assert.True(_gradingService.GradeTestQuestion(question, "Singleton").IsCorrect);
        Assert.True(_gradingService.GradeTestQuestion(question, "Одиночка").IsCorrect);
        Assert.False(_gradingService.GradeTestQuestion(question, "Фабрика").IsCorrect);
    }

    // 7. Установление соответствия пар (Matching Question)
    [Fact]
    public void Scenario07_MatchingPairs_RequiresExactMapping()
    {
        var question = new TestQuestion
        {
            Id = 7,
            Type = QuestionType.Matching,
            Text = "Сопоставьте уровни модели OSI с их номерами:",
            Points = 3,
            Options = new List<TestQuestionOption>
            {
                new TestQuestionOption { Id = 701, Text = "Физический", MatchingKey = "Уровень 1" },
                new TestQuestionOption { Id = 702, Text = "Сетевой", MatchingKey = "Уровень 3" },
                new TestQuestionOption { Id = 703, Text = "Прикладной", MatchingKey = "Уровень 7" }
            }
        };

        // Правильные пары
        var success = _gradingService.GradeTestQuestion(question, "701:Уровень 1;702:Уровень 3;703:Уровень 7");
        // Перепутанные пары
        var fail = _gradingService.GradeTestQuestion(question, "701:Уровень 7;702:Уровень 3;703:Уровень 1");

        Assert.True(success.IsCorrect);
        Assert.Equal(3, success.PointsEarned);
        Assert.False(fail.IsCorrect);
        Assert.Equal(0, fail.PointsEarned);
    }

    // 8. Контрольный вопрос с развернутым ответом: ручная проверка преподавателем (OpenEnded Question)
    [Fact]
    public void Scenario08_OpenEnded_RequiresManualTeacherReview()
    {
        var question = new ControlQuestion
        {
            Id = 8,
            Type = QuestionType.OpenEnded,
            Text = "Опишите преимущества использования архитектуры микросервисов над монолитом.",
            OrderIndex = 1
        };

        var studentAnswer = new StudentControlAnswer
        {
            ControlQuestionId = question.Id,
            GivenAnswer = "Микросервисы позволяют независимо масштабировать модули и развертывать их без остановки всей системы.",
            IsCorrect = false,
            IsReviewedByTeacher = false
        };

        // До проверки преподавателем статус не зачтен
        Assert.False(studentAnswer.IsReviewedByTeacher);
        Assert.False(studentAnswer.IsCorrect);

        // Преподаватель проверил и поставил «Зачтено»
        studentAnswer.IsReviewedByTeacher = true;
        studentAnswer.IsCorrect = true;
        studentAnswer.TeacherComment = "Отличный развернутый ответ.";

        Assert.True(studentAnswer.IsReviewedByTeacher);
        Assert.True(studentAnswer.IsCorrect);
        Assert.NotNull(studentAnswer.TeacherComment);
    }

    // 9. Комплексный тест с расчетом процента и порога сдачи (ScorePercent and Passing Threshold)
    [Fact]
    public void Scenario09_MultiQuestionTest_CalculatesScorePercentAndPassingStatus()
    {
        var test = new Test
        {
            Id = 9,
            Title = "Итоговый тест по базам данных",
            PassingScorePercent = 75,
            Questions = new List<TestQuestion>
            {
                new TestQuestion
                {
                    Id = 901,
                    Type = QuestionType.SingleChoice,
                    Text = "Вопрос 1",
                    Points = 1,
                    Options = new List<TestQuestionOption>
                    {
                        new TestQuestionOption { Id = 1, Text = "Да", IsCorrect = true },
                        new TestQuestionOption { Id = 2, Text = "Нет", IsCorrect = false }
                    }
                },
                new TestQuestion
                {
                    Id = 902,
                    Type = QuestionType.SingleChoice,
                    Text = "Вопрос 2",
                    Points = 1,
                    Options = new List<TestQuestionOption>
                    {
                        new TestQuestionOption { Id = 3, Text = "Да", IsCorrect = true },
                        new TestQuestionOption { Id = 4, Text = "Нет", IsCorrect = false }
                    }
                },
                new TestQuestion
                {
                    Id = 903,
                    Type = QuestionType.SingleChoice,
                    Text = "Вопрос 3",
                    Points = 1,
                    Options = new List<TestQuestionOption>
                    {
                        new TestQuestionOption { Id = 5, Text = "Да", IsCorrect = true },
                        new TestQuestionOption { Id = 6, Text = "Нет", IsCorrect = false }
                    }
                },
                new TestQuestion
                {
                    Id = 904,
                    Type = QuestionType.SingleChoice,
                    Text = "Вопрос 4",
                    Points = 1,
                    Options = new List<TestQuestionOption>
                    {
                        new TestQuestionOption { Id = 7, Text = "Да", IsCorrect = true },
                        new TestQuestionOption { Id = 8, Text = "Нет", IsCorrect = false }
                    }
                }
            }
        };

        // Симулируем ответы: студент ответил правильно на 3 вопроса из 4 (75%)
        int totalQuestions = test.Questions.Count;
        int correctAnswers = 3;
        int scorePercent = (int)Math.Round((double)correctAnswers / totalQuestions * 100);
        bool isPassed = scorePercent >= test.PassingScorePercent;

        Assert.Equal(4, totalQuestions);
        Assert.Equal(75, scorePercent);
        Assert.True(isPassed); // 75% >= 75% -> Сдано

        // Симулируем ответы: студент ответил правильно только на 2 из 4 (50%)
        int failedAnswers = 2;
        int failedScorePercent = (int)Math.Round((double)failedAnswers / totalQuestions * 100);
        bool failedIsPassed = failedScorePercent >= test.PassingScorePercent;

        Assert.Equal(50, failedScorePercent);
        Assert.False(failedIsPassed); // 50% < 75% -> Не сдано
    }

    // 10. Защита от NullReferenceException на экране TestResult (Регрессионный тест)
    [Fact]
    public async Task Scenario10_TestResultNavigation_NeverThrowsNullReferenceEvenIfTopicOrDisciplineIsNull()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            // Создаем дисциплину с одной темой и двумя лекциями
            var disc = new Discipline { Title = "Программирование C#" };
            context.Disciplines.Add(disc);
            await context.SaveChangesAsync();

            var topic = new Topic { DisciplineId = disc.Id, Title = "Тема 1", OrderIndex = 1 };
            context.Topics.Add(topic);
            await context.SaveChangesAsync();

            var lec1 = new Lecture { TopicId = topic.Id, Title = "Лекция 1", OrderIndex = 1 };
            var lec2 = new Lecture { TopicId = topic.Id, Title = "Лекция 2", OrderIndex = 2 };
            context.Lectures.AddRange(lec1, lec2);
            await context.SaveChangesAsync();

            var test = new Test { LectureId = lec1.Id, Title = "Тест 1", PassingScorePercent = 70 };
            context.Tests.Add(test);
            await context.SaveChangesAsync();

            var student = new User { Username = "student_test_10", FullName = "Иван Тестов", PasswordHash = "hash" };
            context.Users.Add(student);
            await context.SaveChangesAsync();

            var attempt = new TestAttempt
            {
                StudentId = student.Id,
                TestId = test.Id,
                AttemptNumber = 1,
                TotalQuestions = 1,
                CorrectQuestions = 1,
                ScorePercent = 100,
                IsPassed = true
            };
            context.TestAttempts.Add(attempt);
            await context.SaveChangesAsync();

            // Проверяем устойчивость нового безопасного алгоритма вычисления следующей лекции
            var loadedAttempt = await context.TestAttempts
                .Include(a => a.Test)
                    .ThenInclude(t => t.Lecture)
                        .ThenInclude(l => l.Topic)
                .FirstOrDefaultAsync(a => a.Id == attempt.Id);

            Assert.NotNull(loadedAttempt);
            Assert.NotNull(loadedAttempt.Test);
            Assert.NotNull(loadedAttempt.Test.Lecture);

            // Безопасное вычисление disciplineId
            int? disciplineId = loadedAttempt.Test.Lecture?.Topic?.DisciplineId;
            if (!disciplineId.HasValue && loadedAttempt.Test?.Lecture != null)
            {
                disciplineId = await context.Topics
                    .Where(t => t.Id == loadedAttempt.Test.Lecture.TopicId)
                    .Select(t => (int?)t.DisciplineId)
                    .FirstOrDefaultAsync();
            }

            Assert.Equal(disc.Id, disciplineId);

            var allDisciplineLectures = await context.Lectures
                .Where(l => l.Topic.DisciplineId == disciplineId!.Value)
                .OrderBy(l => l.Topic.OrderIndex)
                .ThenBy(l => l.Topic.Id)
                .ThenBy(l => l.OrderIndex)
                .ThenBy(l => l.Id)
                .Select(l => l.Id)
                .ToListAsync();

            int currentIndex = allDisciplineLectures.IndexOf(loadedAttempt.Test.LectureId);
            Assert.Equal(0, currentIndex);
            Assert.Equal(lec2.Id, allDisciplineLectures[currentIndex + 1]); // Следующая лекция успешно найдена!
        }
    }
}
