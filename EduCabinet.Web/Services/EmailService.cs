using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduCabinet.Web.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IEventLogService _eventLogger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IEventLogService eventLogger)
    {
        _config = config;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    public async Task<bool> SendConfirmationCodeAsync(string toEmail, string code, string fullName)
    {
        string subject = $"Код {code} — подтверждение регистрации в TechHub";

        string textBody = $@"Здравствуйте, {fullName}!

Ваш 6-значный код подтверждения электронной почты: {code}

Код действует в течение 15 минут. Введите его на странице регистрации для активации учетной записи в TechHub.

Если вы не регистрировались на платформе TechHub, просто проигнорируйте это письмо.

---
TechHub • Образовательная платформа
Автоматическое сервисное уведомление";

        string htmlBody = $@"
<!DOCTYPE html>
<html lang=""ru"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Код подтверждения регистрации TechHub</title>
</head>
<body style=""margin: 0; padding: 0; background-color: #0d1117; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased;"">
    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background-color: #0b0f19; padding: 36px 16px;"">
        <tr>
            <td align=""center"">
                <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""max-width: 560px; background-color: #111827; border-radius: 16px; overflow: hidden; box-shadow: 0 10px 30px rgba(0, 0, 0, 0.45); border: 1px solid #1f2937;"">
                    
                    <!-- Верхняя акцентная полоса TechHub (неоновый синий -> индиго -> фиолетовый) -->
                    <tr>
                        <td style=""height: 6px; background: linear-gradient(90deg, #3b82f6 0%, #6366f1 50%, #8b5cf6 100%);""></td>
                    </tr>

                    <!-- Шапка с логотипом TechHub -->
                    <tr>
                        <td style=""padding: 32px 32px 20px 32px; text-align: center;"">
                            <div style=""display: inline-block; padding: 6px 16px; background-color: #1e293b; border: 1px solid #334155; border-radius: 9999px; font-size: 13px; font-weight: 700; color: #60a5fa; letter-spacing: 0.5px; margin-bottom: 16px;"">
                                &#9878;&#65039; TechHub Platform
                            </div>
                            <h1 style=""margin: 0 0 8px 0; font-size: 24px; font-weight: 800; color: #f8fafc; letter-spacing: -0.5px;"">
                                Подтверждение почты
                            </h1>
                            <p style=""margin: 0; font-size: 14px; color: #94a3b8;"">
                                Активация учетной записи в TechHub
                            </p>
                        </td>
                    </tr>

                    <!-- Основное тело письма -->
                    <tr>
                        <td style=""padding: 0 32px 28px 32px;"">
                            <p style=""margin: 0 0 14px 0; font-size: 15px; color: #e2e8f0; line-height: 1.5;"">
                                Здравствуйте, <strong style=""color: #60a5fa;"">{fullName}</strong>!
                            </p>
                            <p style=""margin: 0 0 24px 0; font-size: 14px; color: #94a3b8; line-height: 1.6;"">
                                Вы указали этот адрес электронной почты для регистрации на образовательной платформе <strong style=""color: #f8fafc;"">TechHub</strong>. Введите проверочный код ниже:
                            </p>

                            <!-- Карточка с кодом -->
                            <div style=""background: #0f172a; border: 2px dashed #3b82f6; border-radius: 12px; padding: 24px; text-align: center; margin-bottom: 24px;"">
                                <div style=""font-size: 11px; text-transform: uppercase; letter-spacing: 1.5px; color: #94a3b8; font-weight: 700; margin-bottom: 12px;"">
                                    Одноразовый проверочный код
                                </div>
                                <div style=""display: inline-block; background: #1e293b; padding: 14px 28px; border-radius: 10px; border: 1px solid #334155; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);"">
                                    <span style=""font-family: 'JetBrains Mono', 'SF Mono', Consolas, Menlo, monospace; font-size: 36px; font-weight: 800; letter-spacing: 8px; color: #38bdf8; text-indent: 8px; display: inline-block;"">
                                        {code}
                                    </span>
                                </div>
                                <div style=""margin-top: 14px; font-size: 12px; color: #94a3b8;"">
                                    &#9201; Срок действия: <strong style=""color: #f1f5f9;"">15 минут</strong>
                                </div>
                            </div>

                            <!-- Блок безопасности -->
                            <div style=""background-color: #1e293b; border-radius: 10px; padding: 14px 18px; margin-bottom: 8px; border: 1px solid #334155;"">
                                <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"">
                                    <tr>
                                        <td width=""24"" valign=""top"" style=""font-size: 16px; padding-top: 1px;"">&#128274;</td>
                                        <td style=""font-size: 12px; color: #cbd5e1; line-height: 1.5; padding-left: 8px;"">
                                            Никому не сообщайте данный код. Администрация TechHub и преподаватели никогда не запрашивают ваши проверочные коды.
                                        </td>
                                    </tr>
                                </table>
                            </div>

                            <p style=""margin: 16px 0 0 0; font-size: 12px; color: #64748b; line-height: 1.5; text-align: center;"">
                                Если вы не регистрировались в TechHub, просто проигнорируйте это письмо — учетная запись не будет активирована.
                            </p>
                        </td>
                    </tr>

                    <!-- Футер -->
                    <tr>
                        <td style=""padding: 20px 32px; background-color: #0f172a; border-top: 1px solid #1f2937; text-align: center;"">
                            <div style=""font-size: 12px; font-weight: 600; color: #cbd5e1; margin-bottom: 4px;"">
                                TechHub • Образовательная платформа
                            </div>
                            <div style=""font-size: 11px; color: #64748b;"">
                                &copy; {DateTime.Now.Year} TechHub • Автоматическое сервисное уведомление
                            </div>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

        return await SendEmailInternalAsync(toEmail, subject, textBody, htmlBody, code, "EMAIL_CONFIRMATION");
    }

    public async Task<bool> SendPasswordResetCodeAsync(string toEmail, string code, string fullName)
    {
        string subject = $"Код {code} — сброс пароля в TechHub";

        string textBody = $@"Здравствуйте, {fullName}!

Поступил запрос на сброс пароля для вашей учетной записи в TechHub.

Ваш 6-значный код восстановления пароля: {code}

Код действует в течение 15 минут. Введите его на странице сброса пароля.

Если вы не запрашивали смену пароля, немедленно обратитесь к администратору или преподавателю TechHub.

---
TechHub • Образовательная платформа
Автоматическое сервисное уведомление";

        string htmlBody = $@"
<!DOCTYPE html>
<html lang=""ru"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Сброс пароля TechHub</title>
</head>
<body style=""margin: 0; padding: 0; background-color: #0d1117; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased;"">
    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background-color: #0b0f19; padding: 36px 16px;"">
        <tr>
            <td align=""center"">
                <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""max-width: 560px; background-color: #111827; border-radius: 16px; overflow: hidden; box-shadow: 0 10px 30px rgba(0, 0, 0, 0.45); border: 1px solid #1f2937;"">
                    
                    <!-- Верхняя акцентная полоса безопасности (янтарно-красный градиент) -->
                    <tr>
                        <td style=""height: 6px; background: linear-gradient(90deg, #ef4444 0%, #f97316 50%, #f59e0b 100%);""></td>
                    </tr>

                    <!-- Шапка -->
                    <tr>
                        <td style=""padding: 32px 32px 20px 32px; text-align: center;"">
                            <div style=""display: inline-block; padding: 6px 16px; background-color: #450a0a; border: 1px solid #7f1d1d; border-radius: 9999px; font-size: 13px; font-weight: 700; color: #f87171; letter-spacing: 0.5px; margin-bottom: 16px;"">
                                &#128274; Безопасность TechHub
                            </div>
                            <h1 style=""margin: 0 0 8px 0; font-size: 24px; font-weight: 800; color: #f8fafc; letter-spacing: -0.5px;"">
                                Сброс пароля
                            </h1>
                            <p style=""margin: 0; font-size: 14px; color: #94a3b8;"">
                                Восстановление доступа к аккаунту TechHub
                            </p>
                        </td>
                    </tr>

                    <!-- Основное тело письма -->
                    <tr>
                        <td style=""padding: 0 32px 28px 32px;"">
                            <p style=""margin: 0 0 14px 0; font-size: 15px; color: #e2e8f0; line-height: 1.5;"">
                                Здравствуйте, <strong style=""color: #f87171;"">{fullName}</strong>!
                            </p>
                            <p style=""margin: 0 0 24px 0; font-size: 14px; color: #94a3b8; line-height: 1.6;"">
                                Поступил запрос на смену пароля для вашей учетной записи на платформе <strong style=""color: #f8fafc;"">TechHub</strong>. Введите проверочный код ниже:
                            </p>

                            <!-- Карточка с кодом -->
                            <div style=""background: #0f172a; border: 2px dashed #f59e0b; border-radius: 12px; padding: 24px; text-align: center; margin-bottom: 24px;"">
                                <div style=""font-size: 11px; text-transform: uppercase; letter-spacing: 1.5px; color: #fbbf24; font-weight: 700; margin-bottom: 12px;"">
                                    Код сброса пароля
                                </div>
                                <div style=""display: inline-block; background: #1e293b; padding: 14px 28px; border-radius: 10px; border: 1px solid #334155; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);"">
                                    <span style=""font-family: 'JetBrains Mono', 'SF Mono', Consolas, Menlo, monospace; font-size: 36px; font-weight: 800; letter-spacing: 8px; color: #fbbf24; text-indent: 8px; display: inline-block;"">
                                        {code}
                                    </span>
                                </div>
                                <div style=""margin-top: 14px; font-size: 12px; color: #94a3b8;"">
                                    &#9201; Срок действия: <strong style=""color: #f1f5f9;"">15 минут</strong>
                                </div>
                            </div>

                            <!-- Предупреждение -->
                            <div style=""background-color: #2a1215; border: 1px solid #7f1d1d; border-radius: 10px; padding: 14px 18px; margin-bottom: 8px;"">
                                <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"">
                                    <tr>
                                        <td width=""24"" valign=""top"" style=""font-size: 16px; padding-top: 1px;"">&#9888;&#65039;</td>
                                        <td style=""font-size: 12px; color: #fca5a5; line-height: 1.5; padding-left: 8px;"">
                                            Если вы не запрашивали сброс пароля, немедленно обратитесь к администратору TechHub. Ваш текущий пароль останется неизменным.
                                        </td>
                                    </tr>
                                </table>
                            </div>
                        </td>
                    </tr>

                    <!-- Футер -->
                    <tr>
                        <td style=""padding: 20px 32px; background-color: #0f172a; border-top: 1px solid #1f2937; text-align: center;"">
                            <div style=""font-size: 12px; font-weight: 600; color: #cbd5e1; margin-bottom: 4px;"">
                                TechHub • Образовательная платформа
                            </div>
                            <div style=""font-size: 11px; color: #64748b;"">
                                &copy; {DateTime.Now.Year} TechHub • Все права защищены
                            </div>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

        return await SendEmailInternalAsync(toEmail, subject, textBody, htmlBody, code, "PASSWORD_RESET");
    }

    private async Task<bool> SendEmailInternalAsync(string toEmail, string subject, string textBody, string htmlBody, string code, string actionType)
    {
        var smtpHost = _config["EmailSettings:SmtpServer"];
        var senderEmail = _config["EmailSettings:SenderEmail"] ?? "techhub3112@gmail.com";
        var senderName = _config["EmailSettings:SenderName"] ?? "TechHub";

        _logger.LogInformation("[{ActionType}] Отправка кода {Code} на адрес {Email} от имени {SenderName}", actionType, code, toEmail, senderName);
        await _eventLogger.LogActionAsync(null, actionType, $"Отправлен код {code} на email: {toEmail} (TechHub)");

        if (string.IsNullOrWhiteSpace(smtpHost) || smtpHost == "localhost" || smtpHost == "smtp.example.com")
        {
            _logger.LogInformation("SMTP сервер не сконфигурирован. Код {Code} успешно зарегистрирован в журнале аудита.", code);
            return true;
        }

        try
        {
            int port = int.TryParse(_config["EmailSettings:SmtpPort"], out int p) ? p : 587;
            bool enableSsl = bool.TryParse(_config["EmailSettings:EnableSsl"], out bool ssl) ? ssl : true;
            string username = _config["EmailSettings:Username"] ?? "";
            string password = _config["EmailSettings:Password"] ?? "";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = subject;
            message.Date = DateTimeOffset.Now;
            message.MessageId = MimeUtils.GenerateMessageId();

            var bodyBuilder = new BodyBuilder
            {
                TextBody = textBody,
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = 15000;

            SecureSocketOptions secureOption = port switch
            {
                465 => SecureSocketOptions.SslOnConnect,
                587 => SecureSocketOptions.StartTls,
                25 => SecureSocketOptions.None,
                _ => enableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None
            };

            await client.ConnectAsync(smtpHost, port, secureOption);

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                await client.AuthenticateAsync(username, password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Письмо TechHub успешно доставлено через SMTP на {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить письмо через SMTP на {Email}. Код сохранен в БД и аудите.", toEmail);
            return true;
        }
    }
}
