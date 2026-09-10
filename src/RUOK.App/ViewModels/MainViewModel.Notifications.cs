using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUOK.Application;
using RUOK_App.Resources;
using RUOK_App.Services;

namespace RUOK_App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<Guid> _handledNotifications = [];
    private WindowsNotifications _notifications = null!;
    private ActivityReminderGate _activityGate = null!;
    private Choice<ReminderMode> _selectedReminderMode = null!;
    private Choice<NotificationFaceAction> _selectedFaceAction = null!;
    private Choice<NotificationLayout> _selectedNotificationLayout = null!;
    private double _reminderInterval = 90;
    private TimeSpan _reminderStart = TimeSpan.FromHours(9);
    private TimeSpan _reminderEnd = TimeSpan.FromHours(17);
    private bool _reminderWeekdays = true;
    private bool _keepInTray = true;
    private DateTimeOffset _retryNotificationsAfter;
    private string _notificationStatus = "";

    private void InitializeNotifications(WindowsNotifications notifications)
    {
        _notifications = notifications;
        _activityGate = new ActivityReminderGate(_clock);
        ReminderModes = Enum.GetValues<ReminderMode>()
            .Select(value => new Choice<ReminderMode>(value, UiText.Get($"ReminderMode{value}"))).ToArray();
        FaceActions = Enum.GetValues<NotificationFaceAction>()
            .Select(value => new Choice<NotificationFaceAction>(value, UiText.Get($"FaceAction{value}"))).ToArray();
        NotificationLayouts = Enum.GetValues<NotificationLayout>()
            .Select(value => new Choice<NotificationLayout>(value, UiText.Get($"NotificationLayout{value}"))).ToArray();
        _selectedReminderMode = ReminderModes[0];
        _selectedFaceAction = FaceActions[0];
        _selectedNotificationLayout = NotificationLayouts[0];
        SaveRemindersCommand = Register(new AsyncRelayCommand(SaveRemindersAsync, () => CanUseData));
        TestNotificationCommand = Register(new AsyncRelayCommand(TestNotificationAsync, () => CanUseData));
    }

    public IReadOnlyList<Choice<ReminderMode>> ReminderModes { get; private set; } = [];
    public IReadOnlyList<Choice<NotificationFaceAction>> FaceActions { get; private set; } = [];
    public IReadOnlyList<Choice<NotificationLayout>> NotificationLayouts { get; private set; } = [];
    public IAsyncRelayCommand SaveRemindersCommand { get; private set; } = null!;
    public IAsyncRelayCommand TestNotificationCommand { get; private set; } = null!;
    public Choice<ReminderMode> SelectedReminderMode { get => _selectedReminderMode; set { if (value is not null) SetProperty(ref _selectedReminderMode, value); } }
    public Choice<NotificationFaceAction> SelectedFaceAction { get => _selectedFaceAction; set { if (value is not null) SetProperty(ref _selectedFaceAction, value); } }
    public Choice<NotificationLayout> SelectedNotificationLayout { get => _selectedNotificationLayout; set { if (value is not null) SetProperty(ref _selectedNotificationLayout, value); } }
    public double ReminderInterval { get => _reminderInterval; set => SetProperty(ref _reminderInterval, value); }
    public TimeSpan ReminderStart { get => _reminderStart; set => SetProperty(ref _reminderStart, value); }
    public TimeSpan ReminderEnd { get => _reminderEnd; set => SetProperty(ref _reminderEnd, value); }
    public bool ReminderWeekdays { get => _reminderWeekdays; set => SetProperty(ref _reminderWeekdays, value); }
    public bool KeepInTray { get => _keepInTray; set => SetProperty(ref _keepInTray, value); }
    public string NotificationStatus { get => _notificationStatus; private set => SetProperty(ref _notificationStatus, value); }
    public bool ShouldStayInTray => IsReady && _settings.Notifications.Mode != ReminderMode.Disabled && _settings.Notifications.KeepInTray;

    private void ApplyNotificationSettings()
    {
        var preferences = _settings.Notifications;
        SelectedReminderMode = ReminderModes.Single(choice => choice.Value == preferences.Mode);
        SelectedFaceAction = FaceActions.Single(choice => choice.Value == preferences.FaceAction);
        SelectedNotificationLayout = NotificationLayouts.Single(choice => choice.Value == preferences.Layout);
        ReminderInterval = preferences.IntervalMinutes;
        ReminderStart = preferences.WindowStart.ToTimeSpan();
        ReminderEnd = preferences.WindowEnd.ToTimeSpan();
        ReminderWeekdays = preferences.WeekdaysOnly;
        KeepInTray = preferences.KeepInTray;
        RefreshNotificationStatus();
    }

    public void RefreshNotificationStatus() => NotificationStatus = _notifications.Status
        + " " + (_settings.Notifications.Mode == ReminderMode.Disabled ? UiText.Get("ReminderOff")
            : UiText.Format("ReminderScheduleHint", _settings.Notifications.IntervalMinutes,
                _settings.Notifications.WindowStart.ToString("t"), _settings.Notifications.WindowEnd.ToString("t")));

    private Task SaveRemindersAsync() => RunAsync(async token =>
    {
        if (!double.IsFinite(ReminderInterval) || ReminderInterval is < 15 or > 480
            || ReminderInterval != Math.Truncate(ReminderInterval))
            throw new ArgumentException("Use a whole number of minutes.");
        var preferences = new NotificationPreferences
        {
            Mode = SelectedReminderMode.Value,
            IntervalMinutes = checked((int)ReminderInterval),
            WindowStart = TimeOnly.FromTimeSpan(ReminderStart),
            WindowEnd = TimeOnly.FromTimeSpan(ReminderEnd),
            WeekdaysOnly = ReminderWeekdays,
            KeepInTray = KeepInTray,
            FaceAction = SelectedFaceAction.Value,
            Layout = SelectedNotificationLayout.Value
        };
        preferences.Validate();
        _settings = await _service.SaveReminderPreferencesAsync(preferences, token);
        _activityGate.Reset();
        _retryNotificationsAfter = default;
        ApplyNotificationSettings();
        SettingsChanged?.Invoke(_settings);
        await _notifications.ClearAsync();
        ShowStatus(UiText.Get("RemindersSaved"));
    });

    private Task TestNotificationAsync() => RunAsync(_ =>
    {
        // A test exercises native rendering and activation, but can never perform a notification save.
        _notifications.Show(_settings.Notifications, _clock.GetUtcNow(), isTest: true);
        RefreshNotificationStatus();
        ShowStatus(UiText.Get("TestNotificationSent"));
        return Task.CompletedTask;
    });

    public async Task CheckReminderAsync()
    {
        if (!CanUseData || _settings.Notifications.Mode == ReminderMode.Disabled)
            return;
        var now = _clock.GetUtcNow();
        if (now < _retryNotificationsAfter)
            return;
        bool active;
        try
        {
            active = _settings.Notifications.Mode != ReminderMode.ActivityAware
                || _activityGate.CanNotify(WindowsNotifications.GetIdleTime());
        }
        catch (Win32Exception)
        {
            _retryNotificationsAfter = now.AddMinutes(5);
            NotificationStatus = UiText.Get("ActivityUnavailable");
            ShowStatus(NotificationStatus, error: true);
            return;
        }
        var latest = _entries.Count == 0 ? (DateTimeOffset?)null : _entries.Max(entry => entry.RecordedAt);
        if (!active || !ReminderSchedule.IsDue(_settings, now, latest, _zone))
            return;
        await RunAsync(async token =>
        {
            // Back off on delivery/persistence failure instead of repeatedly prompting.
            _retryNotificationsAfter = now.AddMinutes(5);
            _notifications.Show(_settings.Notifications, now, isTest: false);
            _settings = await _service.MarkReminderSentAsync(now, token);
            _retryNotificationsAfter = default;
            RefreshNotificationStatus();
        });
    }

    public async Task<bool> HandleNotificationAsync(NotificationIntent intent)
    {
        var showWindow = false;
        if (_handledNotifications.Contains(intent.Id))
            return false;
        await RunAsync(async token =>
        {
            if (_handledNotifications.Contains(intent.Id))
                return;
            var outcome = intent.Evaluate(_settings, _clock.GetUtcNow());
            if (outcome == NotificationOutcome.Expired)
            {
                ShowStatus(UiText.Get("NotificationExpired"));
                showWindow = true;
            }
            else if (outcome == NotificationOutcome.Skip)
            {
                ShowStatus(UiText.Get(intent.IsTest ? "TestNotificationSkipped" : "Skipped"));
            }
            else if (outcome == NotificationOutcome.TestPreview)
            {
                if (intent.Mood is { } preview)
                    SelectedMood = MoodOptions.Single(option => option.Value == preview);
                NavigationRequested?.Invoke(Screen.PulseCheck);
                ShowStatus(UiText.Get("TestNotificationReceived"));
                showWindow = true;
            }
            else if (outcome == NotificationOutcome.NeedsConsent)
            {
                NavigationRequested?.Invoke(Screen.Settings);
                ShowStatus(UiText.Get("NotificationNeedsConsent"));
                showWindow = true;
            }
            else if (outcome == NotificationOutcome.Disabled)
            {
                ShowStatus(UiText.Get("NotificationDisabled"));
                showWindow = true;
            }
            else if (outcome == NotificationOutcome.Save)
            {
                var added = await _service.SaveNotificationPulseAsync(intent, token);
                ApplySnapshot(await _service.RefreshAsync(token));
                ShowStatus(UiText.Get(added ? "PulseSaved" : "NotificationAlreadySaved"));
            }
            else
            {
                if (intent.Mood is { } moodToSelect)
                    SelectedMood = MoodOptions.Single(option => option.Value == moodToSelect);
                NavigationRequested?.Invoke(Screen.PulseCheck);
                ShowStatus(UiText.Get("NotificationDraft"));
                showWindow = true;
            }
            _handledNotifications.Add(intent.Id);
            await _notifications.RemoveAsync(intent.IsTest);
        });
        return showWindow || StatusSeverity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
    }
}
