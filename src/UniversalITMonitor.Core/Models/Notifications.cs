namespace UniversalITMonitor.Core.Models;

public sealed class NotificationSettingsDocument
{
    public NotificationSettings Settings { get; set; } = new();
}

public sealed class NotificationSettings
{
    public bool TelegramEnabled { get; set; }

    public string? TelegramChatId { get; set; }

    public string? ProtectedTelegramBotToken { get; set; }

    public bool EmailEnabled { get; set; }

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public string? SmtpFromAddress { get; set; }

    public string? SmtpToAddress { get; set; }

    public bool SmtpUseTls { get; set; } = true;

    public string? ProtectedSmtpUserName { get; set; }

    public string? ProtectedSmtpPassword { get; set; }
}

public sealed class NotificationHistoryDocument
{
    public List<NotificationRecord> Records { get; set; } = [];
}

public sealed class NotificationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NotificationRecordKind Kind { get; set; }

    public Guid? DeviceId { get; set; }

    public string? DeviceName { get; set; }

    public string? IpAddress { get; set; }

    public string? Location { get; set; }

    public string? Group { get; set; }

    public DeviceStatus? PreviousStatus { get; set; }

    public DeviceStatus? Status { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public DateTimeOffset? NotBeforeUtc { get; set; }

    public Guid? DependsOnRecordId { get; set; }

    public bool IsSuppressed { get; set; }

    public long? ResponseTimeMilliseconds { get; set; }

    public string? Details { get; set; }

    public List<NotificationChannel> RequestedChannels { get; set; } = [];

    public List<NotificationDeliveryResult> Deliveries { get; set; } = [];

    public bool IsComplete { get; set; }
}

public sealed class NotificationDeliveryResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NotificationChannel Channel { get; set; }

    public DateTimeOffset AttemptedUtc { get; set; }

    public int AttemptNumber { get; set; } = 1;

    public bool Succeeded { get; set; }

    public bool IsTransient { get; set; }

    public bool DeadLettered { get; set; }

    public DateTimeOffset? NextAttemptUtc { get; set; }

    public string? Detail { get; set; }
}

public enum NotificationRecordKind
{
    DeviceTransition,
    Test
}

public enum NotificationChannel
{
    Telegram,
    Email
}
