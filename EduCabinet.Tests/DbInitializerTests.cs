using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Tests;

public class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ApplicationName { get; set; } = "EduCabinet";
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
}

public class DbInitializerTests
{
    [Fact]
    public async Task SeedAsync_InitializesAllRequiredEntities()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "EduCabinetTestWebRoot_" + Guid.NewGuid());
            Directory.CreateDirectory(tempPath);
            var fakeEnv = new FakeWebHostEnvironment { WebRootPath = tempPath };

            try
            {
                // Add a dummy test user before seed
                context.Users.Add(new User
                {
                    Username = "olduser",
                    FullName = "Old User",
                    Email = "old@example.com",
                    PasswordHash = "hash"
                });
                await context.SaveChangesAsync();
                Assert.Equal(1, context.Users.Count());

                await DbInitializer.SeedAsync(context, fakeEnv);

                Assert.Equal(0, context.Users.Count());

                // Assertions for clean hand-off: Master Admin token is seeded for initial registration
                Assert.True(context.SystemInviteTokens.Any(t => t.TargetRole == UserRole.Admin && !t.IsUsed));
                Assert.Equal(20, context.StudentGroups.Count());
                Assert.Equal(5, context.StudentGroups.Count(g => g.CourseNumber == 1));
                Assert.Equal(5, context.StudentGroups.Count(g => g.CourseNumber == 2));
                Assert.Equal(5, context.StudentGroups.Count(g => g.CourseNumber == 3));
                Assert.Equal(5, context.StudentGroups.Count(g => g.CourseNumber == 4));
                Assert.True(context.StudentGroups.All(g => !string.IsNullOrEmpty(g.InviteCode)));

                var projectDb = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EduCabinet.Web", "educabinet.db"));
                TestHelper.CheckpointDatabase(projectDb);
            }
            finally
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
        }
    }
}
