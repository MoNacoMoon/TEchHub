using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.DTOs;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/disciplines")]
[Produces("application/json")]
public class DisciplinesApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILectureAccessService _accessService;

    public DisciplinesApiController(AppDbContext db, ILectureAccessService accessService)
    {
        _db = db;
        _accessService = accessService;
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out int id) ? id : null;
    }

    /// <summary>
    /// Получить список всех активных учебных дисциплин
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DisciplineDto>>> GetAll()
    {
        var list = await _db.Disciplines
            .Where(d => d.IsActive)
            .Include(d => d.Topics)
                .ThenInclude(t => t.Lectures)
            .OrderBy(d => d.Title)
            .Select(d => new DisciplineDto
            {
                Id = d.Id,
                Title = d.Title,
                Description = d.Description,
                TopicsCount = d.Topics.Count,
                LecturesCount = d.Topics.SelectMany(t => t.Lectures).Count()
            })
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// Получить детальную структуру дисциплины (темы и лекции со статусами доступа)
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DisciplineDetailDto>> GetById(int id)
    {
        int studentId = GetCurrentUserId() ?? 0;

        var discipline = await _db.Disciplines
            .Include(d => d.Topics.OrderBy(t => t.OrderIndex))
                .ThenInclude(t => t.Lectures.OrderBy(l => l.OrderIndex))
                    .ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(d => d.Id == id && d.IsActive);

        if (discipline == null) return NotFound(new { message = "Дисциплина не найдена" });

        var progress = await _accessService.GetDisciplineProgressAsync(studentId, id);
        var statusDict = progress.Lectures.ToDictionary(l => l.LectureId);

        var dto = new DisciplineDetailDto
        {
            Id = discipline.Id,
            Title = discipline.Title,
            Description = discipline.Description,
            ProgressPercent = progress.ProgressPercent,
            Topics = discipline.Topics.OrderBy(t => t.OrderIndex).Select(t => new TopicDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                OrderIndex = t.OrderIndex,
                Lectures = t.Lectures.OrderBy(l => l.OrderIndex).Select(l =>
                {
                    statusDict.TryGetValue(l.Id, out var stat);
                    return new LectureSummaryDto
                    {
                        Id = l.Id,
                        Title = l.Title,
                        OrderIndex = l.OrderIndex,
                        Status = stat?.Status ?? LectureAccessStatus.Locked,
                        HasAttachment = !string.IsNullOrEmpty(l.AttachmentFilePath),
                        HasTest = l.Test != null && l.Test.IsActive,
                        IsCompleted = stat?.Status == LectureAccessStatus.Completed,
                        BestTestScore = stat?.BestTestScore,
                        IsManuallyUnlocked = stat?.IsManuallyUnlocked ?? false
                    };
                }).ToList()
            }).ToList()
        };

        return Ok(dto);
    }

    /// <summary>
    /// Получить процент прогресса и статистику освоения дисциплины
    /// </summary>
    [HttpGet("{id:int}/progress")]
    public async Task<ActionResult<DisciplineProgressInfo>> GetProgress(int id)
    {
        int studentId = GetCurrentUserId() ?? 0;
        var info = await _accessService.GetDisciplineProgressAsync(studentId, id);
        return Ok(info);
    }
}
