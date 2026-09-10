namespace RUOK.Domain;

public static class TrendCalculator
{
    public static TrendSummary Summarize(
        IEnumerable<PulseEntry> entries,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(zone);

        if (startInclusive >= endExclusive)
        {
            throw new ArgumentException("The interval start must precede its end.", nameof(endExclusive));
        }

        if (endExclusive - startInclusive > TimeSpan.FromDays(3660))
        {
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "The interval must not exceed 3660 days.");
        }

        var (firstDate, lastDate) = GetDateBounds(startInclusive, endExclusive, zone);
        var dailyTotals = new Dictionary<DateOnly, (int Count, long MoodSum)>();
        var moodCounts = new int[5];
        var count = 0;
        long moodSum = 0;
        long stressSum = 0;
        var stressCount = 0;
        long energySum = 0;
        var energyCount = 0;

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                throw new ArgumentException("Entries must not contain null values.", nameof(entries));
            }

            if (entry.RecordedAt < startInclusive || entry.RecordedAt >= endExclusive)
            {
                continue;
            }

            var mood = (int)entry.Mood;
            var date = GetLocalDate(entry.RecordedAt, zone);
            dailyTotals.TryGetValue(date, out var day);
            count++;
            moodSum += mood;
            moodCounts[mood - 1]++;
            dailyTotals[date] = (day.Count + 1, day.MoodSum + mood);
            if (date < firstDate)
            {
                firstDate = date;
            }

            if (date > lastDate)
            {
                lastDate = date;
            }

            if (entry.Stress is int stress)
            {
                stressSum += stress;
                stressCount++;
            }

            if (entry.Energy is int energy)
            {
                energySum += energy;
                energyCount++;
            }
        }

        var distribution = new MoodCount[5];
        for (var index = 0; index < distribution.Length; index++)
        {
            distribution[index] = new MoodCount((Mood)(index + 1), moodCounts[index]);
        }

        var days = new DailyMood[lastDate.DayNumber - firstDate.DayNumber + 1];
        for (var index = 0; index < days.Length; index++)
        {
            var date = firstDate.AddDays(index);
            dailyTotals.TryGetValue(date, out var day);
            days[index] = new DailyMood(date, day.Count, Average(day.MoodSum, day.Count));
        }

        return new TrendSummary(
            count,
            Average(moodSum, count),
            Average(stressSum, stressCount),
            stressCount,
            Average(energySum, energyCount),
            energyCount,
            Array.AsReadOnly(distribution),
            Array.AsReadOnly(days));
    }

    private static DateOnly GetLocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private static (DateOnly First, DateOnly Last) GetDateBounds(
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        TimeZoneInfo zone)
    {
        var first = GetLocalDate(startInclusive, zone);
        // Subtract in UTC so even an endpoint at DateTime.MinValue with a negative offset is safe.
        var last = GetLocalDate(endExclusive.ToUniversalTime().AddTicks(-1), zone);
        if (first > last)
        {
            (first, last) = (last, first);
        }

        if (zone.SupportsDaylightSavingTime)
        {
            // A repeated midnight can visit a date absent from both endpoints, even with no entries.
            var firstCandidate = Math.Max(DateOnly.MinValue.DayNumber, first.DayNumber - 2);
            var lastCandidate = Math.Min(DateOnly.MaxValue.DayNumber, last.DayNumber + 2);
            for (var dayNumber = firstCandidate; dayNumber <= lastCandidate; dayNumber++)
            {
                var midnight = DateOnly.FromDayNumber(dayNumber).ToDateTime(TimeOnly.MinValue);
                if (!zone.IsAmbiguousTime(midnight))
                {
                    continue;
                }

                foreach (var offset in zone.GetAmbiguousTimeOffsets(midnight))
                {
                    var utcTicks = midnight.Ticks - offset.Ticks;
                    if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
                    {
                        continue;
                    }

                    IncludeInstant(utcTicks);
                    if (utcTicks > DateTime.MinValue.Ticks)
                    {
                        IncludeInstant(utcTicks - 1);
                    }
                }
            }
        }

        return (first, last);

        void IncludeInstant(long utcTicks)
        {
            var instant = new DateTimeOffset(utcTicks, TimeSpan.Zero);
            if (instant < startInclusive || instant >= endExclusive)
            {
                return;
            }

            var date = GetLocalDate(instant, zone);
            if (date < first)
            {
                first = date;
            }

            if (date > last)
            {
                last = date;
            }
        }
    }

    private static double? Average(long sum, int count) => count == 0 ? null : (double)sum / count;
}
