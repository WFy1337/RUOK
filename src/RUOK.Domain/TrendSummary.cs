namespace RUOK.Domain;

public record MoodCount(Mood Mood, int Count);

public record DailyMood(DateOnly Date, int Count, double? AverageMood);

public record TrendSummary(
    int Count,
    double? AverageMood,
    double? AverageStress,
    int StressCount,
    double? AverageEnergy,
    int EnergyCount,
    IReadOnlyList<MoodCount> Distribution,
    IReadOnlyList<DailyMood> Days);
