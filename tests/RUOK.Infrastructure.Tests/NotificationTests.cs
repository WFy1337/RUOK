using System.Text.Json;
using System.Xml.Linq;
using RUOK.Application;
using RUOK.Domain;

namespace RUOK.Infrastructure.Tests;

[TestClass]
public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RUOK.Tests", Guid.NewGuid().ToString("N"));
    private SqliteWellbeingStore Store() => new(Path.Combine(_directory, "test.db"), new DpapiDataProtector());
    private static AppSettings Enabled => new()
    {
        PrivacyAccepted = true,
        Notifications = new NotificationPreferences { Mode = ReminderMode.Timed },
        NextReminderUtc = Now
    };
    private static NotificationIntent Intent(Mood mood = Mood.Good) =>
        new(Guid.NewGuid(), Now, NotificationAction.Mood, mood, false);
    private static Dictionary<string, string> Arguments(string input) =>
        input.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void OldSettingsReceiveSafeNotificationDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"Appearance":1,"RetentionDays":90,"PrivacyAccepted":true,"ReducedMotion":true}""")!;
        settings.Validate();
        Assert.AreEqual(ReminderMode.Disabled, settings.Notifications.Mode);
        Assert.AreEqual(NotificationFaceAction.SaveMood, settings.Notifications.FaceAction);
        Assert.IsNull(settings.NextReminderUtc);
        Assert.AreEqual(90, settings.RetentionDays);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(14)]
    [DataRow(481)]
    public void InvalidIntervalsAreRejected(int interval) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationPreferences { IntervalMinutes = interval }.Validate());

    [TestMethod]
    public void InvalidEnumsAndEmptyWindowsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationPreferences { Mode = (ReminderMode)99 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationPreferences { FaceAction = (NotificationFaceAction)99 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationPreferences { Layout = (NotificationLayout)99 }.Validate());
        Assert.Throws<ArgumentException>(() => new NotificationPreferences { WindowEnd = new TimeOnly(9, 0) }.Validate());
    }

    [TestMethod]
    public void DueRemindersRequireConsentScheduleWindowAndNoRecentCheckIn()
    {
        Assert.IsTrue(ReminderSchedule.IsDue(Enabled, Now, null, TimeZoneInfo.Utc));
        Assert.IsFalse(ReminderSchedule.IsDue(new AppSettings(), Now, null, TimeZoneInfo.Utc));
        Assert.IsFalse(ReminderSchedule.IsDue(Enabled with { PrivacyAccepted = false }, Now, null, TimeZoneInfo.Utc));
        Assert.IsFalse(ReminderSchedule.IsDue(Enabled with { NextReminderUtc = null }, Now, null, TimeZoneInfo.Utc));
        Assert.IsFalse(ReminderSchedule.IsDue(Enabled, Now.AddTicks(-1), null, TimeZoneInfo.Utc));
        Assert.IsFalse(ReminderSchedule.IsDue(Enabled, Now, Now.AddMinutes(-89), TimeZoneInfo.Utc));
        Assert.IsTrue(ReminderSchedule.IsDue(Enabled, Now, Now.AddMinutes(-90), TimeZoneInfo.Utc));
    }

    [TestMethod]
    [DataRow("2026-09-09T08:59:59Z", false)]
    [DataRow("2026-09-09T09:00:00Z", true)]
    [DataRow("2026-09-09T16:59:59Z", true)]
    [DataRow("2026-09-09T17:00:00Z", false)]
    [DataRow("2026-09-12T10:00:00Z", false)]
    public void WindowBoundariesAndWeekdaysAreRespected(string instant, bool expected) =>
        Assert.AreEqual(expected, ReminderSchedule.IsWithinWindow(new NotificationPreferences(),
            DateTimeOffset.Parse(instant), TimeZoneInfo.Utc));

    [TestMethod]
    [DataRow("2026-09-11T23:00:00Z", true)]
    [DataRow("2026-09-12T01:59:59Z", true)]
    [DataRow("2026-09-12T02:00:00Z", false)]
    [DataRow("2026-09-12T23:00:00Z", false)]
    [DataRow("2026-09-14T01:00:00Z", false)]
    public void OvernightWindowsBelongToTheirStartingDay(string instant, bool expected)
    {
        var preferences = new NotificationPreferences { WindowStart = new TimeOnly(22, 0), WindowEnd = new TimeOnly(2, 0) };
        Assert.AreEqual(expected, ReminderSchedule.IsWithinWindow(preferences, DateTimeOffset.Parse(instant), TimeZoneInfo.Utc));
    }

    [TestMethod]
    public void WindowsUseCurrentLocalZoneAndOptionalWeekends()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test UTC+2", TimeSpan.FromHours(2), "Test", "Test");
        Assert.IsTrue(ReminderSchedule.IsWithinWindow(new NotificationPreferences(), Now.AddHours(-3), zone));
        Assert.IsFalse(ReminderSchedule.IsWithinWindow(new NotificationPreferences(), Now.AddHours(-3), TimeZoneInfo.Utc));
        Assert.IsTrue(ReminderSchedule.IsWithinWindow(new NotificationPreferences { WeekdaysOnly = false }, Now.AddDays(3), zone));
    }

    [TestMethod]
    public void IdleGateWaitsForReturnAndUsesMonotonicGrace()
    {
        var clock = new TestClock();
        var gate = new ActivityReminderGate(clock);
        Assert.IsTrue(gate.CanNotify(TimeSpan.Zero));
        Assert.IsFalse(gate.CanNotify(TimeSpan.FromMinutes(5)));
        Assert.IsFalse(gate.CanNotify(TimeSpan.Zero));
        clock.Utc = clock.Utc.AddDays(1);
        Assert.IsFalse(gate.CanNotify(TimeSpan.Zero));
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.IsFalse(gate.CanNotify(TimeSpan.Zero));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(gate.CanNotify(TimeSpan.Zero));
        Assert.IsFalse(gate.CanNotify(TimeSpan.FromMinutes(6)));
        gate.Reset();
        Assert.IsTrue(gate.CanNotify(TimeSpan.Zero));
    }

    [TestMethod]
    public void LongSamplingGapAfterSleepRestartsReturnGrace()
    {
        var clock = new TestClock();
        var gate = new ActivityReminderGate(clock);
        Assert.IsTrue(gate.CanNotify(TimeSpan.Zero));
        clock.Advance(TimeSpan.FromHours(2));
        Assert.IsFalse(gate.CanNotify(TimeSpan.Zero));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.IsTrue(gate.CanNotify(TimeSpan.Zero));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void EveryFaceRoundTripsWithoutCultureDependence(int score)
    {
        var original = Intent((Mood)score);
        Assert.IsTrue(NotificationIntent.TryParse(Arguments(original.ToArguments()), out var parsed));
        Assert.AreEqual(original, parsed);
        Assert.IsTrue(NotificationIntent.TryParse(original.ToArguments(), out var rawParsed));
        Assert.AreEqual(original, rawParsed);
    }

    [TestMethod]
    [DataRow("v", "2")]
    [DataRow("id", "not-a-guid")]
    [DataRow("created", "-1")]
    [DataRow("created", "253402300800")]
    [DataRow("mood", "0")]
    [DataRow("mood", "6")]
    [DataRow("mood", "1.0")]
    [DataRow("action", "delete")]
    [DataRow("test", "true")]
    public void MalformedArgumentsCannotProduceAnAction(string key, string value)
    {
        var arguments = Arguments(Intent().ToArguments());
        arguments[key] = value;
        Assert.IsFalse(NotificationIntent.TryParse(arguments, out var parsed));
        Assert.IsNull(parsed);
    }

    [TestMethod]
    public void MissingAndDuplicateArgumentsAreRejected()
    {
        Assert.IsFalse(NotificationIntent.TryParse(Array.Empty<KeyValuePair<string, string>>(), out _));
        Assert.IsFalse(NotificationIntent.TryParse("", out _));
        Assert.IsFalse(NotificationIntent.TryParse("missing-equals", out _));
        var values = Arguments(Intent().ToArguments()).ToList();
        values.Add(values[0]);
        Assert.IsFalse(NotificationIntent.TryParse(values, out _));
    }

    [TestMethod]
    public void OutcomeHonorsSavedPreferenceConsentExpiryAndTestSafety()
    {
        var intent = Intent();
        Assert.AreEqual(NotificationOutcome.Save, intent.Evaluate(Enabled, Now));
        var review = Enabled with { Notifications = Enabled.Notifications with { FaceAction = NotificationFaceAction.OpenPulseCheck } };
        Assert.AreEqual(NotificationOutcome.Open, intent.Evaluate(review, Now));
        Assert.AreEqual(NotificationOutcome.NeedsConsent, intent.Evaluate(Enabled with { PrivacyAccepted = false }, Now));
        Assert.AreEqual(NotificationOutcome.Disabled, intent.Evaluate(new AppSettings { PrivacyAccepted = true }, Now));
        Assert.AreEqual(NotificationOutcome.TestPreview, (intent with { IsTest = true }).Evaluate(new AppSettings(), Now));
        Assert.AreEqual(NotificationOutcome.Skip, (intent with { Action = NotificationAction.Skip }).Evaluate(Enabled, Now));
        Assert.AreEqual(NotificationOutcome.Expired, intent.Evaluate(Enabled, Now.AddHours(24).AddSeconds(1)));
        Assert.AreEqual(NotificationOutcome.Expired, (intent with { CreatedAt = Now.AddMinutes(6) }).Evaluate(Enabled, Now));
        Assert.AreEqual(NotificationOutcome.Open, (intent with { Action = NotificationAction.Open, Mood = null }).Evaluate(Enabled, Now));
    }

    [TestMethod]
    public void NativeFacePayloadHasExactlyFiveIconOnlyAccessibleActions()
    {
        var xml = XDocument.Parse(NotificationPayload.Create(Intent(), NotificationLayout.Faces,
            new NotificationText("RUOK", "A & B < C", "Take survey", "Skip", ["Very low", "Low", "Neutral", "Good", "Great"])));
        var actions = xml.Descendants("action").ToArray();
        Assert.HasCount(5, actions);
        Assert.AreEqual("A & B < C", xml.Descendants("text").Last().Value);
        for (var i = 0; i < 5; i++)
        {
            Assert.AreEqual("", actions[i].Attribute("content")!.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(actions[i].Attribute("hint-toolTip")!.Value));
            StringAssert.EndsWith(actions[i].Attribute("imageUri")!.Value, $"Mood{i + 1}.png");
            Assert.IsTrue(NotificationIntent.TryParse(Arguments(actions[i].Attribute("arguments")!.Value), out var intent));
            Assert.AreEqual((Mood)(i + 1), intent!.Mood);
        }
    }

    [TestMethod]
    public void FallbackHasExactlySurveyAndSkipAndNoNeutralResponse()
    {
        var xml = XDocument.Parse(NotificationPayload.Create(Intent(), NotificationLayout.SurveyAndSkip,
            new NotificationText("RUOK", "Optional", "Take the survey", "Skip for now", ["1", "2", "3", "4", "5"])));
        var actions = xml.Descendants("action").ToArray();
        Assert.HasCount(2, actions);
        Assert.AreEqual("Take the survey", actions[0].Attribute("content")!.Value);
        Assert.AreEqual("Skip for now", actions[1].Attribute("content")!.Value);
        Assert.IsTrue(NotificationIntent.TryParse(Arguments(actions[1].Attribute("arguments")!.Value), out var skip));
        Assert.AreEqual(NotificationAction.Skip, skip!.Action);
        Assert.IsNull(skip.Mood);
    }

    [TestMethod]
    public void StandaloneFacesUseEscapedLocalFilesWithoutChangingTheirActions()
    {
        var images = Enumerable.Range(1, 5).Select(score =>
            new Uri($@"C:\RUOK & tools\Assets\NotificationFaces\Mood{score}.png").AbsoluteUri).ToArray();
        var xml = XDocument.Parse(NotificationPayload.Create(Intent(), NotificationLayout.Faces,
            new NotificationText("RUOK", "Optional", "Survey", "Skip", ["1", "2", "3", "4", "5"]), images));
        var actions = xml.Descendants("action").ToArray();
        Assert.HasCount(5, actions);
        for (var i = 0; i < 5; i++)
        {
            Assert.AreEqual(images[i], actions[i].Attribute("imageUri")!.Value);
            Assert.AreEqual("", actions[i].Attribute("content")!.Value);
            Assert.IsTrue(NotificationIntent.TryParse(actions[i].Attribute("arguments")!.Value, out var intent));
            Assert.AreEqual(NotificationAction.Mood, intent!.Action);
            Assert.AreEqual((Mood)(i + 1), intent.Mood);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Assets/Mood1.png")]
    [DataRow("https://example.invalid/Mood1.png")]
    [DataRow("file://server/share/Mood1.png")]
    public void StandaloneImagesRejectNonlocalOrRelativeUris(string image)
    {
        Assert.Throws<ArgumentException>(() => NotificationPayload.Create(Intent(), NotificationLayout.Faces,
            new NotificationText("RUOK", "Optional", "Survey", "Skip", ["1", "2", "3", "4", "5"]),
            Enumerable.Repeat(image, 5).ToArray()));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(6)]
    public void StandaloneImagesRequireExactlyFiveFaces(int count)
    {
        Assert.Throws<ArgumentException>(() => NotificationPayload.Create(Intent(), NotificationLayout.Faces,
            new NotificationText("RUOK", "Optional", "Survey", "Skip", ["1", "2", "3", "4", "5"]),
            Enumerable.Repeat("file:///C:/RUOK/Mood1.png", count).ToArray()));
    }

    [TestMethod]
    public async Task NotificationSaveIsIdempotentAcrossServiceAndStoreInstances()
    {
        var store = Store();
        await store.InitializeAsync();
        await store.SaveSettingsAsync(Enabled);
        var first = new WellbeingService(store, new TestClock(), TimeZoneInfo.Utc);
        var intent = Intent(Mood.Low);
        Assert.IsTrue(await first.SaveNotificationPulseAsync(intent));
        var second = new WellbeingService(Store(), new TestClock(), TimeZoneInfo.Utc);
        Assert.IsFalse(await second.SaveNotificationPulseAsync(intent with { Mood = Mood.Great }));
        var record = (await store.ReadEntriesAsync()).Single();
        Assert.AreEqual(intent.Id, record.Id);
        Assert.AreEqual(Mood.Low, record.Mood);
        Assert.AreEqual(Now, record.RecordedAt);
        Assert.IsNull(record.Stress);
        Assert.IsNull(record.Energy);
        Assert.IsEmpty(record.Factors);
    }

    [TestMethod]
    public async Task StoreBoundaryRejectsTestSkipExpiredDisabledUnconsentedAndReviewSaves()
    {
        var store = Store();
        var service = new WellbeingService(store, new TestClock(), TimeZoneInfo.Utc);
        await service.InitializeAsync();
        var intent = Intent();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNotificationPulseAsync(intent));
        await store.SaveSettingsAsync(new AppSettings { PrivacyAccepted = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNotificationPulseAsync(intent));
        await store.SaveSettingsAsync(Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNotificationPulseAsync(intent with { IsTest = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNotificationPulseAsync(intent with { Action = NotificationAction.Skip }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNotificationPulseAsync(intent with { CreatedAt = Now.AddDays(-2) }));
        await store.SaveSettingsAsync(Enabled with { Notifications = Enabled.Notifications with { FaceAction = NotificationFaceAction.OpenPulseCheck } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNotificationPulseAsync(intent));
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task ReminderSettingsPersistWithoutChangingConsentRetentionOrAppearance()
    {
        var store = Store();
        var service = new WellbeingService(store, new TestClock(), TimeZoneInfo.Utc);
        await service.InitializeAsync();
        var original = new AppSettings { Appearance = AppearanceMode.Dark, RetentionDays = 90, ReducedMotion = true };
        await store.SaveSettingsAsync(original);
        var preferences = new NotificationPreferences
        {
            Mode = ReminderMode.ActivityAware, IntervalMinutes = 45,
            FaceAction = NotificationFaceAction.OpenPulseCheck, Layout = NotificationLayout.SurveyAndSkip
        };
        var saved = await service.SaveReminderPreferencesAsync(preferences);
        Assert.AreEqual(original with { Notifications = preferences, NextReminderUtc = Now.AddMinutes(45) }, saved);
        Assert.AreEqual(saved, await Store().ReadSettingsAsync());
        await service.SavePresentationAsync(AppearanceMode.Light, false);
        Assert.AreEqual(preferences, (await store.ReadSettingsAsync()).Notifications);
        var sent = await service.MarkReminderSentAsync(Now.AddHours(1));
        Assert.AreEqual(Now.AddMinutes(105), sent.NextReminderUtc);
        var disabled = await service.SaveReminderPreferencesAsync(preferences with { Mode = ReminderMode.Disabled });
        Assert.IsNull(disabled.NextReminderUtc);
        Assert.IsFalse(disabled.PrivacyAccepted);
        await service.ResetAsync();
        Assert.AreEqual(new AppSettings(), await store.ReadSettingsAsync());
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = Now;
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) { _ticks += elapsed.Ticks; Utc += elapsed; }
    }
}
