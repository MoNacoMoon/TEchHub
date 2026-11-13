using System.ComponentModel.DataAnnotations;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Введите логин")]
    [Display(Name = "Логин")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите пароль")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Запомнить меня")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Введите логин")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Логин должен быть от 3 до 50 символов")]
    [Display(Name = "Логин")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите ФИО")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "ФИО должно быть от 3 до 100 символов")]
    [Display(Name = "ФИО студента")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите адрес электронной почты")]
    [EmailAddress(ErrorMessage = "Введите корректный адрес электронной почты (например: student@mail.ru)")]
    [Display(Name = "Электронная почта")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите инвайт-код учебной группы")]
    [Display(Name = "Инвайт-код группы")]
    public string InviteCode { get; set; } = string.Empty;

    public int? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? GroupDescription { get; set; }
    public bool IsCodeLocked { get; set; }

    [Required(ErrorMessage = "Введите пароль")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть не менее 6 символов")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Подтверждение пароля")]
    [Compare("Password", ErrorMessage = "Пароли не совпадают")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class VerifyEmailViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите 6-значный код подтверждения")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Код подтверждения состоит из 6 цифр")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "Код подтверждения должен состоять только из 6 цифр")]
    [Display(Name = "Код подтверждения")]
    public string Code { get; set; } = string.Empty;

    public string? DevHintCode { get; set; }
}

public class RegisterStaffViewModel
{
    [Required(ErrorMessage = "Укажите инвайт-код")]
    [Display(Name = "Инвайт-код")]
    public string InviteCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите логин")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Логин должен быть от 3 до 50 символов")]
    [Display(Name = "Логин")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите ФИО")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "ФИО должно быть от 3 до 100 символов")]
    [Display(Name = "ФИО")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите адрес электронной почты")]
    [EmailAddress(ErrorMessage = "Введите корректный адрес электронной почты")]
    [Display(Name = "Электронная почта")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите пароль")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть не менее 6 символов")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Подтверждение пароля")]
    [Compare("Password", ErrorMessage = "Пароли не совпадают")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public UserRole TargetRole { get; set; } = UserRole.Teacher;
    public string RoleNameRussian => TargetRole == UserRole.Admin ? "Администратор" : "Преподаватель";
    public string? Note { get; set; }
    public bool IsCodeLocked { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Укажите адрес электронной почты или логин")]
    [Display(Name = "Email или логин")]
    public string EmailOrUsername { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите 6-значный код сброса пароля")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Код состоит из 6 цифр")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "Код должен состоять только из 6 цифр")]
    [Display(Name = "Код из письма")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите новый пароль")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть не менее 6 символов")]
    [DataType(DataType.Password)]
    [Display(Name = "Новый пароль")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Подтверждение пароля")]
    [Compare("NewPassword", ErrorMessage = "Пароли не совпадают")]
    public string ConfirmNewPassword { get; set; } = string.Empty;

    public string? DevHintCode { get; set; }
}

