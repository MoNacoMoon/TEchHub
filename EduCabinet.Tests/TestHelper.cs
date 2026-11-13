using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;

namespace EduCabinet.Tests;

public static class TestHelper
{
    public static (AppDbContext context, SqliteConnection connection) CreateInMemoryDbContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }

    public static void CheckpointDatabase(string dbPath)
    {
        if (File.Exists(dbPath))
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
    }
}
