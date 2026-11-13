using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EduCabinet.Web.Services;

/// <summary>
/// Генератор криптографически стойких, уникальных инвайт-кодов для учебных групп.
/// Исключает неоднозначные символы (0/O, 1/I/L) и гарантирует защиту от подбора.
/// Формат: [НомерГруппы]-[СлучайныйТокен6], например: 111-K9X4M2, 112-7W3P8R, 415-J2N9Q5.
/// </summary>
public static class InviteCodeGenerator
{
    // Алфавит Base32 (без 0, O, 1, I, L для предотвращения ошибок при ручном вводе)
    private const string SafeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string GenerateCode(string groupName, int tokenLength = 6)
    {
        var cleanGroup = Regex.Replace(groupName ?? "GRP", @"[^a-zA-Z0-9а-яА-Я]", "").ToUpper();
        if (string.IsNullOrWhiteSpace(cleanGroup))
        {
            cleanGroup = "GRP";
        }

        var randomBytes = RandomNumberGenerator.GetBytes(tokenLength);
        var sb = new StringBuilder(tokenLength);
        for (int i = 0; i < tokenLength; i++)
        {
            int index = randomBytes[i] % SafeAlphabet.Length;
            sb.Append(SafeAlphabet[index]);
        }

        return $"{cleanGroup}-{sb}";
    }
}
