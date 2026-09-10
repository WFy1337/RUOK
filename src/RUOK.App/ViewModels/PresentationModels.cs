using CommunityToolkit.Mvvm.ComponentModel;
using RUOK.Application;
using RUOK.Domain;
using RUOK_App.Resources;

namespace RUOK_App.ViewModels;

public enum Screen
{
    Dashboard,
    PulseCheck,
    History,
    Breathing,
    Settings,
    About
}

public enum HistoryPeriod
{
    Day,
    Week,
    Month,
    All,
    Custom
}

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class MoodOption(Mood value)
{
    public Mood Value { get; } = value;
    public int Score => (int)Value;
    public string Label => UiText.Get($"Mood{Score}");
    public string AccessibleName => UiText.Format("MoodAccessible", Label, Score);
    public override string ToString() => AccessibleName;
}

public sealed class FactorOption(ContextFactor value) : ObservableObject
{
    private bool _isSelected;
    public ContextFactor Value { get; } = value;
    public string Label => UiText.Get($"Factor{Value}");
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class EntryRow(PulseEntry entry, TimeZoneInfo zone)
{
    public Guid Id => entry.Id;
    public int Score => (int)entry.Mood;
    public string MoodLabel => UiText.Get($"Mood{Score}");
    public string Timestamp => TimeZoneInfo.ConvertTime(entry.RecordedAt, zone).ToString("g");
    public string Factors => entry.Factors.Count == 0
        ? UiText.Get("NoContext")
        : string.Join(", ", entry.Factors.Select(factor => UiText.Get($"Factor{factor}")));
    public string Scores => UiText.Format("EntryScores",
        entry.Stress?.ToString() ?? UiText.Get("NotRecorded"),
        entry.Energy?.ToString() ?? UiText.Get("NotRecorded"));
    public string AccessibleName => $"{Timestamp}. {MoodLabel}. {Scores}. {Factors}.";
    public override string ToString() => AccessibleName;
}

public sealed record DistributionRow(int Score, string Label, int Count, int Maximum)
{
    public string AccessibleName => UiText.Format("DistributionAccessible", Label, Count);
}
