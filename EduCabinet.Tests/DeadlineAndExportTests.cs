using Xunit;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Tests;

public class DeadlineAndExportTests
{
    [Fact]
    public async Task Lecture_ReportDeadline_CanBeSetAndQueried()
    {
        var (context, conn) = TestHelper.CreateInMemoryDbContext();
        using (conn)
        {
            var teacher = new User
            {
                Username = "teacher_dl",
                PasswordHash = "hash",
                FullName = "Учитель Смирнов",
                Role = UserRole.Teacher,
                IsActive = true
            };
            context.Users.Add(teacher);
            await context.SaveChangesAsync();

            var disc = new Discipline { Title = "Физика", CreatedByUserId = teacher.Id };
            context.Disciplines.Add(disc);
            await context.SaveChangesAsync();

            var topic = new Topic { DisciplineId = disc.Id, Title = "Механика" };
            context.Topics.Add(topic);
            await context.SaveChangesAsync();

            var deadline = DateTime.UtcNow.AddDays(3);
            var lecture = new Lecture
            {
                TopicId = topic.Id,
                Title = "Лабораторная работа 1",
                ContentHtml = "<p>Условия</p>",
                RequiresReportSubmission = true,
                MaxReportScore = 100,
                ReportDeadline = deadline
            };
            context.Lectures.Add(lecture);
            await context.SaveChangesAsync();

            var saved = await context.Lectures.FirstOrDefaultAsync(l => l.Id == lecture.Id);
            Assert.NotNull(saved);
            Assert.True(saved.RequiresReportSubmission);
            Assert.NotNull(saved.ReportDeadline);
            Assert.Equal(deadline, saved.ReportDeadline.Value);
        }
    }

    [Fact]
    public async Task UserNotification_Entity_Properties_SaveAndRetrieveCorrectly()
    {
        var (context, conn) = TestHelper.CreateInMemoryDbContext();
        using (conn)
        {
            var student = new User
            {
                Username = "student_notif",
                PasswordHash = "hash",
                FullName = "Алексей Иванов",
                Role = UserRole.Student,
                IsActive = true
            };
            context.Users.Add(student);
            await context.SaveChangesAsync();

            var notif = new UserNotification
            {
                UserId = student.Id,
                Title = "Тестовое оповещение",
                Message = "Ваша оценка: 100",
                Type = NotificationType.Success,
                Url = "/Student/Lecture/5",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            context.UserNotifications.Add(notif);
            await context.SaveChangesAsync();

            var fetched = await context.UserNotifications.FirstOrDefaultAsync(n => n.Id == notif.Id);
            Assert.NotNull(fetched);
            Assert.Equal(student.Id, fetched.UserId);
            Assert.Equal(NotificationType.Success, fetched.Type);
            Assert.False(fetched.IsRead);
            Assert.Equal("/Student/Lecture/5", fetched.Url);
        }
    }
}
