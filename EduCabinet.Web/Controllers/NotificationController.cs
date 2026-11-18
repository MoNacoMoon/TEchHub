using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetRecent()
    {
        int userId = GetCurrentUserId();
        if (userId <= 0) return Unauthorized();

        var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
        var notifications = await _notificationService.GetRecentNotificationsAsync(userId, 10);

        return Json(new { unreadCount, notifications });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        int userId = GetCurrentUserId();
        if (userId <= 0) return Unauthorized();

        var success = await _notificationService.MarkAsReadAsync(id, userId);
        var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
        return Json(new { success, unreadCount });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> MarkAllAsRead()
    {
        int userId = GetCurrentUserId();
        if (userId <= 0) return Unauthorized();

        var markedCount = await _notificationService.MarkAllAsReadAsync(userId);
        return Json(new { success = true, markedCount });
    }

    private int GetCurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out int id) ? id : 0;
    }
}
