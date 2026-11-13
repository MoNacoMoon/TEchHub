using Xunit;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task PasswordHashing_And_Authentication_WorksCorrectly()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var authService = new AuthService(context);

            var user = new User
            {
                Username = "teacher_test",
                FullName = "Петров П.П.",
                Role = UserRole.Teacher
            };
            user.PasswordHash = authService.HashPassword(user, "SecurePass123!");
            context.Users.Add(user);
            await context.SaveChangesAsync();

            // Проверка правильного пароля
            var authenticated = await authService.AuthenticateAsync("teacher_test", "SecurePass123!");
            Assert.NotNull(authenticated);
            Assert.Equal("teacher_test", authenticated.Username);

            // Проверка неправильного пароля
            var failedAuth = await authService.AuthenticateAsync("teacher_test", "WrongPassword");
            Assert.Null(failedAuth);

            // Проверка несуществующего логина
            var nonExistent = await authService.AuthenticateAsync("no_user", "AnyPass");
            Assert.Null(nonExistent);
        }
    }

    [Fact]
    public async Task StudentRegistration_SavesUserWithHashedPassword()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var authService = new AuthService(context);

            var group = new StudentGroup { Name = "ИС-41" };
            context.StudentGroups.Add(group);
            await context.SaveChangesAsync();

            var registered = await authService.RegisterStudentAsync("new_student", "Secret123!", "Сидоров С.С.", "sid@test.ru", group.Id);

            Assert.NotNull(registered);
            Assert.Equal(UserRole.Student, registered.Role);
            Assert.NotEqual("Secret123!", registered.PasswordHash); // Пароль обязательно хеширован!
            Assert.True(authService.VerifyPassword(registered, "Secret123!"));
        }
    }
}
