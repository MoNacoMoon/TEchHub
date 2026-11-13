using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using EduCabinet.Web.Data;
using EduCabinet.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Подгружаем локальные секреты разработки, исключенные из Git
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// База данных SQLite (с поддержкой общего кэша и оптимизацией параллельного доступа)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connString) || connString.Contains("educabinet.db"))
    {
        // Поддержка постоянного хранилища на Amvera Cloud (/data)
        if (Directory.Exists("/data"))
        {
            var dataDbPath = "/data/educabinet.db";
            if (!File.Exists(dataDbPath) && File.Exists("educabinet.db"))
            {
                try { File.Copy("educabinet.db", dataDbPath); } catch { }
            }
            connString = $"Data Source={dataDbPath};Cache=Shared;";
        }
        else
        {
            connString = "Data Source=educabinet.db;Cache=Shared;";
        }
    }
    options.UseSqlite(connString);
});

// Защита данных и синхронизация ключей сессий между репликами
var keysDir = Directory.Exists("/data") ? "/data/dataprotection-keys" : "dataprotection-keys";
try { Directory.CreateDirectory(keysDir); } catch { }
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("TechHub");

// Аутентификация на Cookie (Защита сессионных куки: HttpOnly, SameSite, Secure)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "EduCabinet.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Ограничение частоты запросов (Rate Limiting с поддержкой 500+ студентов и кампусного NAT)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Глобальное разделение:
    // Авторизованные студенты/преподаватели получают персональную квоту по UserId (180 запросов/мин).
    // Неавторизованные гости получают квоту по IP с запасом под общий кампусный NAT (до 1200 запросов/мин).
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"user_{userId}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 180,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"ip_{clientIp}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1200,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // Ограничитель попыток входа (60 в минуту на IP — защищает от ботнетов, но позволяет целой аудитории войти на пару)
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 60;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// Сервисы бизнес-логики
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILectureAccessService, LectureAccessService>();
builder.Services.AddScoped<IGradingService, GradingService>();
builder.Services.AddScoped<IEventLogService, EventLogService>();

// Поддержка MVC и Web API контроллеров
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Swagger / OpenAPI для Web API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Электронный учебный кабинет - Web API",
        Version = "v1",
        Description = "REST API для управления дисциплинами, лекциями, последовательным доступом и тестированием"
    });
});

var app = builder.Build();

// Инициализация базы данных и сидирование (с оптимизацией под 500+ одновременных студентов)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        var env = services.GetRequiredService<IWebHostEnvironment>();

        // Оптимизация параллельной работы SQLite:
        // 1. WAL mode: читатели не блокируют писателей, а писатели не блокируют читателей
        // 2. busy_timeout = 10000ms: автоповтор при одновременной записи вместо падений
        // 3. synchronous = NORMAL: десятикратное ускорение записи при тестировании
        // 4. cache_size = -64000: 64 МБ оперативной памяти под кэш горячих данных
        await context.Database.ExecuteSqlRawAsync(
            "PRAGMA journal_mode = WAL; " +
            "PRAGMA synchronous = NORMAL; " +
            "PRAGMA busy_timeout = 10000; " +
            "PRAGMA temp_store = MEMORY; " +
            "PRAGMA cache_size = -64000;");

        await DbInitializer.SeedAsync(context, env);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ошибка при инициализации базы данных");
    }
}

// Конвейер обработки HTTP-запросов
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Заголовки безопасности (Security Headers: Clickjacking, XSS, MIME sniffing)
// Важно: font-src и style-src разрешают cdn.jsdelivr.net и fonts.gstatic.com для корректного отображения иконок Bootstrap
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://fonts.googleapis.com; " +
        "font-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://fonts.gstatic.com data:; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'self';");

    await next();
});

// Swagger UI доступен только в среде разработки для предотвращения утечки структуры API
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EduCabinet Web API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Применение ограничения частоты запросов
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Маршрутизация для Web API контроллеров
app.MapControllers();

// Маршрутизация по умолчанию для MVC контроллеров
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
