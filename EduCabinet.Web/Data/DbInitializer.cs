using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext context, IWebHostEnvironment env)
    {
        await context.Database.EnsureCreatedAsync();

        // Гарантируем наличие колонок в таблице StudentGroups
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""InviteCode"" TEXT NOT NULL DEFAULT '';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""InviteCodeExpiresAt"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""InviteCodeGeneratedAt"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""CourseNumber"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""StudyDurationYears"" INTEGER NOT NULL DEFAULT 4;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""AdmissionYear"" INTEGER NOT NULL DEFAULT 2026;"); } catch { }

        // Гарантируем наличие колонок подтверждения почты и сброса пароля в Users
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""IsEmailConfirmed"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""EmailConfirmationCode"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""EmailConfirmationExpiresAt"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""PasswordResetCode"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""PasswordResetExpiresAt"" TEXT NULL;"); } catch { }

        // Гарантируем наличие колонок изображений к вопросам и вариантам ответов
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""ControlQuestions"" ADD COLUMN ""ImageUrl"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""ControlQuestionOptions"" ADD COLUMN ""ImageUrl"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""TestQuestions"" ADD COLUMN ""ImageUrl"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""TestQuestionOptions"" ADD COLUMN ""ImageUrl"" TEXT NULL;"); } catch { }

        // Гарантируем наличие колонок сдачи отчетов студентами в Lectures и LectureProgresses
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Lectures"" ADD COLUMN ""RequiresReportSubmission"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Lectures"" ADD COLUMN ""ReportInstructions"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Lectures"" ADD COLUMN ""MaxReportScore"" INTEGER NULL DEFAULT 100;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Lectures"" ADD COLUMN ""ReportDeadline"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""LectureProgresses"" ADD COLUMN ""IsReportApproved"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""LectureProgresses"" ADD COLUMN ""ReportGrade"" INTEGER NULL;"); } catch { }

        // Гарантируем наличие таблицы StudentReportSubmissions
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""StudentReportSubmissions"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""LectureId"" INTEGER NOT NULL,
                    ""StudentId"" INTEGER NOT NULL,
                    ""FilePath"" TEXT NOT NULL,
                    ""OriginalFileName"" TEXT NOT NULL,
                    ""FileSizeBytes"" INTEGER NOT NULL,
                    ""ContentType"" TEXT NULL,
                    ""StudentComment"" TEXT NULL,
                    ""SubmittedAt"" TEXT NOT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 1,
                    ""Grade"" INTEGER NULL,
                    ""TeacherFeedback"" TEXT NULL,
                    ""ReviewedAt"" TEXT NULL,
                    ""ReviewedByTeacherId"" INTEGER NULL,
                    ""ReviewedFilePath"" TEXT NULL,
                    ""ReviewedFileName"" TEXT NULL,
                    ""ReviewedFileSizeBytes"" INTEGER NULL,
                    CONSTRAINT ""FK_StudentReportSubmissions_Lectures_LectureId"" FOREIGN KEY (""LectureId"") REFERENCES ""Lectures"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_StudentReportSubmissions_Users_StudentId"" FOREIGN KEY (""StudentId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_StudentReportSubmissions_Users_ReviewedByTeacherId"" FOREIGN KEY (""ReviewedByTeacherId"") REFERENCES ""Users"" (""Id"") ON DELETE SET NULL
                );
            ");
        }
        catch { }

        // Гарантируем наличие колонок проверенного файла в StudentReportSubmissions
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentReportSubmissions"" ADD COLUMN ""ReviewedFilePath"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentReportSubmissions"" ADD COLUMN ""ReviewedFileName"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentReportSubmissions"" ADD COLUMN ""ReviewedFileSizeBytes"" INTEGER NULL;"); } catch { }

        // Гарантируем наличие таблицы SystemInviteTokens
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""SystemInviteTokens"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Token"" TEXT NOT NULL,
                    ""TargetRole"" INTEGER NOT NULL,
                    ""Note"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""ExpiresAt"" TEXT NULL,
                    ""IsUsed"" INTEGER NOT NULL DEFAULT 0,
                    ""UsedByUserId"" INTEGER NULL,
                    ""UsedAt"" TEXT NULL
                );
            ");
        }
        catch { }

        // Гарантируем наличие таблицы CourseAnnouncements
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourseAnnouncements"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""DisciplineId"" INTEGER NOT NULL,
                    ""AuthorTeacherId"" INTEGER NOT NULL,
                    ""Title"" TEXT NOT NULL,
                    ""Content"" TEXT NOT NULL,
                    ""IsPinned"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_CourseAnnouncements_Disciplines_DisciplineId"" FOREIGN KEY (""DisciplineId"") REFERENCES ""Disciplines"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_CourseAnnouncements_Users_AuthorTeacherId"" FOREIGN KEY (""AuthorTeacherId"") REFERENCES ""Users"" (""Id"") ON DELETE RESTRICT
                );
            ");
        }
        catch { }

        // Гарантируем наличие таблицы UserNotifications
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""UserNotifications"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""UserId"" INTEGER NOT NULL,
                    ""Title"" TEXT NOT NULL,
                    ""Message"" TEXT NOT NULL,
                    ""Type"" INTEGER NOT NULL DEFAULT 1,
                    ""Url"" TEXT NULL,
                    ""IsRead"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""ReadAt"" TEXT NULL,
                    CONSTRAINT ""FK_UserNotifications_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ""IX_UserNotifications_UserId_IsRead"" ON ""UserNotifications"" (""UserId"", ""IsRead"");
            ");
        }
        catch { }

        // Создание главного Администратора системы TechHub (DilaAdmin)
        var hasher = new PasswordHasher<User>();
        var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == "dilaadmin");

        // Если почта была привязана к другому старому пользователю — освобождаем её
        var emailClash = await context.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == "cilondiom@gmail.com" && u.Username.ToLower() != "dilaadmin");
        if (emailClash != null)
        {
            emailClash.Email = $"old_{emailClash.Id}@techhub.local";
            await context.SaveChangesAsync();
        }

        if (adminUser == null)
        {
            adminUser = new User
            {
                Username = "DilaAdmin",
                FullName = "Администратор TechHub",
                Email = "cilondiom@gmail.com",
                Role = UserRole.Admin,
                IsActive = true,
                IsEmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            adminUser.PasswordHash = hasher.HashPassword(adminUser, "Moon_GGWW52");
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }
        else
        {
            adminUser.FullName = "Администратор TechHub";
            adminUser.Email = "cilondiom@gmail.com";
            adminUser.Role = UserRole.Admin;
            adminUser.IsActive = true;
            adminUser.IsEmailConfirmed = true;
            adminUser.PasswordHash = hasher.HashPassword(adminUser, "Moon_GGWW52");
            await context.SaveChangesAsync();
        }

        // Очищаем устаревшие системные мастер-инвайты (теперь администратор управляет инвайтами вручную)
        var oldMasterTokens = await context.SystemInviteTokens
            .Where(t => t.Token == "ADMIN-TECHHUB-2026-M8W2" || t.Token == "ADMIN-TECHHUB-2026-X7K9")
            .ToListAsync();
        if (oldMasterTokens.Any())
        {
            context.SystemInviteTokens.RemoveRange(oldMasterTokens);
            await context.SaveChangesAsync();
        }

        // Очищаем автоматически созданные пустые группы 111-415, чтобы они не восстанавливались после удаления пользователем
        var defaultGroupNames = (from course in Enumerable.Range(1, 4)
                                 from groupIdx in Enumerable.Range(1, 5)
                                 select $"{course}1{groupIdx}").ToHashSet();

        var resurrectedGroups = await context.StudentGroups
            .Include(g => g.Memberships)
            .Where(g => defaultGroupNames.Contains(g.Name) && !g.Memberships.Any() && !context.DisciplineGroups.Any(dg => dg.GroupId == g.Id))
            .ToListAsync();

        if (resurrectedGroups.Any())
        {
            context.StudentGroups.RemoveRange(resurrectedGroups);
            await context.SaveChangesAsync();
        }
    }
}
