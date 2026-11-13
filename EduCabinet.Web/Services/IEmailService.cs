namespace EduCabinet.Web.Services;

public interface IEmailService
{
    /// <summary>
    /// Отправляет 6-значный код подтверждения адреса электронной почты при регистрации.
    /// </summary>
    Task<bool> SendConfirmationCodeAsync(string toEmail, string code, string fullName);

    /// <summary>
    /// Отправляет 6-значный код для сброса и восстановления забытого пароля.
    /// </summary>
    Task<bool> SendPasswordResetCodeAsync(string toEmail, string code, string fullName);
}
