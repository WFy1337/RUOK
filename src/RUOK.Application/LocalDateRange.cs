namespace RUOK.Application;

public static class LocalDateRange
{
    public static (DateTimeOffset Start, DateTimeOffset End) ToUtc(
        DateOnly firstDay, DateOnly lastDay, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (firstDay > lastDay || lastDay == DateOnly.MaxValue)
            throw new ArgumentException("Choose a valid inclusive date range.");
        var start = StartOfDay(firstDay, timeZone);
        var end = StartOfDay(lastDay.AddDays(1), timeZone);
        if (start >= end)
            throw new ArgumentException("The selected dates do not contain a valid local time.");
        return (start, end);
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones change clocks at midnight, or skip an entire calendar date.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
