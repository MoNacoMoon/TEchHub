using Xunit;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Tests;

public class LectureAccessServiceTests
{
    [Fact]
    public async Task FirstLecture_IsAlwaysAccessible_ForEnrolledStudent()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            // Arrange: 1 discipline, 1 topic, 2 lectures
            var user = new User { Username = "student1", FullName = "Иванов И.И.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Тестирование ПО", Description = "Курс" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1", OrderIndex = 1 };
            var lec1 = new Lecture { Topic = topic, Title = "Лекция 1", OrderIndex = 1, ContentHtml = "Теория 1" };
            var lec2 = new Lecture { Topic = topic, Title = "Лекция 2", OrderIndex = 2, ContentHtml = "Теория 2" };

            context.Users.Add(user);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            await context.SaveChangesAsync();

            // Act
            bool canAccessLec1 = await service.CanStudentAccessLectureAsync(user.Id, lec1.Id);
            var statusLec1 = await service.GetLectureStatusAsync(user.Id, lec1.Id);

            // Assert
            Assert.True(canAccessLec1);
            Assert.Equal(LectureAccessStatus.Available, statusLec1);
        }
    }

    [Fact]
    public async Task SecondLecture_IsLocked_WhenFirstLectureNotCompleted()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var user = new User { Username = "student1", FullName = "Иванов И.И.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Тестирование ПО", Description = "Курс" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1", OrderIndex = 1 };
            var lec1 = new Lecture { Topic = topic, Title = "Лекция 1", OrderIndex = 1, ContentHtml = "Теория 1" };
            var lec2 = new Lecture { Topic = topic, Title = "Лекция 2", OrderIndex = 2, ContentHtml = "Теория 2" };

            context.Users.Add(user);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            await context.SaveChangesAsync();

            // Act: Попытка доступа ко второй лекции без сдачи первой
            bool canAccessLec2 = await service.CanStudentAccessLectureAsync(user.Id, lec2.Id);
            var statusLec2 = await service.GetLectureStatusAsync(user.Id, lec2.Id);

            // Assert: Строгая блокировка сервером!
            Assert.False(canAccessLec2);
            Assert.Equal(LectureAccessStatus.Locked, statusLec2);
        }
    }

    [Fact]
    public async Task SecondLecture_RemainsLocked_WhenTestScoreBelowPassingThreshold()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var user = new User { Username = "student1", FullName = "Иванов И.И.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Тестирование ПО", Description = "Курс" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1", OrderIndex = 1 };
            var lec1 = new Lecture { Topic = topic, Title = "Лекция 1", OrderIndex = 1, ContentHtml = "Теория 1" };
            var lec2 = new Lecture { Topic = topic, Title = "Лекция 2", OrderIndex = 2, ContentHtml = "Теория 2" };

            var test1 = new Test
            {
                Lecture = lec1,
                Title = "Тест 1",
                PassingScorePercent = 70,
                MaxAttempts = 3,
                IsActive = true
            };

            context.Users.Add(user);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            context.Tests.Add(test1);
            await context.SaveChangesAsync();

            // Студент сдал тест с результатом 50% (ниже 70%)
            var attempt = new TestAttempt
            {
                StudentId = user.Id,
                TestId = test1.Id,
                ScorePercent = 50,
                IsPassed = false
            };
            context.TestAttempts.Add(attempt);
            await context.SaveChangesAsync();

            // Обновляем статус
            await service.UpdateLectureCompletionStatusAsync(user.Id, lec1.Id);

            // Act
            bool canAccessLec2 = await service.CanStudentAccessLectureAsync(user.Id, lec2.Id);

            // Assert: Лекция 2 остается закрытой!
            Assert.False(canAccessLec2);
        }
    }

    [Fact]
    public async Task SecondLecture_Unlocks_WhenFirstLectureCompletedAndTestPassed()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var user = new User { Username = "student1", FullName = "Иванов И.И.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Тестирование ПО", Description = "Курс" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1", OrderIndex = 1 };
            var lec1 = new Lecture { Topic = topic, Title = "Лекция 1", OrderIndex = 1, ContentHtml = "Теория 1" };
            var lec2 = new Lecture { Topic = topic, Title = "Лекция 2", OrderIndex = 2, ContentHtml = "Теория 2" };

            var test1 = new Test
            {
                Lecture = lec1,
                Title = "Тест 1",
                PassingScorePercent = 70,
                MaxAttempts = 3,
                IsActive = true
            };

            context.Users.Add(user);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            context.Tests.Add(test1);
            await context.SaveChangesAsync();

            // Студент сдал тест на 80% (выше 70%)
            var attempt = new TestAttempt
            {
                StudentId = user.Id,
                TestId = test1.Id,
                ScorePercent = 80,
                IsPassed = true
            };
            context.TestAttempts.Add(attempt);
            await context.SaveChangesAsync();

            // Обновляем статус первой лекции
            bool isCompleted = await service.UpdateLectureCompletionStatusAsync(user.Id, lec1.Id);
            Assert.True(isCompleted);

            // Act: Проверяем доступ ко второй лекции
            bool canAccessLec2 = await service.CanStudentAccessLectureAsync(user.Id, lec2.Id);
            var statusLec2 = await service.GetLectureStatusAsync(user.Id, lec2.Id);

            // Assert: Вторая лекция теперь доступна!
            Assert.True(canAccessLec2);
            Assert.Equal(LectureAccessStatus.Available, statusLec2);
        }
    }

    [Fact]
    public async Task Teacher_CanManuallyUnlockLecture_AndActionIsLogged()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var teacher = new User { Username = "teacher", FullName = "Смирнов А.П.", Role = UserRole.Teacher };
            var student = new User { Username = "student1", FullName = "Иванов И.И.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Тестирование ПО", Description = "Курс" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1", OrderIndex = 1 };
            var lec1 = new Lecture { Topic = topic, Title = "Лекция 1", OrderIndex = 1, ContentHtml = "Теория 1" };
            var lec2 = new Lecture { Topic = topic, Title = "Лекция 2", OrderIndex = 2, ContentHtml = "Теория 2" };

            context.Users.AddRange(teacher, student);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            await context.SaveChangesAsync();

            // Студент еще не закончил лекцию 1
            Assert.False(await service.CanStudentAccessLectureAsync(student.Id, lec2.Id));

            // Преподаватель вручную открывает лекцию 2
            bool unlockSuccess = await service.ManuallyUnlockLectureAsync(student.Id, lec2.Id, "Особые обстоятельства", teacher.Id, "127.0.0.1");

            // Assert
            Assert.True(unlockSuccess);
            Assert.True(await service.CanStudentAccessLectureAsync(student.Id, lec2.Id));

            // Проверяем запись в журнале аудита
            var log = Assert.Single(context.EventLogs);
            Assert.Equal("MANUAL_UNLOCK", log.Action);
            Assert.Contains("Особые обстоятельства", log.Description);
            Assert.Equal(student.FullName, log.TargetStudent);
        }
    }
}
