using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Models.ViewModels;
using EduCabinet.Web.Services;

namespace EduCabinet.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly AppDbContext _db;
    private readonly IEventLogService _eventLogger;
    private readonly IEmailService _emailService;
    private readonly IWebHostEnvironment _env;
    private readonly IRegistrationSecurityService _regSecurity;

    public AccountController(
        IAuthService authService, 
        AppDbContext db, 
        IEventLogService eventLogger,
        IEmailService emailService,
        IWebHostEnvironment env,
        IRegistrationSecurityService regSecurity)
    {
        _authService = authService;
        _db = db;
        _eventLogger = eventLogger;
        _emailService = emailService;
        _env = env;
        _regSecurity = regSecurity;
    }

    private string GetClientIp()
    {
        var forwarded = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var ip = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(ip)) return ip;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToUserHome();
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _authService.AuthenticateAsync(model.Username, model.Password);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Неверный логин или пароль");
                return View(model);
            }

            if (user.Role == UserRole.Student && !user.IsEmailConfirmed)
            {
                TempData["WarningMessage"] = "Для входа в систему необходимо подтвердить адрес электронной почты.";
                return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
            }

            await _authService.SignInUserAsync(HttpContext, user);

            try
            {
                await _eventLogger.LogActionAsync(
                    user.Id,
                    "LOGIN",
                    $"Вход в систему пользователя {user.FullName} ({user.Username}), роль: {user.Role}",
                    null,
                    null,
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );
            }
            catch { }

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToUserHome(user.Role);
        }
        catch (Exception ex)
        {
            await _eventLogger.LogActionAsync(null, "LOGIN_ERROR", ex.Message);
            ModelState.AddModelError(string.Empty, "Ошибка входа: " + ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Register(string? code = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToUserHome();
        }

        var model = new RegisterViewModel();
        var clientIp = GetClientIp();

        if (_regSecurity.IsBlocked(clientIp, out int remaining, out var retryAfter))
        {
            model.IsIpBlocked = true;
            model.RemainingAttempts = 0;
            ModelState.AddModelError(string.Empty, $"Превышен лимит попыток регистрации (максимум 3 попытки с одного IP). Доступ временно заблокирован на {(int)Math.Ceiling(retryAfter?.TotalMinutes ?? 15)} мин.");
        }
        else
        {
            model.RemainingAttempts = remaining;
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var cleanCode = code.Trim().ToUpper().Replace(" ", "");
            if (cleanCode.StartsWith("ADMIN") || await _db.SystemInviteTokens.AnyAsync(t => t.Token.ToUpper() == cleanCode))
            {
                return RedirectToAction(nameof(RegisterStaff), new { code = cleanCode });
            }

            var cleanRaw = cleanCode.Replace("-", "");
            var group = await _db.StudentGroups.FirstOrDefaultAsync(g => 
                g.InviteCode.ToUpper() == cleanCode || 
                g.InviteCode.ToUpper().Replace("-", "") == cleanRaw);
            if (group != null)
            {
                if (group.InviteCodeExpiresAt.HasValue && group.InviteCodeExpiresAt.Value < DateTime.UtcNow)
                {
                    model.InviteCode = code.Trim();
                    ModelState.AddModelError("InviteCode", "Срок действия инвайт-кода истек. Инвайт-коды действуют 5 минут в целях безопасности. Попросите преподавателя обновить код.");
                }
                else
                {
                    model.InviteCode = group.InviteCode;
                    model.GroupId = group.Id;
                    model.GroupName = group.Name;
                    model.GroupDescription = group.Description;
                    model.IsCodeLocked = true;
                    if (group.InviteCodeExpiresAt.HasValue)
                    {
                        model.RemainingCodeSeconds = Math.Max(0, (int)(group.InviteCodeExpiresAt.Value - DateTime.UtcNow).TotalSeconds);
                    }
                }
            }
            else
            {
                model.InviteCode = code.Trim();
                ModelState.AddModelError("InviteCode", "Код приглашения не найден или устарел. Проверьте правильность кода или ссылки.");
            }
        }

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("register")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        var clientIp = GetClientIp();

        if (_regSecurity.IsBlocked(clientIp, out int remainingAttempts, out var retryAfter))
        {
            model.IsIpBlocked = true;
            model.RemainingAttempts = 0;
            ModelState.AddModelError(string.Empty, $"Превышено максимальное количество попыток регистрации (3) с вашего IP-адреса. Доступ временно заблокирован на {(int)Math.Ceiling(retryAfter?.TotalMinutes ?? 15)} мин.");
            return View(model);
        }

        int remaining = _regSecurity.RecordAttempt(clientIp);
        model.RemainingAttempts = remaining;

        if (string.IsNullOrWhiteSpace(model.InviteCode))
        {
            ModelState.AddModelError("InviteCode", "Укажите инвайт-код вашей учебной группы.");
        }

        if (string.IsNullOrWhiteSpace(model.Email))
        {
            ModelState.AddModelError("Email", "Адрес электронной почты обязателен для регистрации.");
        }

        StudentGroup? group = null;
        if (!string.IsNullOrWhiteSpace(model.InviteCode))
        {
            var cleanCode = model.InviteCode.Trim().ToUpper().Replace(" ", "");
            if (cleanCode.StartsWith("ADMIN") || await _db.SystemInviteTokens.AnyAsync(t => t.Token.ToUpper() == cleanCode))
            {
                return RedirectToAction(nameof(RegisterStaff), new { code = cleanCode });
            }

            var cleanRaw = cleanCode.Replace("-", "");
            group = await _db.StudentGroups.FirstOrDefaultAsync(g => 
                g.InviteCode.ToUpper() == cleanCode || 
                g.InviteCode.ToUpper().Replace("-", "") == cleanRaw);
            if (group == null)
            {
                ModelState.AddModelError("InviteCode", "Недействительный код приглашения. Обратитесь к преподавателю за кодом вашей группы.");
            }
            else if (group.InviteCodeExpiresAt.HasValue && group.InviteCodeExpiresAt.Value < DateTime.UtcNow)
            {
                ModelState.AddModelError("InviteCode", "Срок действия инвайт-кода истек. Инвайт-коды действуют ровно 5 минут в целях безопасности. Попросите преподавателя обновить код.");
                group = null;
            }
            else
            {
                model.GroupId = group.Id;
                model.GroupName = group.Name;
                model.GroupDescription = group.Description;
                if (group.InviteCodeExpiresAt.HasValue)
                {
                    model.RemainingCodeSeconds = Math.Max(0, (int)(group.InviteCodeExpiresAt.Value - DateTime.UtcNow).TotalSeconds);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Username))
        {
            var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == model.Username.Trim().ToLower());
            if (existingUser != null)
            {
                if (!existingUser.IsEmailConfirmed)
                {
                    _db.Users.Remove(existingUser);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    ModelState.AddModelError("Username", "Пользователь с таким логином уже занят.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            var cleanEmail = model.Email.Trim().ToLower();
            var existingEmailUser = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == cleanEmail);
            if (existingEmailUser != null)
            {
                if (!existingEmailUser.IsEmailConfirmed)
                {
                    _db.Users.Remove(existingEmailUser);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    ModelState.AddModelError("Email", "Пользователь с таким адресом электронной почты уже зарегистрирован.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var user = await _authService.RegisterStudentAsync(
                model.Username,
                model.Password,
                model.FullName,
                model.Email.Trim(),
                group!.Id
            );

            // Генерируем 6-значный код подтверждения email
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.IsEmailConfirmed = false;
            user.EmailConfirmationCode = code;
            user.EmailConfirmationExpiresAt = DateTime.UtcNow.AddMinutes(15);
            await _db.SaveChangesAsync();

            // Отправляем код на указанную почту
            await _emailService.SendConfirmationCodeAsync(user.Email!, code, user.FullName);

            await _eventLogger.LogActionAsync(
                user.Id,
                "REGISTER",
                $"Регистрация нового студента {user.FullName} ({user.Username}) в группу {group.Name}. Ожидается подтверждение почты {user.Email}.",
                user.FullName,
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = $"Регистрация успешна! На почту {user.Email} отправлен 6-значный код подтверждения.";
            return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult RegisterAdmin(string? code = null)
    {
        return RedirectToAction(nameof(RegisterStaff), new { code = code });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterStaff(string? code = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToUserHome();
        }

        var model = new RegisterStaffViewModel();
        var clientIp = GetClientIp();

        if (_regSecurity.IsBlocked(clientIp, out int remaining, out var retryAfter))
        {
            model.IsIpBlocked = true;
            model.RemainingAttempts = 0;
            ModelState.AddModelError(string.Empty, $"Превышен лимит попыток регистрации (максимум 3 попытки с одного IP). Доступ временно заблокирован на {(int)Math.Ceiling(retryAfter?.TotalMinutes ?? 15)} мин.");
        }
        else
        {
            model.RemainingAttempts = remaining;
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var clean = code.Trim().ToUpper();
            var token = await _db.SystemInviteTokens.FirstOrDefaultAsync(t => t.Token.ToUpper() == clean);

            if (token != null)
            {
                var hasConfirmedAdmin = await _db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.IsEmailConfirmed);
                if (token.TargetRole == UserRole.Admin && !hasConfirmedAdmin)
                {
                    token.IsUsed = false;
                    await _db.SaveChangesAsync();
                }

                if (token.IsUsed)
                {
                    ModelState.AddModelError("InviteCode", "Этот инвайт-код уже был использован.");
                }
                else if (token.ExpiresAt.HasValue && token.ExpiresAt < DateTime.UtcNow)
                {
                    ModelState.AddModelError("InviteCode", "Срок действия этого инвайт-кода истек (инвайт-коды действуют 5 минут). Запросите новый код у Администратора.");
                }
                else
                {
                    model.InviteCode = token.Token;
                    model.TargetRole = token.TargetRole;
                    model.Note = token.Note;
                    model.IsCodeLocked = true;
                    if (token.ExpiresAt.HasValue)
                    {
                        model.RemainingCodeSeconds = Math.Max(0, (int)(token.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds);
                    }
                }
            }
            else
            {
                model.InviteCode = code.Trim();
                ModelState.AddModelError("InviteCode", "Инвайт-код не найден в системе.");
            }
        }

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("register")]
    public async Task<IActionResult> RegisterStaff(RegisterStaffViewModel model)
    {
        var clientIp = GetClientIp();

        if (_regSecurity.IsBlocked(clientIp, out int remainingAttempts, out var retryAfter))
        {
            model.IsIpBlocked = true;
            model.RemainingAttempts = 0;
            ModelState.AddModelError(string.Empty, $"Превышено максимальное количество попыток регистрации (3) с вашего IP-адреса. Доступ временно заблокирован на {(int)Math.Ceiling(retryAfter?.TotalMinutes ?? 15)} мин.");
            return View(model);
        }

        int remaining = _regSecurity.RecordAttempt(clientIp);
        model.RemainingAttempts = remaining;

        if (string.IsNullOrWhiteSpace(model.InviteCode))
        {
            ModelState.AddModelError("InviteCode", "Укажите инвайт-код.");
        }

        var clean = model.InviteCode?.Trim().ToUpper() ?? "";
        var token = await _db.SystemInviteTokens.FirstOrDefaultAsync(t => t.Token.ToUpper() == clean);

        if (token == null)
        {
            ModelState.AddModelError("InviteCode", "Недействительный инвайт-код.");
        }
        else
        {
            var hasConfirmedAdmin = await _db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.IsEmailConfirmed);
            if (token.TargetRole == UserRole.Admin && !hasConfirmedAdmin)
            {
                token.IsUsed = false;
                await _db.SaveChangesAsync();
            }

            if (token.IsUsed)
            {
                ModelState.AddModelError("InviteCode", "Этот инвайт-код уже был использован.");
            }
            else if (token.ExpiresAt.HasValue && token.ExpiresAt < DateTime.UtcNow)
            {
                ModelState.AddModelError("InviteCode", "Срок действия инвайт-кода истек (коды действуют 5 минут). Запросите новый код у Администратора.");
            }
            else
            {
                model.TargetRole = token.TargetRole;
                model.Note = token.Note;
                if (token.ExpiresAt.HasValue)
                {
                    model.RemainingCodeSeconds = Math.Max(0, (int)(token.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Username))
        {
            var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == model.Username.Trim().ToLower());
            if (existingUser != null)
            {
                if (!existingUser.IsEmailConfirmed)
                {
                    _db.Users.Remove(existingUser);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    ModelState.AddModelError("Username", "Пользователь с таким логином уже занят.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            var cleanEmail = model.Email.Trim().ToLower();
            var existingEmailUser = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == cleanEmail);
            if (existingEmailUser != null)
            {
                if (!existingEmailUser.IsEmailConfirmed)
                {
                    _db.Users.Remove(existingEmailUser);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    ModelState.AddModelError("Email", "Пользователь с таким адресом электронной почты уже зарегистрирован.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var user = new User
            {
                Username = model.Username.Trim(),
                FullName = model.FullName.Trim(),
                Email = model.Email.Trim(),
                Role = token!.TargetRole,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                IsEmailConfirmed = false
            };

            user.PasswordHash = _authService.HashPassword(user, model.Password);

            // Если это регистрация первого Администратора системы через мастер-токен — удаляем тестовых пользователей
            if (user.Role == UserRole.Admin)
            {
                var seedUsers = await _db.Users.Where(u => u.Username == "admin" || u.Username == "teacher").ToListAsync();
                if (seedUsers.Any())
                {
                    _db.Users.RemoveRange(seedUsers);
                }
            }

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            token.IsUsed = true;
            token.UsedByUserId = user.Id;
            token.UsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Генерируем 6-значный код подтверждения email
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.EmailConfirmationCode = code;
            user.EmailConfirmationExpiresAt = DateTime.UtcNow.AddMinutes(15);
            await _db.SaveChangesAsync();

            await _emailService.SendConfirmationCodeAsync(user.Email!, code, user.FullName);

            await _eventLogger.LogActionAsync(
                user.Id,
                "REGISTER_STAFF",
                $"Регистрация сотрудника {user.FullName} ({user.Username}) с ролью {user.Role}. Использован инвайт {token.Token}.",
                user.FullName,
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = $"Регистрация успешна! На почту {user.Email} отправлен 6-значный код подтверждения.";
            return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ValidateInviteCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Json(new { isValid = false, message = "Введите инвайт-код." });
        }

        var cleanCode = code.Trim().ToUpper().Replace(" ", "");
        var cleanRaw = cleanCode.Replace("-", "");

        // 1. Проверяем служебные инвайт-токены (Преподаватель / Администратор)
        var staffToken = await _db.SystemInviteTokens.FirstOrDefaultAsync(t => t.Token.ToUpper() == cleanCode);
        if (staffToken != null)
        {
            if (staffToken.IsUsed)
            {
                return Json(new { isValid = false, expired = true, message = "Этот инвайт-код уже был использован." });
            }
            if (staffToken.ExpiresAt.HasValue && staffToken.ExpiresAt.Value < DateTime.UtcNow)
            {
                return Json(new { isValid = false, expired = true, message = "Срок действия инвайт-кода истек (коды действуют 5 минут)." });
            }

            int remainingSec = staffToken.ExpiresAt.HasValue 
                ? Math.Max(0, (int)(staffToken.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds) 
                : 300;

            string roleName = staffToken.TargetRole == UserRole.Admin ? "Администратор" : "Преподаватель";
            return Json(new 
            { 
                isValid = true, 
                type = "staff", 
                role = roleName, 
                note = staffToken.Note,
                remainingSeconds = remainingSec,
                message = $"Инвайт подтвержден: роль {roleName}"
            });
        }

        // 2. Проверяем инвайт-коды учебных групп
        var group = await _db.StudentGroups.FirstOrDefaultAsync(g => 
            g.InviteCode.ToUpper() == cleanCode || 
            g.InviteCode.ToUpper().Replace("-", "") == cleanRaw);

        if (group != null)
        {
            if (group.InviteCodeExpiresAt.HasValue && group.InviteCodeExpiresAt.Value < DateTime.UtcNow)
            {
                return Json(new 
                { 
                    isValid = false, 
                    expired = true, 
                    message = "Срок действия инвайт-кода истек. Инвайт-коды действуют ровно 5 минут в целях безопасности. Попросите преподавателя обновить код." 
                });
            }

            int remainingSec = group.InviteCodeExpiresAt.HasValue 
                ? Math.Max(0, (int)(group.InviteCodeExpiresAt.Value - DateTime.UtcNow).TotalSeconds) 
                : 300;

            return Json(new 
            { 
                isValid = true, 
                type = "group", 
                groupId = group.Id,
                name = group.Name, 
                description = group.Description,
                remainingSeconds = remainingSec,
                message = $"Инвайт подтвержден: Группа {group.Name}"
            });
        }

        return Json(new { isValid = false, message = "Инвайт-код не найден в системе." });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifyEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToAction(nameof(Login));
        }

        return View(new VerifyEmailViewModel
        {
            Email = email.Trim()
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var cleanEmail = model.Email.Trim().ToLower();
        var user = await _db.Users
            .Include(u => u.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == cleanEmail);

        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Пользователь с указанным адресом почты не найден.");
            return View(model);
        }

        if (user.IsEmailConfirmed)
        {
            TempData["SuccessMessage"] = "Адрес почты уже подтвержден! Пожалуйста, выполните вход.";
            return RedirectToAction(nameof(Login));
        }

        if (string.IsNullOrEmpty(user.EmailConfirmationCode) || 
            user.EmailConfirmationCode != model.Code.Trim() ||
            user.EmailConfirmationExpiresAt < DateTime.UtcNow)
        {
            ModelState.AddModelError("Code", "Неверный или истекший код подтверждения.");
            return View(model);
        }

        user.IsEmailConfirmed = true;
        user.EmailConfirmationCode = null;
        user.EmailConfirmationExpiresAt = null;
        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            user.Id,
            "EMAIL_CONFIRMED",
            $"Почта {user.Email} успешно подтверждена студентом {user.FullName}",
            user.FullName,
            null,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        await _authService.SignInUserAsync(HttpContext, user);
        TempData["SuccessMessage"] = "Почта успешно подтверждена! Добро пожаловать в TechHub.";
        return RedirectToUserHome(user.Role);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendConfirmationCode(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToAction(nameof(Login));
        }

        var cleanEmail = email.Trim().ToLower();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == cleanEmail);
        if (user != null && !user.IsEmailConfirmed)
        {
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.EmailConfirmationCode = code;
            user.EmailConfirmationExpiresAt = DateTime.UtcNow.AddMinutes(15);
            await _db.SaveChangesAsync();

            await _emailService.SendConfirmationCodeAsync(user.Email!, code, user.FullName);
            TempData["SuccessMessage"] = $"Новый код подтверждения отправлен на {user.Email}.";
            return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
        }

        TempData["WarningMessage"] = "Пользователь не найден или адрес уже подтвержден.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToUserHome();
        }
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var query = model.EmailOrUsername.Trim().ToLower();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.Email != null && u.Email.ToLower() == query) ||
            u.Username.ToLower() == query);

        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            ModelState.AddModelError(string.Empty, "Пользователь с указанным логином/email не найден, либо у него не привязан email. Обратитесь к преподавателю за сбросом пароля.");
            return View(model);
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        user.PasswordResetCode = code;
        user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(15);
        await _db.SaveChangesAsync();

        await _emailService.SendPasswordResetCodeAsync(user.Email, code, user.FullName);

        await _eventLogger.LogActionAsync(
            user.Id,
            "PASSWORD_RESET_REQUEST",
            $"Запрос сброса пароля для {user.FullName} ({user.Username}). Код выслан на {user.Email}."
        );

        TempData["SuccessMessage"] = $"Код для восстановления пароля отправлен на почту {MaskEmail(user.Email)}.";
        return RedirectToAction(nameof(ResetPassword), new { email = user.Email });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(new ResetPasswordViewModel
        {
            Email = email.Trim()
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var cleanEmail = model.Email.Trim().ToLower();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == cleanEmail);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Пользователь не найден.");
            return View(model);
        }

        if (string.IsNullOrEmpty(user.PasswordResetCode) ||
            user.PasswordResetCode != model.Code.Trim() ||
            user.PasswordResetExpiresAt < DateTime.UtcNow)
        {
            ModelState.AddModelError("Code", "Неверный или истекший код сброса пароля.");
            return View(model);
        }

        user.PasswordHash = _authService.HashPassword(user, model.NewPassword);
        user.PasswordResetCode = null;
        user.PasswordResetExpiresAt = null;
        user.IsEmailConfirmed = true; // Сброс по подтвержденному email также подтверждает почту
        await _db.SaveChangesAsync();

        await _eventLogger.LogActionAsync(
            user.Id,
            "PASSWORD_RESET_SUCCESS",
            $"Пользователь {user.FullName} ({user.Username}) успешно сменил пароль по email коду."
        );

        TempData["SuccessMessage"] = "Пароль успешно обновлен! Теперь вы можете войти в кабинет.";
        return RedirectToAction(nameof(Login));
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2) return email;
        var name = parts[0];
        var domain = parts[1];
        var maskedName = name.Length <= 2 ? name[0] + "***" : name[0] + new string('*', Math.Max(1, name.Length - 2)) + name[^1];
        return $"{maskedName}@{domain}";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        int? userId = null;
        if (int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id))
        {
            userId = id;
        }

        await _eventLogger.LogActionAsync(
            userId,
            "LOGOUT",
            $"Выход из системы пользователя {User.Identity?.Name}",
            null,
            null,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        await _authService.SignOutUserAsync(HttpContext);
        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private IActionResult RedirectToUserHome(UserRole? role = null)
    {
        var roleName = role?.ToString() ?? User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (roleName == UserRole.Teacher.ToString() || roleName == UserRole.Admin.ToString())
        {
            return RedirectToAction("Index", "Teacher");
        }
        return RedirectToAction("Index", "Student");
    }
}
