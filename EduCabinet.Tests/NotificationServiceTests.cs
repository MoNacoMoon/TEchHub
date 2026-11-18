using Xunit;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Tests;

public class NotificationServiceTests
{
    [Fact]
    public async Task CreateNotification_ShouldSaveNotification_AndIncrementUnreadCount()
    {
        var (context, conn) = TestHelper.CreateInMemoryDbContext();
        using (conn)
        {
            var user = new User
            {
                Username = "teststudent",
                PasswordHash = "hash",
                FullName = "Иван Иванов",
                Role = UserRole.Student,
                IsActive = true
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var service = new NotificationService(context);

            await service.CreateNotificationAsync(
                user.Id,
                "🎉 Отчет зачтен!",
                "Преподаватель проверил ваш отчет. Оценка 95/100.",
                NotificationType.Success,
                "/Student/Lecture/1"
            );

            var unreadCount = await service.GetUnreadCountAsync(user.Id);
            Assert.Equal(1, unreadCount);

            var notifications = await service.GetRecentNotificationsAsync(user.Id, 10);
            Assert.Single(notifications);
            Assert.Equal("🎉 Отчет зачтен!", notifications[0].Title);
            Assert.Equal("success", notifications[0].Type);
            Assert.False(notifications[0].IsRead);
            Assert.Equal("/Student/Lecture/1", notifications[0].Url);
        }
    }

    [Fact]
    public async Task MarkAsRead_And_MarkAllAsRead_ShouldUpdateStatus()
    {
        var (context, conn) = TestHelper.CreateInMemoryDbContext();
        using (conn)
        {
            var user = new User
            {
                Username = "teststudent2",
                PasswordHash = "hash",
                FullName = "Петр Петров",
                Role = UserRole.Student,
                IsActive = true
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var service = new NotificationService(context);

            await service.CreateNotificationAsync(user.Id, "Тема 1", "Сообщение 1");
            await service.CreateNotificationAsync(user.Id, "Тема 2", "Сообщение 2");

            var initialUnread = await service.GetUnreadCountAsync(user.Id);
            Assert.Equal(2, initialUnread);

            var list = await service.GetRecentNotificationsAsync(user.Id);
            int firstId = list[0].Id;

            bool marked = await service.MarkAsReadAsync(firstId, user.Id);
            Assert.True(marked);

            var afterOne = await service.GetUnreadCountAsync(user.Id);
            Assert.Equal(1, afterOne);

            int markedAll = await service.MarkAllAsReadAsync(user.Id);
            Assert.Equal(1, markedAll);

            var finalUnread = await service.GetUnreadCountAsync(user.Id);
            Assert.Equal(0, finalUnread);
        }
    }

    [Fact]
    public async Task NotifyDisciplineTeachers_ShouldCreateNotificationsForTeacher()
    {
        var (context, conn) = TestHelper.CreateInMemoryDbContext();
        using (conn)
        {
            var teacher = new User
            {
                Username = "teacher1",
                PasswordHash = "hash",
                FullName = "Преподаватель Сидоров",
                Role = UserRole.Teacher,
                IsActive = true
            };
            context.Users.Add(teacher);
            await context.SaveChangesAsync();

            var discipline = new Discipline
            {
                Title = "Программирование C#",
                CreatedByUserId = teacher.Id,
                IsActive = true
            };
            context.Disciplines.Add(discipline);
            await context.SaveChangesAsync();

            var service = new NotificationService(context);

            await service.NotifyDisciplineTeachersAsync(
                discipline.Id,
                "📥 Сдан новый отчет",
                "Студент сдал отчет",
                NotificationType.Info,
                "/Teacher/StudentReports"
            );

            var teacherUnread = await service.GetUnreadCountAsync(teacher.Id);
            Assert.Equal(1, teacherUnread);

            var notifications = await service.GetRecentNotificationsAsync(teacher.Id);
            Assert.Single(notifications);
            Assert.Equal("📥 Сдан новый отчет", notifications[0].Title);
        }
    }
}
