using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RUOK.Application;
using RUOK.Domain;
using RUOK_App.Resources;
using RUOK_App.Services;

namespace RUOK_App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly WellbeingService _service;
    private readonly ICsvExporter _exporter;
    private readonly IUserDialogs _dialogs;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<IRelayCommand> _commands = [];
    private IReadOnlyList<PulseEntry> _entries = [];
    private IReadOnlyList<PulseEntry> _visibleEntries = [];
    private AppSettings _settings = new();
    private DateTimeOffset _appliedStart;
    private DateTimeOffset _appliedEnd;
    private HistoryPeriod _appliedPeriod = HistoryPeriod.Week;
    private bool _hasAppliedRange;
    private bool _isBusy;
    private bool _isReady;
    private bool _statusOpen;
    private string _statusMessage = "";
    private InfoBarSeverity _statusSeverity;
    private MoodOption? _selectedMood;
    private Choice<int?>? _selectedStress;
    private Choice<int?>? _selectedEnergy;
    private EntryRow? _selectedEntry;
    private Choice<HistoryPeriod> _selectedPeriod;
    private Choice<AppearanceMode> _selectedAppearance;
    private Choice<int> _selectedRetention;
    private bool _reducedMotion;
    private DateTimeOffset? _startDate;
    private DateTimeOffset? _endDate;
    private bool _changingFactors;
    private string _averageMood = "";
    private string _weeklyCount = "0";
    private string _latestMood = "";
    private string _historyCaption = "";

    public MainViewModel(
        WellbeingService service, ICsvExporter exporter, IUserDialogs dialogs,
        TimeProvider clock, TimeZoneInfo zone, string dataLocation, WindowsNotifications notifications)
    {
        _service = service;
        _exporter = exporter;
        _dialogs = dialogs;
        _clock = clock;
        _zone = zone;
        DataLocation = dataLocation;
        MoodOptions = Enum.GetValues<Mood>().Select(mood => new MoodOption(mood)).ToArray();
        Factors = Enum.GetValues<ContextFactor>().Select(factor => new FactorOption(factor)).ToArray();
        foreach (var factor in Factors)
            factor.PropertyChanged += FactorChanged;
        StressOptions = ScoreChoices("Stress");
        EnergyOptions = ScoreChoices("Energy");
        _selectedStress = StressOptions[0];
        _selectedEnergy = EnergyOptions[0];
        Periods = Enum.GetValues<HistoryPeriod>()
            .Select(period => new Choice<HistoryPeriod>(period, UiText.Get($"Period{period}"))).ToArray();
        Appearances = Enum.GetValues<AppearanceMode>()
            .Select(mode => new Choice<AppearanceMode>(mode, UiText.Get($"Appearance{mode}"))).ToArray();
        Retentions = new[] { 30, 90, 365, 0 }.Select(days => new Choice<int>(
            days, days == 0 ? UiText.Get("KeepUntilDeleted") : UiText.Format("KeepDays", days))).ToArray();
        _selectedPeriod = Periods.Single(period => period.Value == HistoryPeriod.Week);
        _selectedAppearance = Appearances[0];
        _selectedRetention = Retentions.Single(retention => retention.Value == 365);
        _startDate = _clock.GetLocalNow().Date.AddDays(-6);
        _endDate = _clock.GetLocalNow().Date;
        _averageMood = UiText.Get("NoData");
        _latestMood = UiText.Get("NoCheckInYet");

        RefreshCommand = Register(new AsyncRelayCommand(RefreshAsync, () => !IsBusy));
        SavePulseCommand = Register(new AsyncRelayCommand(SavePulseAsync, CanSavePulse));
        AcceptPrivacyCommand = Register(new AsyncRelayCommand(AcceptPrivacyAsync, () => CanUseData && NeedsPrivacyAcceptance));
        SaveSettingsCommand = Register(new AsyncRelayCommand(SaveSettingsAsync, () => CanUseData));
        ChangeAppearanceCommand = Register(new AsyncRelayCommand<Choice<AppearanceMode>>(
            ChangeAppearanceAsync, selection => CanUseData && selection is not null && selection.Value != _settings.Appearance));
        ChangeReducedMotionCommand = Register(new AsyncRelayCommand<bool>(
            ChangeReducedMotionAsync, value => CanUseData && value != _settings.ReducedMotion));
        ExportCommand = Register(new AsyncRelayCommand(ExportAsync, () => CanUseData && _visibleEntries.Count > 0));
        DeleteEntryCommand = Register(new AsyncRelayCommand(DeleteEntryAsync, () => CanUseData && SelectedEntry is not null));
        DeleteRangeCommand = Register(new AsyncRelayCommand(DeleteRangeAsync, () => CanUseData && _visibleEntries.Count > 0));
        ResetCommand = Register(new AsyncRelayCommand(ResetAsync, () => !IsBusy));
        ApplyFilterCommand = Register(new RelayCommand(ApplyFilter, () => CanUseData));
        SkipCommand = Register(new RelayCommand(() =>
        {
            ResetDraft();
            ShowStatus(UiText.Get("Skipped"));
        }, () => !IsBusy));
        OpenPulseCommand = new RelayCommand(() => NavigationRequested?.Invoke(Screen.PulseCheck));
        OpenBreathingCommand = new RelayCommand(() => NavigationRequested?.Invoke(Screen.Breathing));
        OpenPrivacyCommand = new RelayCommand(() => NavigationRequested?.Invoke(Screen.Settings));
        InitializeNotifications(notifications);
    }

    public event Action<Screen>? NavigationRequested;
    public event Action<AppSettings>? SettingsChanged;
    public string DataLocation { get; }
    public string TimeZoneCaption => UiText.Format("TimeZoneCaption", _zone.DisplayName);
    public IReadOnlyList<MoodOption> MoodOptions { get; }
    public IReadOnlyList<FactorOption> Factors { get; }
    public IReadOnlyList<Choice<int?>> StressOptions { get; }
    public IReadOnlyList<Choice<int?>> EnergyOptions { get; }
    public IReadOnlyList<Choice<HistoryPeriod>> Periods { get; }
    public IReadOnlyList<Choice<AppearanceMode>> Appearances { get; }
    public IReadOnlyList<Choice<int>> Retentions { get; }
    public ObservableCollection<EntryRow> History { get; } = [];
    public ObservableCollection<DistributionRow> Distribution { get; } = [];
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); NotifyAvailability(); } }
    public bool IsReady { get => _isReady; private set { SetProperty(ref _isReady, value); NotifyAvailability(); } }
    public bool CanUseData => IsReady && !IsBusy && !_lifetime.IsCancellationRequested;
    public bool NeedsPrivacyAcceptance => IsReady && !_settings.PrivacyAccepted;
    public bool IsHistoryEmpty => History.Count == 0;
    public bool IsCustomPeriod => SelectedPeriod.Value == HistoryPeriod.Custom;
    public bool StatusOpen { get => _statusOpen; set => SetProperty(ref _statusOpen, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public InfoBarSeverity StatusSeverity { get => _statusSeverity; private set => SetProperty(ref _statusSeverity, value); }
    public string AverageMood { get => _averageMood; private set => SetProperty(ref _averageMood, value); }
    public string WeeklyCount { get => _weeklyCount; private set => SetProperty(ref _weeklyCount, value); }
    public string LatestMood { get => _latestMood; private set => SetProperty(ref _latestMood, value); }
    public string HistoryCaption { get => _historyCaption; private set => SetProperty(ref _historyCaption, value); }
    public MoodOption? SelectedMood { get => _selectedMood; set { SetProperty(ref _selectedMood, value); SavePulseCommand.NotifyCanExecuteChanged(); } }
    public Choice<int?>? SelectedStress { get => _selectedStress; set => SetProperty(ref _selectedStress, value); }
    public Choice<int?>? SelectedEnergy { get => _selectedEnergy; set => SetProperty(ref _selectedEnergy, value); }
    public EntryRow? SelectedEntry { get => _selectedEntry; set { SetProperty(ref _selectedEntry, value); DeleteEntryCommand.NotifyCanExecuteChanged(); } }
    public Choice<HistoryPeriod> SelectedPeriod
    {
        get => _selectedPeriod;
        set { if (value is not null) { SetProperty(ref _selectedPeriod, value); OnPropertyChanged(nameof(IsCustomPeriod)); } }
    }
    public Choice<AppearanceMode> SelectedAppearance { get => _selectedAppearance; set { if (value is not null) SetProperty(ref _selectedAppearance, value); } }
    public Choice<int> SelectedRetention { get => _selectedRetention; set { if (value is not null) SetProperty(ref _selectedRetention, value); } }
    public bool ReducedMotion { get => _reducedMotion; set => SetProperty(ref _reducedMotion, value); }
    public DateTimeOffset? StartDate { get => _startDate; set => SetProperty(ref _startDate, value); }
    public DateTimeOffset? EndDate { get => _endDate; set => SetProperty(ref _endDate, value); }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SavePulseCommand { get; }
    public IAsyncRelayCommand AcceptPrivacyCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand<Choice<AppearanceMode>> ChangeAppearanceCommand { get; }
    public IAsyncRelayCommand<bool> ChangeReducedMotionCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand DeleteEntryCommand { get; }
    public IAsyncRelayCommand DeleteRangeCommand { get; }
    public IAsyncRelayCommand ResetCommand { get; }
    public IRelayCommand ApplyFilterCommand { get; }
    public IRelayCommand SkipCommand { get; }
    public IRelayCommand OpenPulseCommand { get; }
    public IRelayCommand OpenBreathingCommand { get; }
    public IRelayCommand OpenPrivacyCommand { get; }

    public Task InitializeAsync() => RunAsync(async token =>
    {
        ApplySnapshot(await _service.InitializeAsync(token));
        IsReady = true;
    });

    private Task RefreshAsync() => RunAsync(async token =>
    {
        ApplySnapshot(await _service.InitializeAsync(token));
        IsReady = true;
        ShowStatus(UiText.Get("Refreshed"));
    });

    private Task AcceptPrivacyAsync() => RunAsync(async token =>
    {
        await _service.SaveSettingsAsync(_settings with { PrivacyAccepted = true }, token);
        ApplySnapshot(await _service.RefreshAsync(token));
        ShowStatus(UiText.Get("PrivacyAccepted"));
    });

    private Task SavePulseAsync() => RunAsync(async token =>
    {
        if (SelectedMood is null)
            throw new ArgumentException("Choose a mood before saving.");
        await _service.SavePulseAsync(
            SelectedMood.Value, SelectedStress?.Value, SelectedEnergy?.Value,
            Factors.Where(factor => factor.IsSelected).Select(factor => factor.Value).ToArray(), token);
        ResetDraft();
        ApplySnapshot(await _service.RefreshAsync(token));
        ShowStatus(UiText.Get("PulseSaved"));
    });

    private Task ChangeAppearanceAsync(Choice<AppearanceMode>? selection) => RunAsync(async token =>
    {
        ArgumentNullException.ThrowIfNull(selection);
        await SavePresentationAsync(_settings with { Appearance = selection.Value }, token);
    });

    private Task ChangeReducedMotionAsync(bool reducedMotion) => RunAsync(token =>
        SavePresentationAsync(_settings with { ReducedMotion = reducedMotion }, token));

    private async Task SavePresentationAsync(AppSettings preview, CancellationToken token)
    {
        preview.Validate();
        var saved = false;
        try
        {
            SettingsChanged?.Invoke(preview);
            _settings = await _service.SavePresentationAsync(preview.Appearance, preview.ReducedMotion, token);
            saved = true;
        }
        finally
        {
            if (!saved)
            {
                SelectedAppearance = Appearances.Single(choice => choice.Value == _settings.Appearance);
                ReducedMotion = _settings.ReducedMotion;
                if (!_lifetime.IsCancellationRequested)
                    SettingsChanged?.Invoke(_settings);
            }
        }
    }

    private Task SaveSettingsAsync() => RunAsync(async token =>
    {
        var settings = _settings with { RetentionDays = SelectedRetention.Value };
        var shortensRetention = settings.RetentionDays > 0
            && (_settings.RetentionDays == 0 || settings.RetentionDays < _settings.RetentionDays);
        if (shortensRetention && !await _dialogs.ConfirmAsync(
            UiText.Get("RetentionTitle"), UiText.Format("RetentionConfirm", settings.RetentionDays),
            UiText.Get("ApplyAndDelete"), token))
        {
            ShowStatus(UiText.Get("Cancelled"));
            return;
        }
        await _service.SaveSettingsAsync(settings, token);
        ApplySnapshot(await _service.RefreshAsync(token));
        ShowStatus(UiText.Get("SettingsSaved"));
    });

    private Task ExportAsync() => RunAsync(async token =>
    {
        var snapshot = _visibleEntries.ToArray();
        if (!await _dialogs.ConfirmAsync(UiText.Get("ExportTitle"),
            UiText.Format("ExportConfirm", snapshot.Length), UiText.Get("ChooseFile"), token))
        {
            ShowStatus(UiText.Get("Cancelled"));
            return;
        }
        var path = await _dialogs.ChooseCsvPathAsync(token);
        if (path is null)
        {
            ShowStatus(UiText.Get("Cancelled"));
            return;
        }
        await _exporter.ExportAsync(path, snapshot, token);
        ShowStatus(UiText.Format("Exported", snapshot.Length));
    });

    private Task DeleteEntryAsync() => RunAsync(async token =>
    {
        var selected = SelectedEntry ?? throw new InvalidOperationException("Select an entry first.");
        if (!await _dialogs.ConfirmAsync(UiText.Get("DeleteTitle"),
            UiText.Get("DeleteEntryConfirm"), UiText.Get("Delete"), token))
        {
            ShowStatus(UiText.Get("Cancelled"));
            return;
        }
        await _service.DeleteAsync(selected.Id, token);
        ApplySnapshot(await _service.RefreshAsync(token));
        ShowStatus(UiText.Get("Deleted"));
    });

    private Task DeleteRangeAsync() => RunAsync(async token =>
    {
        if (!await _dialogs.ConfirmAsync(UiText.Get("DeleteRangeTitle"),
            UiText.Format("DeleteRangeConfirm", _visibleEntries.Count, HistoryCaption), UiText.Get("Delete"), token))
        {
            ShowStatus(UiText.Get("Cancelled"));
            return;
        }
        await _service.DeleteRangeAsync(_appliedStart, _appliedEnd, token);
        ApplySnapshot(await _service.RefreshAsync(token));
        ShowStatus(UiText.Get("Deleted"));
    });

    private Task ResetAsync() => RunAsync(async token =>
    {
        if (!await _dialogs.ConfirmAsync(UiText.Get("ResetTitle"),
            UiText.Get("ResetConfirm"), UiText.Get("ResetAll"), token))
        {
            ShowStatus(UiText.Get("Cancelled"));
            return;
        }
        await _service.ResetAsync(token);
        ResetDraft();
        ApplySnapshot(await _service.RefreshAsync(token));
        await _notifications.ClearAsync();
        _activityGate.Reset();
        IsReady = true;
        ShowStatus(UiText.Get("ResetDone"));
    });

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        _settings = snapshot.Settings;
        _entries = snapshot.Entries;
        SelectedAppearance = Appearances.Single(choice => choice.Value == _settings.Appearance);
        SelectedRetention = Retentions.Single(choice => choice.Value == _settings.RetentionDays);
        ReducedMotion = _settings.ReducedMotion;
        ApplyNotificationSettings();
        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);
        var week = LocalDateRange.ToUtc(today.AddDays(-6), today, _zone);
        var summary = TrendCalculator.Summarize(_entries, week.Start, week.End, _zone);
        AverageMood = summary.AverageMood?.ToString("0.0", CultureInfo.CurrentCulture) ?? UiText.Get("NoData");
        WeeklyCount = summary.Count.ToString(CultureInfo.CurrentCulture);
        LatestMood = _entries.Count == 0 ? UiText.Get("NoCheckInYet") : UiText.Get($"Mood{(int)_entries[0].Mood}");
        Distribution.Clear();
        foreach (var bin in summary.Distribution)
            Distribution.Add(new DistributionRow((int)bin.Mood, UiText.Get($"Mood{(int)bin.Mood}"), bin.Count, Math.Max(1, summary.Count)));
        if (!_hasAppliedRange)
            ApplyFilter();
        else
        {
            if (_appliedPeriod is not (HistoryPeriod.Custom or HistoryPeriod.All))
                (_appliedStart, _appliedEnd) = RollingRange(_appliedPeriod);
            PopulateHistory();
        }
        OnPropertyChanged(nameof(NeedsPrivacyAcceptance));
        SettingsChanged?.Invoke(_settings);
        if (snapshot.RemovedByRetention > 0)
            ShowStatus(UiText.Format("RetentionRemoved", snapshot.RemovedByRetention));
    }

    private void ApplyFilter()
    {
        try
        {
            var period = SelectedPeriod.Value;
            var range = period switch
            {
                HistoryPeriod.Day or HistoryPeriod.Week or HistoryPeriod.Month => RollingRange(period),
                HistoryPeriod.All => (Start: DateTimeOffset.MinValue, End: DateTimeOffset.MaxValue),
                HistoryPeriod.Custom when StartDate.HasValue && EndDate.HasValue =>
                    LocalDateRange.ToUtc(DateOnly.FromDateTime(StartDate.Value.DateTime), DateOnly.FromDateTime(EndDate.Value.DateTime), _zone),
                _ => throw new ArgumentException("Choose both dates.")
            };
            _appliedStart = range.Start;
            _appliedEnd = range.End;
            _appliedPeriod = period;
            _hasAppliedRange = true;
            HistoryCaption = period == HistoryPeriod.Custom
                ? UiText.Format("CustomRangeCaption", StartDate!.Value.ToString("d"), EndDate!.Value.ToString("d"))
                : SelectedPeriod.Label;
            PopulateHistory();
        }
        catch (ArgumentException)
        {
            ShowStatus(UiText.Get("InvalidDateRange"), error: true);
        }
    }

    private (DateTimeOffset Start, DateTimeOffset End) RollingRange(HistoryPeriod period)
    {
        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);
        return period switch
        {
            HistoryPeriod.Day => (now.AddHours(-24), now.AddTicks(1)),
            HistoryPeriod.Week => LocalDateRange.ToUtc(today.AddDays(-6), today, _zone),
            HistoryPeriod.Month => LocalDateRange.ToUtc(today.AddDays(-29), today, _zone),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }

    private void PopulateHistory()
    {
        _visibleEntries = _entries.Where(entry => entry.RecordedAt >= _appliedStart && entry.RecordedAt < _appliedEnd).ToArray();
        SelectedEntry = null;
        History.Clear();
        foreach (var entry in _visibleEntries)
            History.Add(new EntryRow(entry, _zone));
        OnPropertyChanged(nameof(IsHistoryEmpty));
        NotifyAvailability();
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        var entered = false;
        try
        {
            await _operationGate.WaitAsync(_lifetime.Token);
            entered = true;
            IsBusy = true;
            await operation(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Window shutdown cancels outstanding work; completed transactions remain committed.
        }
        catch (DataStoreException)
        {
            ShowStatus(UiText.Get("StorageError"), error: true);
        }
        catch (NotificationDeliveryException exception)
        {
            NotificationStatus = exception.Message;
            ShowStatus(exception.Message, error: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            ShowStatus(UiText.Get("FileError"), error: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ShowStatus(UiText.Get("ValidationError"), error: true);
        }
        finally
        {
            if (entered)
            {
                IsBusy = false;
                _operationGate.Release();
            }
        }
    }

    private void ResetDraft()
    {
        SelectedMood = null;
        SelectedStress = StressOptions[0];
        SelectedEnergy = EnergyOptions[0];
        foreach (var factor in Factors)
            factor.IsSelected = false;
    }

    private void FactorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_changingFactors || args.PropertyName != nameof(FactorOption.IsSelected)
            || sender is not FactorOption { IsSelected: true } selected)
            return;
        _changingFactors = true;
        try
        {
            foreach (var factor in Factors)
                if (factor != selected && (selected.Value == ContextFactor.PreferNotToSay || factor.Value == ContextFactor.PreferNotToSay))
                    factor.IsSelected = false;
        }
        finally
        {
            _changingFactors = false;
        }
    }

    private static IReadOnlyList<Choice<int?>> ScoreChoices(string prefix) =>
        new[] { new Choice<int?>(null, UiText.Get("NotRecorded")) }
            .Concat(Enumerable.Range(1, 5).Select(score => new Choice<int?>(score, UiText.Get($"{prefix}{score}")))).ToArray();

    private bool CanSavePulse() => CanUseData && _settings.PrivacyAccepted && SelectedMood is not null;
    private T Register<T>(T command) where T : IRelayCommand { _commands.Add(command); return command; }
    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanUseData));
        OnPropertyChanged(nameof(NeedsPrivacyAcceptance));
        foreach (var command in _commands)
            command.NotifyCanExecuteChanged();
    }

    public void ShowStatus(string message, bool error = false)
    {
        StatusMessage = message;
        StatusSeverity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        StatusOpen = true;
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        foreach (var factor in Factors)
            factor.PropertyChanged -= FactorChanged;
    }
}
