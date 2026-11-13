using System.Text.Json;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Services;

public class GradingResult
{
    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
    public string Feedback { get; set; } = string.Empty;
}

public interface IGradingService
{
    GradingResult GradeControlQuestion(ControlQuestion question, string studentAnswer);
    GradingResult GradeTestQuestion(TestQuestion question, string studentAnswer);
}

public class GradingService : IGradingService
{
    public GradingResult GradeControlQuestion(ControlQuestion question, string studentAnswer)
    {
        return GradeQuestionInternal(
            question.Type,
            question.CorrectTextAnswer,
            question.SynonymsCsv,
            question.Options.Select(o => new OptionDto { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect, MatchingKey = o.MatchingKey }).ToList(),
            studentAnswer,
            points: 1
        );
    }

    public GradingResult GradeTestQuestion(TestQuestion question, string studentAnswer)
    {
        return GradeQuestionInternal(
            question.Type,
            question.CorrectTextAnswer,
            question.SynonymsCsv,
            question.Options.Select(o => new OptionDto { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect, MatchingKey = o.MatchingKey }).ToList(),
            studentAnswer,
            question.Points
        );
    }

    private class OptionDto
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
        public string? MatchingKey { get; set; }
    }

    private GradingResult GradeQuestionInternal(
        QuestionType type,
        string? correctText,
        string? synonymsCsv,
        List<OptionDto> options,
        string studentAnswer,
        int points)
    {
        if (string.IsNullOrWhiteSpace(studentAnswer))
        {
            return new GradingResult { IsCorrect = false, PointsEarned = 0, Feedback = "Ответ не предоставлен" };
        }

        studentAnswer = studentAnswer.Trim();

        switch (type)
        {
            case QuestionType.SingleChoice:
                {
                    if (int.TryParse(studentAnswer, out int selectedId))
                    {
                        var opt = options.FirstOrDefault(o => o.Id == selectedId);
                        if (opt != null && opt.IsCorrect)
                        {
                            return new GradingResult { IsCorrect = true, PointsEarned = points, Feedback = "Правильно" };
                        }
                    }
                    return new GradingResult { IsCorrect = false, PointsEarned = 0, Feedback = "Неверный ответ" };
                }

            case QuestionType.MultipleChoice:
                {
                    // studentAnswer может быть "1,3,4" или JSON массив
                    var selectedIds = new HashSet<int>();
                    if (studentAnswer.StartsWith("["))
                    {
                        try
                        {
                            var list = JsonSerializer.Deserialize<List<int>>(studentAnswer);
                            if (list != null) selectedIds = new HashSet<int>(list);
                        }
                        catch { }
                    }
                    else
                    {
                        var parts = studentAnswer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out int id)) selectedIds.Add(id);
                        }
                    }

                    var correctIds = options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
                    bool isMatch = selectedIds.SetEquals(correctIds);

                    return new GradingResult
                    {
                        IsCorrect = isMatch,
                        PointsEarned = isMatch ? points : 0,
                        Feedback = isMatch ? "Правильно" : "Выбраны не все правильные ответы или выбраны лишние"
                    };
                }

            case QuestionType.ShortText:
            case QuestionType.FillBlank:
                {
                    var normalizedStudent = NormalizeText(studentAnswer);
                    var acceptable = new List<string>();

                    if (!string.IsNullOrWhiteSpace(correctText))
                    {
                        acceptable.Add(NormalizeText(correctText));
                    }

                    if (!string.IsNullOrWhiteSpace(synonymsCsv))
                    {
                        var synonyms = synonymsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        acceptable.AddRange(synonyms.Select(NormalizeText));
                    }

                    bool matches = acceptable.Any(a => a == normalizedStudent);
                    return new GradingResult
                    {
                        IsCorrect = matches,
                        PointsEarned = matches ? points : 0,
                        Feedback = matches ? "Правильно" : $"Неверно. Ваш ответ: {studentAnswer}"
                    };
                }

            case QuestionType.Matching:
                {
                    // studentAnswer может быть JSON словарь: {"1":"Б", "2":"А", ...}
                    // или "1:Б;2:А;3:В"
                    var pairs = new Dictionary<int, string>();
                    if (studentAnswer.StartsWith("{"))
                    {
                        try
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(studentAnswer);
                            if (dict != null)
                            {
                                foreach (var kv in dict)
                                {
                                    if (int.TryParse(kv.Key, out int optId))
                                    {
                                        pairs[optId] = kv.Value.Trim();
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        var tokens = studentAnswer.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var token in tokens)
                        {
                            var sub = token.Split(':');
                            if (sub.Length == 2 && int.TryParse(sub[0].Trim(), out int optId))
                            {
                                pairs[optId] = sub[1].Trim();
                            }
                        }
                    }

                    bool allMatch = true;
                    if (pairs.Count != options.Count || options.Count == 0)
                    {
                        allMatch = false;
                    }
                    else
                    {
                        foreach (var opt in options)
                        {
                            if (!pairs.TryGetValue(opt.Id, out var userVal) ||
                                !string.Equals(NormalizeText(userVal), NormalizeText(opt.MatchingKey ?? ""), StringComparison.OrdinalIgnoreCase))
                            {
                                allMatch = false;
                                break;
                            }
                        }
                    }

                    return new GradingResult
                    {
                        IsCorrect = allMatch,
                        PointsEarned = allMatch ? points : 0,
                        Feedback = allMatch ? "Соответствия установлены верно" : "Некоторые соответствия установлены неверно"
                    };
                }

            case QuestionType.OpenEnded:
                {
                    // Развернутый ответ ожидает проверки преподавателем
                    return new GradingResult
                    {
                        IsCorrect = false,
                        PointsEarned = 0,
                        Feedback = "Ответ сохранён и передан преподавателю на проверку"
                    };
                }

            default:
                return new GradingResult { IsCorrect = false, PointsEarned = 0, Feedback = "Неизвестный тип вопроса" };
        }
    }

    private static string NormalizeText(string text)
    {
        return text.Trim().ToLowerInvariant().Replace("ё", "е");
    }
}
