namespace RUOK.Application;

public enum AppearanceMode
{
    System,
    Light,
    Dark
}

public sealed record AppSettings
{
    public AppearanceMode Appearance { get; init; } = AppearanceMode.System;
    public int RetentionDays { get; init; } = 365;
    public bool ReducedMotion { get; init; }
    public bool PrivacyAccepted { get; init; }
    public NotificationPreferences Notifications { get; init; } = new();
    public DateTimeOffset? NextReminderUtc { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Appearance))
            throw new ArgumentOutOfRangeException(nameof(Appearance));
        if (RetentionDays is not (0 or 30 or 90 or 365))
            throw new ArgumentOutOfRangeException(nameof(RetentionDays));
        ArgumentNullException.ThrowIfNull(Notifications);
        Notifications.Validate();
    }
}
