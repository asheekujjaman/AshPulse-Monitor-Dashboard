using System.ComponentModel.DataAnnotations;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class NotificationSettingsPageViewModel
{
    public NotificationSettingsFormModel Input { get; set; } = new();
    public bool HasTelegramBotToken { get; set; }
    public bool HasSmtpUserName { get; set; }
    public bool HasSmtpPassword { get; set; }
    public IReadOnlyList<NotificationRecord> History { get; set; } = [];

    public static NotificationSettingsPageViewModel From(NotificationSettingsView settings, IReadOnlyList<NotificationRecord> history) => new()
    {
        Input = new NotificationSettingsFormModel
        {
            TelegramEnabled = settings.TelegramEnabled,
            TelegramChatId = settings.TelegramChatId,
            EmailEnabled = settings.EmailEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpFromAddress = settings.SmtpFromAddress,
            SmtpToAddress = settings.SmtpToAddress,
            SmtpUseTls = settings.SmtpUseTls
        },
        HasTelegramBotToken = settings.HasTelegramBotToken,
        HasSmtpUserName = settings.HasSmtpUserName,
        HasSmtpPassword = settings.HasSmtpPassword,
        History = history
    };

    public NotificationSettingsInput ToInput() => new()
    {
        TelegramEnabled = Input.TelegramEnabled,
        TelegramChatId = Input.TelegramChatId,
        TelegramBotToken = Input.TelegramBotToken,
        ClearTelegramBotToken = Input.ClearTelegramBotToken,
        EmailEnabled = Input.EmailEnabled,
        SmtpHost = Input.SmtpHost,
        SmtpPort = Input.SmtpPort,
        SmtpFromAddress = Input.SmtpFromAddress,
        SmtpToAddress = Input.SmtpToAddress,
        SmtpUseTls = Input.SmtpUseTls,
        SmtpUserName = Input.SmtpUserName,
        SmtpPassword = Input.SmtpPassword,
        ClearSmtpCredentials = Input.ClearSmtpCredentials
    };
}

public sealed class NotificationSettingsFormModel
{
    [Display(Name = "Enable Telegram alerts")]
    public bool TelegramEnabled { get; set; }

    [StringLength(128)]
    [Display(Name = "Telegram chat ID")]
    public string? TelegramChatId { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Telegram bot token")]
    public string? TelegramBotToken { get; set; }

    [Display(Name = "Remove saved Telegram token")]
    public bool ClearTelegramBotToken { get; set; }

    [Display(Name = "Enable email alerts")]
    public bool EmailEnabled { get; set; }

    [StringLength(255)]
    [Display(Name = "SMTP host")]
    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    [Display(Name = "SMTP port")]
    public int SmtpPort { get; set; } = 587;

    [StringLength(254)]
    [Display(Name = "From address")]
    public string? SmtpFromAddress { get; set; }

    [StringLength(254)]
    [Display(Name = "Recipient address")]
    public string? SmtpToAddress { get; set; }

    [Display(Name = "Use SMTP TLS")]
    public bool SmtpUseTls { get; set; } = true;

    [DataType(DataType.Password)]
    [Display(Name = "SMTP username")]
    public string? SmtpUserName { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "SMTP password")]
    public string? SmtpPassword { get; set; }

    [Display(Name = "Remove saved SMTP credentials")]
    public bool ClearSmtpCredentials { get; set; }

}
