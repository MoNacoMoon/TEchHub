using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Services;

public interface IEventLogService
{
    Task LogActionAsync(int? userId, string action, string description, string? targetStudent = null, string? targetLecture = null, string? ipAddress = null);
}

public class EventLogService : IEventLogService
{
    private readonly AppDbContext _db;

    public EventLogService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogActionAsync(int? userId, string action, string description, string? targetStudent = null, string? targetLecture = null, string? ipAddress = null)
    {
        var log = new EventLog
        {
            UserId = userId,
            Action = action,
            Description = description,
            TargetStudent = targetStudent,
            TargetLecture = targetLecture,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        };

        _db.EventLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
