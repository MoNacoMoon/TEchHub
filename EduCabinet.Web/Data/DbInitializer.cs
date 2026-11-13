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

        // Мастер-инвайты для регистрации преподавателей и доп. администраторов
        var masterCodes = new[] { "ADMIN-TECHHUB-2026-M8W2", "ADMIN-TECHHUB-2026-X7K9" };
        foreach (var code in masterCodes)
        {
            var existingToken = await context.SystemInviteTokens.FirstOrDefaultAsync(t => t.Token == code);
            if (existingToken == null)
            {
                context.SystemInviteTokens.Add(new SystemInviteToken
                {
                    Token = code,
                    TargetRole = UserRole.Admin,
                    Note = "Мастер-инвайт Администратора TechHub",
                    CreatedAt = DateTime.UtcNow,
                    IsUsed = false
                });
            }
        }
        await context.SaveChangesAsync();

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
