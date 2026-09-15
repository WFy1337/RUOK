using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using RUOK.Application;
using RUOK_App.Resources;

namespace RUOK_App.Services;

public sealed class WindowsNotifications
{
    public const string Group = "RUOK.CheckIn";
    public const string EncouragementGroup = "RUOK.Encourage";
    private bool _registered;
    public string? RegistrationError { get; private set; }
    public bool IsRegistered => _registered;

    public void Register()
    {
        try
        {
            AppRuntime.EnsureNotificationResources();
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException)
        {
            RegistrationError = UiText.Format("NotificationRegistrationError", $"0x{error.HResult:X8}");
        }
    }

    public void Unregister()
    {
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
            _registered = false;
        }
    }

    public string Status => RegistrationError ?? (!_registered
        ? UiText.Get("NotificationUnavailable")
        : AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled
            ? UiText.Get("NotificationReady") : UiText.Get("NotificationBlocked"));

    public void Show(NotificationPreferences preferences, DateTimeOffset now, bool isTest)
    {
        EnsureAvailable();
        var layout = preferences.Layout == NotificationLayout.Faces && !AppNotificationButton.IsToolTipSupported()
            ? NotificationLayout.SurveyAndSkip : preferences.Layout;
        var bodyKey = isTest ? "NotificationTestBody"
            : layout == NotificationLayout.SurveyAndSkip ? "NotificationSurveyBody"
            : preferences.FaceAction == NotificationFaceAction.SaveMood ? "NotificationSaveBody" : "NotificationOpenBody";
        var text = new NotificationText(UiText.Get(isTest ? "NotificationTestTitle" : "NotificationTitle"),
            UiText.Get(bodyKey), UiText.Get("TakeSurvey"), UiText.Get("Skip"),
            Enumerable.Range(1, 5).Select(score => UiText.Format("MoodAccessible", UiText.Get($"Mood{score}"), score)).ToArray());
        var intent = new NotificationIntent(Guid.NewGuid(), now, NotificationAction.Open, null, isTest);
        var imageUris = AppRuntime.IsPackaged ? null : Enumerable.Range(1, 5)
            .Select(score => AppRuntime.AssetUri("NotificationFaces", $"Mood{score}.png").AbsoluteUri).ToArray();
        var toast = new AppNotification(NotificationPayload.Create(intent, layout, text, imageUris))
        {
            Group = Group,
            Tag = isTest ? "test" : "reminder",
            Expiration = now.AddHours(24)
        };
        Deliver(toast);
    }

    public void ShowEncouragement(string message, DateTimeOffset now, bool isTest)
    {
        EnsureAvailable();
        var intent = new NotificationIntent(Guid.NewGuid(), now, NotificationAction.Encouragement, null, isTest);
        var toast = new AppNotification(NotificationPayload.CreateEncouragement(intent,
            UiText.Get(isTest ? "EncouragementPreviewTitle" : "EncouragementNotificationTitle"), message))
        {
            Group = EncouragementGroup,
            Tag = isTest ? "test" : "message",
            Expiration = now.AddHours(1)
        };
        Deliver(toast);
    }

    public void EnsureAvailable()
    {
        if (!_registered)
            throw new NotificationDeliveryException(RegistrationError ?? UiText.Get("NotificationUnavailable"));
        if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
            throw new NotificationDeliveryException(UiText.Get("NotificationBlocked"));
    }

    private static void Deliver(AppNotification toast)
    {
        try
        {
            AppNotificationManager.Default.Show(toast);
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or ArgumentException)
        {
            throw new NotificationDeliveryException(UiText.Format("NotificationSendError", $"0x{error.HResult:X8}"), error);
        }
        if (toast.Id == 0)
            throw new NotificationDeliveryException(UiText.Get("NotificationNotAccepted"));
    }

    public async Task ClearAsync()
    {
        if (_registered)
            await AppNotificationManager.Default.RemoveByGroupAsync(Group);
    }

    public async Task RemoveAsync(bool isTest)
    {
        if (_registered)
            await AppNotificationManager.Default.RemoveByTagAndGroupAsync(isTest ? "test" : "reminder", Group);
    }

    public async Task ClearEncouragementsAsync()
    {
        if (_registered)
            await AppNotificationManager.Default.RemoveByGroupAsync(EncouragementGroup);
    }

    public async Task RemoveEncouragementAsync(bool isTest)
    {
        if (_registered)
            await AppNotificationManager.Default.RemoveByTagAndGroupAsync(isTest ? "test" : "message", EncouragementGroup);
    }

    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.Get("ActivityUnavailable"));
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}

public sealed class NotificationDeliveryException : Exception
{
    public NotificationDeliveryException(string message, Exception? inner = null) : base(message, inner) { }
}
