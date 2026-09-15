using CommunityToolkit.Mvvm.Input;
using RUOK.Application;
using RUOK_App.Resources;

namespace RUOK_App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly EncouragementService _encouragementService;
    private EncouragementState _encouragementState = new();
    private bool _encouragementReady;
    private bool _encouragementEnabled;
    private double _encouragementMinimumInterval = 120;
    private string _encouragementMessages = EncouragementPreferences.DefaultMessages;
    private string _encouragementStatus = "";
    private string? _lastEncouragementPreview;
    private DateTimeOffset _retryEncouragementsAfter;

    private void InitializeEncouragements()
    {
        SaveEncouragementsCommand = Register(new AsyncRelayCommand(SaveEncouragementsAsync, () => CanUseEncouragements));
        PreviewEncouragementCommand = Register(new AsyncRelayCommand(PreviewEncouragementAsync, () => CanUseEncouragements));
        RestoreEncouragementMessagesCommand = Register(new RelayCommand(() =>
        {
            EncouragementMessages = EncouragementPreferences.DefaultMessages;
            ShowStatus(UiText.Get("EncouragementDefaultsLoaded"));
        }, () => CanUseEncouragements));
    }

    public IAsyncRelayCommand SaveEncouragementsCommand { get; private set; } = null!;
    public IAsyncRelayCommand PreviewEncouragementCommand { get; private set; } = null!;
    public IRelayCommand RestoreEncouragementMessagesCommand { get; private set; } = null!;
    public bool CanUseEncouragements => CanUseData && _encouragementReady;
    public bool EncouragementEnabled { get => _encouragementEnabled; set => SetProperty(ref _encouragementEnabled, value); }
    public double EncouragementMinimumInterval { get => _encouragementMinimumInterval; set => SetProperty(ref _encouragementMinimumInterval, value); }
    public string EncouragementMessages { get => _encouragementMessages; set => SetProperty(ref _encouragementMessages, value); }
    public string EncouragementStatus { get => _encouragementStatus; private set => SetProperty(ref _encouragementStatus, value); }

    private async Task LoadEncouragementsAsync(CancellationToken token)
    {
        _encouragementReady = false;
        EncouragementStatus = UiText.Get("EncouragementUnavailable");
        _encouragementState = await _encouragementService.ReadAsync(token);
        _encouragementReady = true;
        ApplyEncouragementSettings();
        NotifyAvailability();
        SettingsChanged?.Invoke(_settings);
    }

    private void ApplyEncouragementSettings()
    {
        var preferences = _encouragementState.Preferences;
        EncouragementEnabled = preferences.Enabled;
        EncouragementMinimumInterval = preferences.MinimumIntervalMinutes;
        EncouragementMessages = preferences.Messages;
        RefreshEncouragementStatus();
    }

    private void RefreshEncouragementStatus()
    {
        var preferences = _encouragementState.Preferences;
        EncouragementStatus = preferences.Enabled
            ? UiText.Format("EncouragementScheduleHint", preferences.MinimumIntervalMinutes,
                preferences.MinimumIntervalMinutes * 2)
            : UiText.Get("EncouragementOff");
    }

    private EncouragementPreferences ReadEncouragementEditor()
    {
        if (!double.IsFinite(EncouragementMinimumInterval) || EncouragementMinimumInterval is < 15 or > 240
            || EncouragementMinimumInterval != Math.Truncate(EncouragementMinimumInterval))
            throw new ArgumentException("Use a whole number of minutes between 15 and 240.");
        return new EncouragementPreferences
        {
            Enabled = EncouragementEnabled,
            MinimumIntervalMinutes = checked((int)EncouragementMinimumInterval),
            Messages = EncouragementMessages
        }.Normalize();
    }

    private Task SaveEncouragementsAsync() => RunAsync(async token =>
    {
        _encouragementState = await _encouragementService.SavePreferencesAsync(ReadEncouragementEditor(), token);
        _retryEncouragementsAfter = default;
        ApplyEncouragementSettings();
        SettingsChanged?.Invoke(_settings);
        await _notifications.ClearEncouragementsAsync();
        ShowStatus(UiText.Get("EncouragementSaved"));
    });

    private Task PreviewEncouragementAsync() => RunAsync(_ =>
    {
        var message = _encouragementService.SelectMessage(ReadEncouragementEditor(), _lastEncouragementPreview);
        var now = _clock.GetUtcNow();
        _nextNotificationAllowedUtc = now.AddMinutes(1);
        _notifications.ShowEncouragement(message, now, isTest: true);
        _lastEncouragementPreview = message;
        RefreshNotificationStatus();
        ShowStatus(UiText.Get("EncouragementPreviewSent"));
        return Task.CompletedTask;
    });

    private async Task CheckEncouragementAsync()
    {
        if (!CanUseEncouragements || !_encouragementState.Preferences.Enabled)
            return;
        var now = _clock.GetUtcNow();
        if (now < _retryEncouragementsAfter
            || _encouragementState.NextUtc is { } next
                && (now < next || !ReminderSchedule.IsWithinWindow(_settings.Notifications, now, _zone)))
            return;
        await RunAsync(async token =>
        {
            _retryEncouragementsAfter = now.AddMinutes(5);
            _notifications.EnsureAvailable();
            var prepared = await _encouragementService.PrepareAsync(token);
            _encouragementState = prepared.State;
            if (prepared.Message is { } message)
            {
                token.ThrowIfCancellationRequested();
                var shownAt = _clock.GetUtcNow();
                _nextNotificationAllowedUtc = shownAt.AddMinutes(1);
                _notifications.ShowEncouragement(message, shownAt, isTest: false);
            }
            _retryEncouragementsAfter = default;
            RefreshEncouragementStatus();
        });
    }
}
