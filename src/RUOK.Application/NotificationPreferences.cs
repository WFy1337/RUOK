namespace RUOK.Application;

public enum ReminderMode
{
    Disabled,
    Timed,
    ActivityAware
}

public enum NotificationFaceAction
{
    SaveMood,
    OpenPulseCheck
}

public enum NotificationLayout
{
    Faces,
    SurveyAndSkip
}

public sealed record NotificationPreferences
{
    public ReminderMode Mode { get; init; }
    public int IntervalMinutes { get; init; } = 90;
    public TimeOnly WindowStart { get; init; } = new(9, 0);
    public TimeOnly WindowEnd { get; init; } = new(17, 0);
    public bool WeekdaysOnly { get; init; } = true;
    public bool KeepInTray { get; init; } = true;
    public NotificationFaceAction FaceAction { get; init; }
    public NotificationLayout Layout { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(FaceAction) || !Enum.IsDefined(Layout))
            throw new ArgumentOutOfRangeException(nameof(Mode), "Choose a supported notification option.");
        if (IntervalMinutes is < 15 or > 480)
            throw new ArgumentOutOfRangeException(nameof(IntervalMinutes), "Use an interval between 15 and 480 minutes.");
        if (WindowStart == WindowEnd)
            throw new ArgumentException("The reminder window must have different start and end times.");
    }
}

public static class ReminderSchedule
{
    public static bool IsDue(
        AppSettings settings, DateTimeOffset now, DateTimeOffset? latestCheckIn, TimeZoneInfo zone)
    {
        if (!settings.PrivacyAccepted || settings.Notifications.Mode == ReminderMode.Disabled
            || settings.NextReminderUtc is not { } next || now < next)
            return false;
        if (latestCheckIn is { } latest && now - latest < TimeSpan.FromMinutes(settings.Notifications.IntervalMinutes))
            return false;
        return IsWithinWindow(settings.Notifications, now, zone);
    }

    public static bool IsWithinWindow(NotificationPreferences preferences, DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var overnight = preferences.WindowStart > preferences.WindowEnd;
        if (!overnight && (time < preferences.WindowStart || time >= preferences.WindowEnd))
            return false;
        if (overnight)
        {
            if (time >= preferences.WindowEnd && time < preferences.WindowStart)
                return false;
            if (time < preferences.WindowEnd)
                date = date.AddDays(-1);
        }
        return !preferences.WeekdaysOnly || date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
}

public sealed class ActivityReminderGate(TimeProvider clock)
{
    private bool _wasIdle;
    private long? _returnedAt;
    private long? _lastObservation;

    public bool CanNotify(TimeSpan idleFor)
    {
        if (idleFor < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleFor));
        var now = clock.GetTimestamp();
        if (_lastObservation is { } last && clock.GetElapsedTime(last, now) >= TimeSpan.FromMinutes(5))
        {
            _wasIdle = true;
            _returnedAt = null;
        }
        _lastObservation = now;
        if (idleFor >= TimeSpan.FromMinutes(5))
        {
            _wasIdle = true;
            _returnedAt = null;
            return false;
        }
        if (!_wasIdle)
            return true;
        _returnedAt ??= clock.GetTimestamp();
        return clock.GetElapsedTime(_returnedAt.Value) >= TimeSpan.FromMinutes(2);
    }

    public void Reset()
    {
        _wasIdle = false;
        _returnedAt = null;
        _lastObservation = null;
    }
}
