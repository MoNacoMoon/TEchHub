using Xunit;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Tests;

public class GradingServiceTests
{
    private readonly GradingService _service = new();

    [Fact]
    public void SingleChoice_GradesCorrectly()
    {
        var q = new ControlQuestion
        {
            Type = QuestionType.SingleChoice,
            Text = "Вопрос",
            Options = new List<ControlQuestionOption>
            {
                new ControlQuestionOption { Id = 1, Text = "Неверно", IsCorrect = false },
                new ControlQuestionOption { Id = 2, Text = "Верно", IsCorrect = true }
            }
        };

        var correctResult = _service.GradeControlQuestion(q, "2");
        var wrongResult = _service.GradeControlQuestion(q, "1");

        Assert.True(correctResult.IsCorrect);
        Assert.False(wrongResult.IsCorrect);
    }

    [Fact]
    public void MultipleChoice_RequiresAllCorrectAndNoIncorrect()
    {
        var q = new ControlQuestion
        {
            Type = QuestionType.MultipleChoice,
            Text = "Вопрос",
            Options = new List<ControlQuestionOption>
            {
                new ControlQuestionOption { Id = 10, Text = "Правильный 1", IsCorrect = true },
                new ControlQuestionOption { Id = 20, Text = "Неправильный", IsCorrect = false },
                new ControlQuestionOption { Id = 30, Text = "Правильный 2", IsCorrect = true }
            }
        };

        // Точное совпадение
        var matchResult = _service.GradeControlQuestion(q, "10,30");
        Assert.True(matchResult.IsCorrect);

        // Неполный ответ (только один правильный)
        var partialResult = _service.GradeControlQuestion(q, "10");
        Assert.False(partialResult.IsCorrect);

        // Лишний неправильный ответ
        var wrongResult = _service.GradeControlQuestion(q, "10,20,30");
        Assert.False(wrongResult.IsCorrect);
    }

    [Fact]
    public void ShortText_MatchesCaseInsensitiveAndSynonyms()
    {
        var q = new ControlQuestion
        {
            Type = QuestionType.ShortText,
            CorrectTextAnswer = "Отладка",
            SynonymsCsv = "дебаг, debugging, дебаггинг"
        };

        Assert.True(_service.GradeControlQuestion(q, "отладка").IsCorrect);
        Assert.True(_service.GradeControlQuestion(q, "  ОТЛАДКА  ").IsCorrect);
        Assert.True(_service.GradeControlQuestion(q, "дебаг").IsCorrect);
        Assert.True(_service.GradeControlQuestion(q, "Debugging").IsCorrect);
        Assert.False(_service.GradeControlQuestion(q, "компиляция").IsCorrect);
    }

    [Fact]
    public void Matching_RequiresAllPairsToMatch()
    {
        var q = new ControlQuestion
        {
            Type = QuestionType.Matching,
            Options = new List<ControlQuestionOption>
            {
                new ControlQuestionOption { Id = 1, Text = "Unit", MatchingKey = "Модуль" },
                new ControlQuestionOption { Id = 2, Text = "E2E", MatchingKey = "Сквозной" }
            }
        };

        // Верные пары: 1:Модуль;2:Сквозной
        var success = _service.GradeControlQuestion(q, "1:Модуль;2:Сквозной");
        Assert.True(success.IsCorrect);

        // Перепутанные пары
        var fail = _service.GradeControlQuestion(q, "1:Сквозной;2:Модуль");
        Assert.False(fail.IsCorrect);
    }
}
