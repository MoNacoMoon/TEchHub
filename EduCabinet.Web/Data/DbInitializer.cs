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
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""CourseNumber"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""StudyDurationYears"" INTEGER NOT NULL DEFAULT 4;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""StudentGroups"" ADD COLUMN ""AdmissionYear"" INTEGER NOT NULL DEFAULT 2026;"); } catch { }

        // Гарантируем наличие колонок подтверждения почты и сброса пароля в Users
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""IsEmailConfirmed"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""EmailConfirmationCode"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""EmailConfirmationExpiresAt"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""PasswordResetCode"" TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Users"" ADD COLUMN ""PasswordResetExpiresAt"" TEXT NULL;"); } catch { }

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

        // Мастер-инвайт для первичной регистрации Администратора системы TechHub
        const string MasterAdminToken = "ADMIN-TECHHUB-2026-X7K9";

        // Разовая очистка базы данных по запросу пользователя при переходе на TechHub
        var markerPath = Directory.Exists("/data") ? "/data/.techhub_db_cleaned_v1" : "educabinet.db.cleaned_v1";
        if (!File.Exists(markerPath))
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(@"
                    PRAGMA foreign_keys = OFF;
                    DELETE FROM ""TestAttemptAnswers"";
                    DELETE FROM ""TestAttempts"";
                    DELETE FROM ""StudentControlAnswers"";
                    DELETE FROM ""LectureProgresses"";
                    DELETE FROM ""GroupMemberships"";
                    DELETE FROM ""CourseAnnouncements"";
                    DELETE FROM ""EventLogs"";
                    DELETE FROM ""Users"";
                    UPDATE ""SystemInviteTokens"" SET ""IsUsed"" = 0, ""UsedByUserId"" = NULL, ""UsedAt"" = NULL;
                    PRAGMA foreign_keys = ON;
                ");
                await File.WriteAllTextAsync(markerPath, DateTime.UtcNow.ToString("O"));
            }
            catch { }
        }

        var existingMaster = await context.SystemInviteTokens.FirstOrDefaultAsync(t => t.Token == MasterAdminToken);
        if (existingMaster == null)
        {
            context.SystemInviteTokens.Add(new SystemInviteToken
            {
                Token = MasterAdminToken,
                TargetRole = UserRole.Admin,
                Note = "Мастер-инвайт для регистрации Администратора системы TechHub",
                CreatedAt = DateTime.UtcNow,
                IsUsed = false
            });
            await context.SaveChangesAsync();
        }
        else if (!await context.Users.AnyAsync(u => u.Role == UserRole.Admin))
        {
            // Если администратора еще нет — мастер-инвайт всегда открыт для регистрации
            existingMaster.IsUsed = false;
            existingMaster.UsedByUserId = null;
            existingMaster.UsedAt = null;
            await context.SaveChangesAsync();
        }

        // 1. Проверяем и создаем 20 групп колледжа (1-4 курс, 111-115, 211-215, 311-315, 411-415)
        var existingGroups = await context.StudentGroups.ToListAsync();
        var existingNames = existingGroups.Select(g => g.Name).ToHashSet();

        for (int course = 1; course <= 4; course++)
        {
            int durationYears = (course == 3) ? 3 : 4; // 3 года или 4 года обучения
            for (int groupIdx = 1; groupIdx <= 5; groupIdx++)
            {
                string groupName = $"{course}1{groupIdx}";
                if (!existingNames.Contains(groupName))
                {
                    context.StudentGroups.Add(new StudentGroup
                    {
                        Name = groupName,
                        Description = $"Учебная группа {groupName}, {course} курс ({durationYears} года обучения)",
                        InviteCode = Services.InviteCodeGenerator.GenerateCode(groupName),
                        CourseNumber = course,
                        StudyDurationYears = durationYears,
                        AdmissionYear = 2026
                    });
                }
            }
        }
        await context.SaveChangesAsync();
    }
}
