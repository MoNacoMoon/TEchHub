using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Services;

public interface IAuthService
{
    string HashPassword(User user, string plainPassword);
    bool VerifyPassword(User user, string plainPassword);
    Task<User?> AuthenticateAsync(string username, string plainPassword);
    Task SignInUserAsync(HttpContext httpContext, User user);
    Task SignOutUserAsync(HttpContext httpContext);
    Task<User> RegisterStudentAsync(string username, string plainPassword, string fullName, string? email, int? groupId);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _hasher;

    public AuthService(AppDbContext db)
    {
        _db = db;
        _hasher = new PasswordHasher<User>();
    }

    public string HashPassword(User user, string plainPassword)
    {
        return _hasher.HashPassword(user, plainPassword);
    }

    public bool VerifyPassword(User user, string plainPassword)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, plainPassword);
        return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded;
    }

    public async Task<User?> AuthenticateAsync(string username, string plainPassword)
    {
        var user = await _db.Users
            .Include(u => u.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.Trim().ToLower() && u.IsActive);

        if (user == null) return null;

        if (VerifyPassword(user, plainPassword))
        {
            return user;
        }

        return null;
    }

    public async Task SignInUserAsync(HttpContext httpContext, User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("FullName", user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var group = user.GroupMemberships.FirstOrDefault()?.Group;
        if (group != null)
        {
            claims.Add(new Claim("GroupName", group.Name));
            claims.Add(new Claim("GroupId", group.Id.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
    }

    public async Task SignOutUserAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<User> RegisterStudentAsync(string username, string plainPassword, string fullName, string? email, int? groupId)
    {
        var existing = await _db.Users.AnyAsync(u => u.Username.ToLower() == username.Trim().ToLower());
        if (existing)
        {
            throw new InvalidOperationException("Пользователь с таким логином уже существует");
        }

        var user = new User
        {
            Username = username.Trim(),
            FullName = fullName.Trim(),
            Email = email?.Trim(),
            Role = UserRole.Student,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        user.PasswordHash = HashPassword(user, plainPassword);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (groupId.HasValue)
        {
            var membership = new GroupMembership
            {
                StudentId = user.Id,
                GroupId = groupId.Value
            };
            _db.GroupMemberships.Add(membership);
            await _db.SaveChangesAsync();
        }

        return user;
    }
}
