using System.Collections;

namespace RUOK.Domain.Tests;

[TestClass]
public sealed class TrendCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo HalfHourZone = TimeZoneInfo.CreateCustomTimeZone(
        "Synthetic+05:30", TimeSpan.FromMinutes(330), "Synthetic +05:30", "Synthetic +05:30");
    private static readonly TimeZoneInfo DstZone = CreateDstZone();

    [TestMethod]
    public void AveragesUseIndependentDenominatorsAndUnweightedEntryMoods()
    {
        var summary = TrendCalculator.Summarize(
            new[]
            {
                Entry(Start, Mood.VeryLow, stress: 1),
                Entry(Start.AddHours(1), Mood.Low, energy: 2),
                Entry(Start.AddDays(1), Mood.Great, stress: 5)
            },
            Start, Start.AddDays(2), TimeZoneInfo.Utc);

        Assert.AreEqual(3, summary.Count);
        Assert.AreEqual(8d / 3, summary.AverageMood);
        Assert.AreEqual(3d, summary.AverageStress);
        Assert.AreEqual(2, summary.StressCount);
        Assert.AreEqual(2d, summary.AverageEnergy);
        Assert.AreEqual(1, summary.EnergyCount);
        CollectionAssert.AreEqual(new[] { 1, 1, 0, 0, 1 }, summary.Distribution.Select(bin => bin.Count).ToArray());
        Assert.AreEqual(new DailyMood(new DateOnly(2026, 1, 1), 2, 1.5), summary.Days[0]);
        Assert.AreEqual(new DailyMood(new DateOnly(2026, 1, 2), 1, 5), summary.Days[1]);
    }

    [TestMethod]
    public void MissingOptionalScoresAreNotImputed()
    {
        var summary = TrendCalculator.Summarize(
            new[] { Entry(Start, Mood.Good), Entry(Start.AddHours(1), Mood.Great) },
            Start, Start.AddDays(1), TimeZoneInfo.Utc);

        Assert.AreEqual(2, summary.Count);
        Assert.AreEqual(4.5, summary.AverageMood);
        Assert.AreEqual(0, summary.StressCount);
        Assert.IsNull(summary.AverageStress);
        Assert.AreEqual(0, summary.EnergyCount);
        Assert.IsNull(summary.AverageEnergy);
    }

    [TestMethod]
    public void EmptyInputHasAllMoodBinsAndContinuousNullDailyAverages()
    {
        var summary = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), Start.AddHours(12), Start.AddDays(3), TimeZoneInfo.Utc);

        Assert.AreEqual(0, summary.Count);
        Assert.IsNull(summary.AverageMood);
        Assert.IsNull(summary.AverageStress);
        Assert.AreEqual(0, summary.StressCount);
        Assert.IsNull(summary.AverageEnergy);
        Assert.AreEqual(0, summary.EnergyCount);
        CollectionAssert.AreEqual(Enum.GetValues<Mood>(), summary.Distribution.Select(bin => bin.Mood).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0 }, summary.Distribution.Select(bin => bin.Count).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new DailyMood(new DateOnly(2026, 1, 1), 0, null),
                new DailyMood(new DateOnly(2026, 1, 2), 0, null),
                new DailyMood(new DateOnly(2026, 1, 3), 0, null)
            },
            summary.Days.ToArray());
    }

    [TestMethod]
    public void StartIsInclusiveAndEndIsExclusiveToTheTick()
    {
        var end = Start.AddDays(1);
        var summary = TrendCalculator.Summarize(
            new[]
            {
                Entry(Start.AddTicks(-1), Mood.VeryLow, stress: 1, energy: 1),
                Entry(Start, Mood.Low, stress: 2),
                Entry(Start.AddHours(12), Mood.Neutral, energy: 4),
                Entry(end.AddTicks(-1), Mood.Good, stress: 4),
                Entry(end, Mood.Great, stress: 5, energy: 5),
                Entry(end.AddTicks(1), Mood.VeryLow, stress: 1, energy: 1)
            },
            Start, end, TimeZoneInfo.Utc);

        Assert.AreEqual(3, summary.Count);
        Assert.AreEqual(3d, summary.AverageMood);
        Assert.AreEqual(2, summary.StressCount);
        Assert.AreEqual(3d, summary.AverageStress);
        Assert.AreEqual(1, summary.EnergyCount);
        Assert.AreEqual(4d, summary.AverageEnergy);
        CollectionAssert.AreEqual(new[] { 0, 1, 1, 1, 0 }, summary.Distribution.Select(bin => bin.Count).ToArray());
        Assert.HasCount(1, summary.Days);
    }

    [TestMethod]
    public void OffsetRepresentationsAreFilteredByTheirActualInstants()
    {
        var summary = TrendCalculator.Summarize(
            new[]
            {
                Entry(new DateTimeOffset(2026, 1, 1, 1, 59, 59, TimeSpan.FromHours(2)), Mood.VeryLow),
                Entry(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.FromHours(2)), Mood.Good),
                Entry(new DateTimeOffset(2026, 1, 1, 2, 30, 0, TimeSpan.FromHours(2)), Mood.Neutral),
                Entry(new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.FromHours(2)), Mood.Great)
            },
            Start, Start.AddHours(1), TimeZoneInfo.Utc);

        Assert.AreEqual(2, summary.Count);
        Assert.AreEqual(3.5, summary.AverageMood);
        Assert.HasCount(1, summary.Days);
    }

    [TestMethod]
    public void GroupingUsesTheSuppliedZoneRatherThanTheStoredTimeZoneId()
    {
        var start = new DateTimeOffset(2025, 12, 31, 18, 30, 0, TimeSpan.Zero);
        var entries = new[]
        {
            Entry(Start.AddHours(18).AddMinutes(30).AddTicks(-1), Mood.Low),
            Entry(Start.AddHours(18).AddMinutes(30), Mood.Great)
        };

        var summary = TrendCalculator.Summarize(entries, start, start.AddDays(2), HalfHourZone);

        CollectionAssert.AreEqual(
            new[]
            {
                new DailyMood(new DateOnly(2026, 1, 1), 1, 2),
                new DailyMood(new DateOnly(2026, 1, 2), 1, 5)
            },
            summary.Days.ToArray());
    }

    [TestMethod]
    public void CrossingUtcMidnightNeedNotCrossTheLocalDate()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Synthetic-05", TimeSpan.FromHours(-5), "Synthetic -05", "Synthetic -05");
        var start = Start.AddHours(22);
        var summary = TrendCalculator.Summarize(
            new[] { Entry(start, Mood.Low), Entry(Start.AddDays(1).AddHours(1), Mood.Good) },
            start, Start.AddDays(1).AddHours(2), zone);

        Assert.HasCount(1, summary.Days);
        Assert.AreEqual(new DailyMood(new DateOnly(2026, 1, 1), 2, 3), summary.Days[0]);
    }

    [TestMethod]
    public void SpringForwardDayIsOneLocalBinDespiteHavingTwentyThreeHours()
    {
        var start = new DateTimeOffset(2025, 3, 9, 0, 0, 0, TimeSpan.FromHours(-5));
        var end = new DateTimeOffset(2025, 3, 10, 0, 0, 0, TimeSpan.FromHours(-4));
        var summary = TrendCalculator.Summarize(
            new[]
            {
                Entry(new DateTimeOffset(2025, 3, 9, 1, 59, 59, TimeSpan.FromHours(-5)), Mood.Low),
                Entry(new DateTimeOffset(2025, 3, 9, 3, 0, 0, TimeSpan.FromHours(-4)), Mood.Good),
                Entry(end, Mood.Great)
            },
            start, end, DstZone);

        Assert.AreEqual(TimeSpan.FromHours(23), end - start);
        Assert.HasCount(1, summary.Days);
        Assert.AreEqual(new DailyMood(new DateOnly(2025, 3, 9), 2, 3), summary.Days[0]);
    }

    [TestMethod]
    public void FallBackDayIncludesBothOccurrencesOfAnAmbiguousHour()
    {
        var start = new DateTimeOffset(2025, 11, 2, 0, 0, 0, TimeSpan.FromHours(-4));
        var end = new DateTimeOffset(2025, 11, 3, 0, 0, 0, TimeSpan.FromHours(-5));
        var summary = TrendCalculator.Summarize(
            new[]
            {
                Entry(new DateTimeOffset(2025, 11, 2, 1, 30, 0, TimeSpan.FromHours(-4)), Mood.VeryLow),
                Entry(new DateTimeOffset(2025, 11, 2, 1, 30, 0, TimeSpan.FromHours(-5)), Mood.Great)
            },
            start, end, DstZone);

        Assert.AreEqual(TimeSpan.FromHours(25), end - start);
        Assert.HasCount(1, summary.Days);
        Assert.AreEqual(new DailyMood(new DateOnly(2025, 11, 2), 2, 3), summary.Days[0]);
    }

    [TestMethod]
    public void NonMidnightRangeAcrossDstAndMidnightKeepsPartialEndpointDays()
    {
        var start = new DateTimeOffset(2025, 3, 8, 23, 30, 0, TimeSpan.FromHours(-5));
        var end = new DateTimeOffset(2025, 3, 10, 0, 30, 0, TimeSpan.FromHours(-4));
        var summary = TrendCalculator.Summarize(
            new[]
            {
                Entry(start, Mood.Low),
                Entry(new DateTimeOffset(2025, 3, 9, 0, 0, 0, TimeSpan.FromHours(-5)), Mood.Neutral),
                Entry(new DateTimeOffset(2025, 3, 9, 3, 0, 0, TimeSpan.FromHours(-4)), Mood.Great),
                Entry(end.AddMinutes(-1), Mood.Good)
            },
            start, end, DstZone);

        CollectionAssert.AreEqual(
            new[]
            {
                new DailyMood(new DateOnly(2025, 3, 8), 1, 2),
                new DailyMood(new DateOnly(2025, 3, 9), 2, 4),
                new DailyMood(new DateOnly(2025, 3, 10), 1, 4)
            },
            summary.Days.ToArray());
    }

    [TestMethod]
    public void HourRangeCrossingSpringForwardStillHasOneDateBin()
    {
        var start = new DateTimeOffset(2025, 3, 9, 1, 30, 0, TimeSpan.FromHours(-5));
        var end = new DateTimeOffset(2025, 3, 9, 3, 30, 0, TimeSpan.FromHours(-4));

        var summary = TrendCalculator.Summarize(
            new[] { Entry(start, Mood.Neutral), Entry(end.AddTicks(-1), Mood.Great) },
            start, end, DstZone);

        Assert.AreEqual(TimeSpan.FromHours(1), end - start);
        Assert.HasCount(1, summary.Days);
        Assert.AreEqual(new DailyMood(new DateOnly(2025, 3, 9), 2, 4), summary.Days[0]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MidnightFallbackIncludesAnIntermediateDateEvenWhenEndpointsShareADate(bool withEntry)
    {
        var zone = CreateMidnightFallbackZone();
        var start = new DateTimeOffset(2025, 11, 1, 23, 45, 0, TimeSpan.FromHours(-4));
        var end = new DateTimeOffset(2025, 11, 1, 23, 50, 0, TimeSpan.FromHours(-5));
        var entries = withEntry
            ? new[] { Entry(new DateTimeOffset(2025, 11, 2, 0, 15, 0, TimeSpan.FromHours(-4)), Mood.Good) }
            : Array.Empty<PulseEntry>();

        var summary = TrendCalculator.Summarize(entries, start, end, zone);

        CollectionAssert.AreEqual(
            new[]
            {
                new DailyMood(new DateOnly(2025, 11, 1), 0, null),
                new DailyMood(new DateOnly(2025, 11, 2), withEntry ? 1 : 0, withEntry ? 4 : null)
            },
            summary.Days.ToArray());
    }

    [TestMethod]
    public void MidnightFallbackCanReverseEndpointDatesWithoutReversingTheInstantInterval()
    {
        var zone = CreateMidnightFallbackZone();
        var start = new DateTimeOffset(2025, 11, 2, 0, 15, 0, TimeSpan.FromHours(-4));
        var end = new DateTimeOffset(2025, 11, 1, 23, 45, 0, TimeSpan.FromHours(-5));

        var summary = TrendCalculator.Summarize(
            new[] { Entry(start, Mood.Great), Entry(end.AddTicks(-1), Mood.Low) },
            start, end, zone);

        CollectionAssert.AreEqual(
            new[]
            {
                new DailyMood(new DateOnly(2025, 11, 1), 1, 2),
                new DailyMood(new DateOnly(2025, 11, 2), 1, 5)
            },
            summary.Days.ToArray());
    }

    [TestMethod]
    public void ASubHourNonMidnightIntervalProducesOneBin()
    {
        var start = Start.AddHours(10).AddMinutes(15);
        var summary = TrendCalculator.Summarize(
            new[] { Entry(start.AddMinutes(1), Mood.Good) },
            start, start.AddMinutes(30), TimeZoneInfo.Utc);

        Assert.HasCount(1, summary.Days);
        Assert.AreEqual(new DailyMood(new DateOnly(2026, 1, 1), 1, 4), summary.Days[0]);
    }

    [TestMethod]
    public void OneHourCrossingMidnightProducesTwoPartialBins()
    {
        var start = Start.AddHours(23).AddMinutes(30);
        var summary = TrendCalculator.Summarize(
            new[] { Entry(start, Mood.Low), Entry(Start.AddDays(1), Mood.Good) },
            start, start.AddHours(1), TimeZoneInfo.Utc);

        CollectionAssert.AreEqual(
            new[]
            {
                new DailyMood(new DateOnly(2026, 1, 1), 1, 2),
                new DailyMood(new DateOnly(2026, 1, 2), 1, 4)
            },
            summary.Days.ToArray());
    }

    [TestMethod]
    public void AnExclusiveMidnightEndpointDoesNotAddAnEmptyNextDay()
    {
        var summary = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), Start.AddHours(23), Start.AddDays(1), TimeZoneInfo.Utc);

        Assert.HasCount(1, summary.Days);
        Assert.AreEqual(new DateOnly(2026, 1, 1), summary.Days[0].Date);
    }

    [TestMethod]
    public void OneTickIntervalsOnEitherSideOfMidnightUseTheCorrectDate()
    {
        var before = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), Start.AddTicks(-1), Start, TimeZoneInfo.Utc);
        var after = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), Start, Start.AddTicks(1), TimeZoneInfo.Utc);

        Assert.HasCount(1, before.Days);
        Assert.AreEqual(new DateOnly(2025, 12, 31), before.Days[0].Date);
        Assert.HasCount(1, after.Days);
        Assert.AreEqual(new DateOnly(2026, 1, 1), after.Days[0].Date);
    }

    [TestMethod]
    public void UnsortedEntriesProduceChronologicalBinsIncludingGaps()
    {
        var summary = TrendCalculator.Summarize(
            new[]
            {
                Entry(Start.AddDays(3), Mood.Great),
                Entry(Start.AddDays(1).AddHours(1), Mood.Low),
                Entry(Start, Mood.VeryLow)
            },
            Start, Start.AddDays(4), TimeZoneInfo.Utc);

        CollectionAssert.AreEqual(
            new[]
            {
                new DailyMood(new DateOnly(2026, 1, 1), 1, 1),
                new DailyMood(new DateOnly(2026, 1, 2), 1, 2),
                new DailyMood(new DateOnly(2026, 1, 3), 0, null),
                new DailyMood(new DateOnly(2026, 1, 4), 1, 5)
            },
            summary.Days.ToArray());
    }

    [TestMethod]
    public void InputIsEnumeratedExactlyOnce()
    {
        var entries = new SinglePassEntries(new[] { Entry(Start, Mood.Neutral) });

        var summary = TrendCalculator.Summarize(entries, Start, Start.AddDays(1), TimeZoneInfo.Utc);

        Assert.AreEqual(1, entries.EnumerationCount);
        Assert.AreEqual(1, summary.Count);
    }

    [TestMethod]
    public void NullEntriesAreRejected()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() =>
            TrendCalculator.Summarize(null!, Start, Start.AddDays(1), TimeZoneInfo.Utc));

        Assert.AreEqual("entries", error.ParamName);
    }

    [TestMethod]
    public void NullZoneIsRejected()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() =>
            TrendCalculator.Summarize(Array.Empty<PulseEntry>(), Start, Start.AddDays(1), null!));

        Assert.AreEqual("zone", error.ParamName);
    }

    [TestMethod]
    public void NullElementsAreRejectedInsteadOfSilentlyIgnored()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() =>
            TrendCalculator.Summarize(new PulseEntry[] { null! }, Start, Start.AddDays(1), TimeZoneInfo.Utc));

        Assert.AreEqual("entries", error.ParamName);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void EmptyOrReversedIntervalsAreRejectedBeforeEnumeration(int endDays)
    {
        var entries = new SinglePassEntries(Array.Empty<PulseEntry>());

        var error = Assert.ThrowsExactly<ArgumentException>(() =>
            TrendCalculator.Summarize(entries, Start, Start.AddDays(endDays), TimeZoneInfo.Utc));

        Assert.AreEqual("endExclusive", error.ParamName);
        Assert.AreEqual(0, entries.EnumerationCount);
    }

    [TestMethod]
    public void DifferentlyOffsetButEqualInstantsDoNotFormAValidInterval()
    {
        Assert.ThrowsExactly<ArgumentException>(() => TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), Start, Start.ToOffset(TimeSpan.FromHours(5)), TimeZoneInfo.Utc));
    }

    [TestMethod]
    public void MaximumRangeOf3660DaysIsAccepted()
    {
        var summary = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), Start, Start.AddDays(3660), TimeZoneInfo.Utc);

        Assert.HasCount(3660, summary.Days);
        Assert.AreEqual(DateOnly.FromDateTime(Start.DateTime), summary.Days[0].Date);
        Assert.AreEqual(DateOnly.FromDateTime(Start.AddDays(3659).DateTime), summary.Days[^1].Date);
    }

    [TestMethod]
    public void RangeLongerThan3660DaysIsRejectedBeforeEnumeration()
    {
        var entries = new SinglePassEntries(Array.Empty<PulseEntry>());

        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TrendCalculator.Summarize(entries, Start, Start.AddDays(3660).AddTicks(1), TimeZoneInfo.Utc));

        Assert.AreEqual("endExclusive", error.ParamName);
        Assert.AreEqual(0, entries.EnumerationCount);
    }

    [TestMethod]
    public void EnormousRangeIsRejectedWithoutAllocatingDailyBins()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), DateTimeOffset.MinValue, DateTimeOffset.MaxValue, TimeZoneInfo.Utc));
    }

    [TestMethod]
    public void ExtremeDatesCanBeUsedForSmallValidRanges()
    {
        var earliest = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), DateTimeOffset.MinValue, DateTimeOffset.MinValue.AddTicks(1), TimeZoneInfo.Utc);
        var latest = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), DateTimeOffset.MaxValue.AddTicks(-1), DateTimeOffset.MaxValue, TimeZoneInfo.Utc);

        Assert.HasCount(1, earliest.Days);
        Assert.AreEqual(DateOnly.MinValue, earliest.Days[0].Date);
        Assert.HasCount(1, latest.Days);
        Assert.AreEqual(DateOnly.MaxValue, latest.Days[0].Date);
    }

    [TestMethod]
    public void ExclusiveEndTickIsSubtractedInUtcToAvoidLocalDateUnderflow()
    {
        var end = new DateTimeOffset(DateTime.MinValue, TimeSpan.FromHours(-1));

        var summary = TrendCalculator.Summarize(
            Array.Empty<PulseEntry>(), DateTimeOffset.MinValue, end, TimeZoneInfo.Utc);

        Assert.HasCount(1, summary.Days);
        Assert.AreEqual(DateOnly.MinValue, summary.Days[0].Date);
    }

    [TestMethod]
    public void ReturnedCollectionsAreReadOnly()
    {
        var summary = TrendCalculator.Summarize(
            new[] { Entry(Start, Mood.Good) }, Start, Start.AddDays(1), TimeZoneInfo.Utc);

        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<MoodCount>)summary.Distribution)[0] = new MoodCount(Mood.VeryLow, 99));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<DailyMood>)summary.Days).Clear());
    }

    private static PulseEntry Entry(DateTimeOffset at, Mood mood, int? stress = null, int? energy = null) =>
        new(Guid.NewGuid(), at, "Synthetic/StoredZone", mood, stress, energy, Array.Empty<ContextFactor>());

    private static TimeZoneInfo CreateDstZone()
    {
        var spring = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var fall = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1), new DateTime(2035, 12, 31), TimeSpan.FromHours(1), spring, fall);

        return TimeZoneInfo.CreateCustomTimeZone(
            "SyntheticEastern", TimeSpan.FromHours(-5), "Synthetic Eastern", "Synthetic Standard",
            "Synthetic Daylight", new[] { rule });
    }

    private static TimeZoneInfo CreateMidnightFallbackZone()
    {
        var spring = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var fall = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 0, 30, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1), new DateTime(2035, 12, 31), TimeSpan.FromHours(1), spring, fall);

        return TimeZoneInfo.CreateCustomTimeZone(
            "SyntheticMidnightFallback", TimeSpan.FromHours(-5), "Synthetic Midnight Fallback",
            "Synthetic Standard", "Synthetic Daylight", new[] { rule });
    }

    private sealed class SinglePassEntries(IEnumerable<PulseEntry> entries) : IEnumerable<PulseEntry>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<PulseEntry> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The source can only be enumerated once.");
            }

            return entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
