using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;

namespace EduCabinet.Web.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        // Авторизованный студент или преподаватель сразу попадает в свой рабочий кабинет
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Teacher") || User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Teacher");
            }
            if (User.IsInRole("Student"))
            {
                return RedirectToAction("Index", "Student");
            }
        }

        ViewBag.DisciplinesCount = await _db.Disciplines.CountAsync(d => d.IsActive);
        ViewBag.LecturesCount = await _db.Lectures.CountAsync();
        ViewBag.QuestionsCount = await _db.TestQuestions.CountAsync() + await _db.ControlQuestions.CountAsync();
        ViewBag.StudentsCount = await _db.Users.CountAsync(u => u.Role == Models.Entities.UserRole.Student);
        ViewBag.GroupsCount = await _db.StudentGroups.CountAsync();

        return View();
    }

    public IActionResult Error()
    {
        return View();
    }
}
