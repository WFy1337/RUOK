using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using RUOK.Application;
using RUOK.Domain;

namespace RUOK.Infrastructure.Tests;

[TestClass]
public sealed class EncouragementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RUOK.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "encouragement.db");
    private readonly TestClock _clock = new();
    private SqliteWellbeingStore Store() => new(DatabasePath, new DpapiDataProtector());
    private EncouragementService Service(SqliteWellbeingStore store, IEncouragementStore? reminders = null,
        Random? random = null, TimeZoneInfo? zone = null) =>
        new(reminders ?? store, store, _clock, zone ?? TimeZoneInfo.Utc, random ?? new MinimumRandom());

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void DefaultsAreOffWithOriginalShortMessagesAndNoSchedule()
    {
        var state = new EncouragementState();
        state.Validate();
        Assert.IsFalse(state.Preferences.Enabled);
        Assert.AreEqual(120, state.Preferences.MinimumIntervalMinutes);
        Assert.HasCount(12, state.Preferences.GetMessages());
        Assert.IsNull(state.NextUtc);
        Assert.IsNull(state.LastMessage);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(14)]
    [DataRow(241)]
    public void InvalidSpacingIsRejected(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EncouragementPreferences { MinimumIntervalMinutes = minutes }.Validate());

    [TestMethod]
    [DataRow(15)]
    [DataRow(120)]
    [DataRow(240)]
    public void SpacingBoundariesAreSupported(int minutes) =>
        new EncouragementPreferences { MinimumIntervalMinutes = minutes }.Validate();

    [TestMethod]
    [DataRow("")]
    [DataRow("  \r\n ")]
    [DataRow("A\u0000B")]
    [DataRow("A\tB")]
    [DataRow("A\u000bB")]
    public void EmptyOrControlCharacterMessagesAreRejected(string messages) =>
        Assert.Throws<ArgumentException>(() => new EncouragementPreferences { Messages = messages }.Validate());

    [TestMethod]
    public void InvalidUnicodeAndNullMessagesAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new EncouragementPreferences { Messages = new string((char)0xd800, 1) }.Validate());
        Assert.Throws<ArgumentNullException>(() => new EncouragementPreferences { Messages = null! }.Validate());
    }

    [TestMethod]
    public void MessageLengthAndCountAreBounded()
    {
        new EncouragementPreferences { Messages = new string('a', 160) }.Validate();
        new EncouragementPreferences { Messages = string.Join("\n", Enumerable.Range(1, 30)) }.Validate();
        Assert.Throws<ArgumentException>(() => new EncouragementPreferences { Messages = new string('a', 161) }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new EncouragementPreferences { Messages = string.Join("\n", Enumerable.Range(1, 31)) }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new EncouragementPreferences { Messages = new string(' ', EncouragementPreferences.MaximumTextLength + 1) }.Validate());
    }

    [TestMethod]
    public void NormalizationTrimsAndDeduplicatesWithoutMutatingInput()
    {
        var original = new EncouragementPreferences { Messages = " First step \r\n\r\nfirst STEP\rSecond step\n" };
        var normalized = original.Normalize();
        Assert.AreEqual("First step\nSecond step", normalized.Messages);
        CollectionAssert.AreEqual(new[] { "First step", "Second step" }, normalized.GetMessages().ToArray());
        Assert.AreEqual(" First step \r\n\r\nfirst STEP\rSecond step\n", original.Messages);
        Assert.HasCount(1, new EncouragementPreferences { Messages = string.Join("\n", Enumerable.Repeat("Keep going", 40)) }.GetMessages());
    }

    [TestMethod]
    public void StateValidationRejectsInconsistentOrUnprotectedMetadata()
    {
        Assert.Throws<ArgumentNullException>(() => new EncouragementState { Preferences = null! }.Validate());
        Assert.Throws<ArgumentException>(() => new EncouragementState { NextUtc = Now }.Validate());
        Assert.Throws<ArgumentException>(() => new EncouragementState { LastMessage = "Small steps count." }.Validate());
        Assert.Throws<ArgumentException>(() => new EncouragementState
        {
            Preferences = new EncouragementPreferences { Enabled = true },
            NextUtc = Now.ToOffset(TimeSpan.FromHours(2))
        }.Validate());
        Assert.Throws<ArgumentException>(() => new EncouragementState
        {
            Preferences = new EncouragementPreferences { Enabled = true },
            LastMessage = "A removed message"
        }.Validate());
    }

    [TestMethod]
    public void MessageSelectionIsRandomAndAvoidsImmediateRepeats()
    {
        var service = Service(Store(), random: new Random(37));
        var preferences = new EncouragementPreferences { Messages = "First\nSecond\nThird\nFourth" };
        var observed = new HashSet<string>();
        string? previous = null;
        for (var i = 0; i < 100; i++)
        {
            var message = service.SelectMessage(preferences, previous);
            Assert.AreNotEqual(previous, message);
            observed.Add(message);
            previous = message;
        }
        CollectionAssert.AreEquivalent(preferences.GetMessages().ToArray(), observed.ToArray());
        Assert.IsFalse(File.Exists(DatabasePath));
    }

    [TestMethod]
    public void AOneMessageListAndRemovedPreviousMessageWork()
    {
        var service = Service(Store());
        var preferences = new EncouragementPreferences { Messages = "One\none\nONE" };
        Assert.AreEqual("One", service.SelectMessage(preferences, "One"));
        Assert.AreEqual("One", service.SelectMessage(preferences, "Deleted"));
    }

    [TestMethod]
    public async Task DisabledAndPreviewPathsCreateNoPreferencesOrCheckIns()
    {
        var store = Store();
        await store.InitializeAsync();
        var service = Service(store);
        Assert.IsNull((await service.PrepareAsync()).Message);
        await service.SavePreferencesAsync(new EncouragementPreferences());
        service.SelectMessage(new EncouragementPreferences { Messages = "An unsaved preview" });
        Assert.AreEqual(0L, await ScalarAsync("SELECT COUNT(*) FROM Preferences;"));
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    [DataRow(15, false)]
    [DataRow(15, true)]
    [DataRow(120, false)]
    [DataRow(240, true)]
    public async Task SavedFirstDelayUsesTheConfiguredRandomBounds(int minutes, bool upper)
    {
        var store = Store();
        await store.InitializeAsync();
        var service = Service(store, random: new BoundaryRandom(upper));
        var saved = await service.SavePreferencesAsync(new EncouragementPreferences
        {
            Enabled = true, MinimumIntervalMinutes = minutes
        });
        Assert.AreEqual(Now.AddMinutes(minutes * (upper ? 2 : 1)), saved.NextUtc);
        Assert.IsNull(saved.LastMessage);
        Assert.IsNull((await service.PrepareAsync()).Message);
    }

    [TestMethod]
    public async Task SavingSamePreferencesDoesNotResetTheScheduleOrRewriteTheRow()
    {
        var store = Store();
        await store.InitializeAsync();
        var service = Service(store);
        var first = await service.SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        var before = await BlobAsync("encouragements");
        _clock.Utc = Now.AddMinutes(20);
        Assert.AreEqual(first, await service.SavePreferencesAsync(first.Preferences));
        CollectionAssert.AreEqual(before, await BlobAsync("encouragements"));
    }

    [TestMethod]
    public async Task DueDeliveryWorksWithPulseCheckOffAndNeverRecordsMood()
    {
        var store = Store();
        await store.InitializeAsync();
        var service = Service(store);
        var saved = await service.SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        _clock.Utc = saved.NextUtc!.Value;
        var delivery = await service.PrepareAsync();
        Assert.IsNotNull(delivery.Message);
        Assert.AreEqual(delivery.Message, delivery.State.LastMessage);
        Assert.AreEqual(_clock.Utc.AddHours(2), delivery.State.NextUtc);
        Assert.AreEqual(new AppSettings(), await store.ReadSettingsAsync());
        Assert.IsEmpty(await store.ReadEntriesAsync());
        Assert.IsNull((await service.PrepareAsync()).Message);
    }

    [TestMethod]
    [DataRow("2026-09-15T08:59:59Z", false)]
    [DataRow("2026-09-15T09:00:00Z", true)]
    [DataRow("2026-09-15T16:59:59Z", true)]
    [DataRow("2026-09-15T17:00:00Z", false)]
    [DataRow("2026-09-19T10:00:00Z", false)]
    public async Task AutomaticDeliveryHonorsSavedHoursAndWeekdays(string time, bool expected)
    {
        var store = Store();
        await store.InitializeAsync();
        var due = new EncouragementState
        {
            Preferences = new EncouragementPreferences { Enabled = true },
            NextUtc = Now.AddDays(-1)
        };
        Assert.IsTrue(await store.TrySaveEncouragementsAsync(new EncouragementState(), due));
        _clock.Utc = DateTimeOffset.Parse(time);
        Assert.AreEqual(expected, (await Service(store).PrepareAsync()).Message is not null);
        if (!expected)
            Assert.AreEqual(due, await store.ReadEncouragementsAsync());
    }

    [TestMethod]
    public async Task OvernightHoursBelongToTheirStartingDayAndLocalZone()
    {
        var store = Store();
        await store.InitializeAsync();
        await store.SaveSettingsAsync(new AppSettings
        {
            Notifications = new NotificationPreferences { WindowStart = new(22, 0), WindowEnd = new(2, 0) }
        });
        var due = new EncouragementState
        {
            Preferences = new EncouragementPreferences { Enabled = true },
            NextUtc = Now
        };
        await store.TrySaveEncouragementsAsync(new EncouragementState(), due);
        _clock.Utc = DateTimeOffset.Parse("2026-09-18T23:00:00Z");
        var zone = TimeZoneInfo.CreateCustomTimeZone("Synthetic UTC+2", TimeSpan.FromHours(2), "Synthetic", "Synthetic");
        Assert.IsNotNull((await Service(store, zone: zone).PrepareAsync()).Message);
        _clock.Utc = DateTimeOffset.Parse("2026-09-19T23:00:00Z");
        Assert.IsNull((await Service(store, zone: zone).PrepareAsync()).Message);
    }

    [TestMethod]
    public async Task RestartAndMissedNativeDeliveryDoNotReplayAttempts()
    {
        var store = Store();
        await store.InitializeAsync();
        var service = Service(store);
        var saved = await service.SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        _clock.Utc = saved.NextUtc!.Value;
        var first = await service.PrepareAsync();
        Assert.IsNotNull(first.Message);
        var restarted = Service(Store());
        Assert.IsNull((await restarted.PrepareAsync()).Message);
        Assert.AreEqual(first.State, await Store().ReadEncouragementsAsync());
        _clock.Utc = first.State.NextUtc!.Value;
        var second = await restarted.PrepareAsync();
        Assert.IsNotNull(second.Message);
        Assert.AreNotEqual(first.Message, second.Message);
    }

    [TestMethod]
    public async Task SleepRecoveryConsidersOnlyOneOverdueMessage()
    {
        var store = Store();
        await store.InitializeAsync();
        await Service(store).SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        _clock.Utc = Now.AddDays(7);
        var restarted = Service(Store());
        var result = await restarted.PrepareAsync();
        Assert.IsNotNull(result.Message);
        Assert.AreEqual(_clock.Utc.AddHours(2), result.State.NextUtc);
        for (var i = 0; i < 10; i++)
            Assert.IsNull((await restarted.PrepareAsync()).Message);
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task MissingNextTimeIsInitializedWithoutSendingAnImmediateMessage()
    {
        var store = Store();
        await store.InitializeAsync();
        var state = new EncouragementState { Preferences = new EncouragementPreferences { Enabled = true } };
        await store.TrySaveEncouragementsAsync(new EncouragementState(), state);
        var result = await Service(store).PrepareAsync();
        Assert.IsNull(result.Message);
        Assert.AreEqual(Now.AddHours(2), result.State.NextUtc);
    }

    [TestMethod]
    public async Task DisablingClearsPendingTimeAndLastMessageAndStopsDelivery()
    {
        var store = Store();
        await store.InitializeAsync();
        var service = Service(store);
        var enabled = await service.SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        _clock.Utc = enabled.NextUtc!.Value;
        await service.PrepareAsync();
        var disabled = await service.SavePreferencesAsync(enabled.Preferences with { Enabled = false });
        Assert.IsNull(disabled.NextUtc);
        Assert.IsNull(disabled.LastMessage);
        _clock.Utc = Now.AddDays(7);
        Assert.IsNull((await Service(Store()).PrepareAsync()).Message);
    }

    [TestMethod]
    public async Task ReplacingMessagesRemovesThePreviousCustomMessage()
    {
        var store = Store();
        await store.InitializeAsync();
        var service = Service(store);
        var saved = await service.SavePreferencesAsync(new EncouragementPreferences { Enabled = true, Messages = "Old one" });
        _clock.Utc = saved.NextUtc!.Value;
        await service.PrepareAsync();
        var changed = await service.SavePreferencesAsync(saved.Preferences with { Messages = "New one" });
        Assert.IsNull(changed.LastMessage);
        Assert.AreEqual("New one", (await Store().ReadEncouragementsAsync()).Preferences.Messages);
    }

    [TestMethod]
    public async Task ProtectedExtensionPreservesLegacySettingsCiphertextEntriesAndSchema()
    {
        var store = Store();
        await store.InitializeAsync();
        var original = new AppSettings
        {
            Appearance = AppearanceMode.Dark, PrivacyAccepted = true, RetentionDays = 90, ReducedMotion = true,
            Notifications = new NotificationPreferences { Mode = ReminderMode.ActivityAware, IntervalMinutes = 45 },
            NextReminderUtc = Now.AddMinutes(45)
        };
        await store.SaveSettingsAsync(original);
        var entry = new PulseEntry(Guid.NewGuid(), Now, "UTC", Mood.Low, null, null, []);
        await store.AddAsync(entry);
        var settingsBefore = await BlobAsync("app");
        var entryBefore = (byte[])(await ScalarAsync("SELECT protected_payload FROM Entries;"))!;
        var state = await Service(store).SavePreferencesAsync(new EncouragementPreferences
        {
            Enabled = true, Messages = "Synthetic private encouragement"
        });
        Assert.AreEqual(state, await Store().ReadEncouragementsAsync());
        Assert.AreEqual(original, await store.ReadSettingsAsync());
        CollectionAssert.AreEqual(settingsBefore, await BlobAsync("app"));
        CollectionAssert.AreEqual(entryBefore, (byte[])(await ScalarAsync("SELECT protected_payload FROM Entries;"))!);
        Assert.AreEqual(1L, await ScalarAsync("PRAGMA user_version;"));
        Assert.AreEqual(2L, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"));
        var protectedValue = await BlobAsync("encouragements");
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(protectedValue).Contains("Synthetic private encouragement", StringComparison.Ordinal));
        var json = JsonSerializer.Serialize(original);
        Assert.IsFalse(json.Contains("Encouragement", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task LegacySettingsSavesAndPresentationChangesPreserveTheExtension()
    {
        var store = Store();
        await store.InitializeAsync();
        var encouragement = await Service(store).SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        var before = await BlobAsync("encouragements");
        var wellbeing = new WellbeingService(store, _clock, TimeZoneInfo.Utc);
        await wellbeing.SaveReminderPreferencesAsync(new NotificationPreferences { Mode = ReminderMode.Timed });
        await wellbeing.SavePresentationAsync(AppearanceMode.Dark, true);
        await wellbeing.SaveSettingsAsync((await store.ReadSettingsAsync()) with { PrivacyAccepted = true, RetentionDays = 30 });
        CollectionAssert.AreEqual(before, await BlobAsync("encouragements"));
        Assert.AreEqual(encouragement, await store.ReadEncouragementsAsync());
    }

    [TestMethod]
    public async Task CompareAndSwapRejectsStaleOrConcurrentState()
    {
        var store = Store();
        await store.InitializeAsync();
        var expected = new EncouragementState();
        var first = expected with { Preferences = expected.Preferences with { Messages = "First" } };
        var second = expected with { Preferences = expected.Preferences with { Messages = "Second" } };
        var results = await Task.WhenAll(store.TrySaveEncouragementsAsync(expected, first),
            Store().TrySaveEncouragementsAsync(expected, second));
        Assert.AreEqual(1, results.Count(result => result));
        var current = await store.ReadEncouragementsAsync();
        Assert.IsTrue(current == first || current == second);
        var before = await BlobAsync("encouragements");
        Assert.IsFalse(await store.TrySaveEncouragementsAsync(expected, expected));
        CollectionAssert.AreEqual(before, await BlobAsync("encouragements"));
    }

    [TestMethod]
    public async Task ConcurrentServicesReserveOnlyOneAutomaticDelivery()
    {
        var store = Store();
        await store.InitializeAsync();
        var first = Service(store);
        var saved = await first.SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        _clock.Utc = saved.NextUtc!.Value;
        var results = await Task.WhenAll(first.PrepareAsync(), Service(Store()).PrepareAsync());
        Assert.AreEqual(1, results.Count(result => result.Message is not null));
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task CancellationAndInvalidInputDoNotChangeTheExtension()
    {
        var store = Store();
        await store.InitializeAsync();
        var saved = await Service(store).SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        var before = await BlobAsync("encouragements");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(store).SavePreferencesAsync(new EncouragementPreferences { Enabled = true, Messages = "" }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Service(store).PrepareAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.TrySaveEncouragementsAsync(saved, saved with { NextUtc = Now.AddHours(3) }, cancellation.Token));
        CollectionAssert.AreEqual(before, await BlobAsync("encouragements"));
    }

    [TestMethod]
    public async Task CorruptExtensionFailsExplicitlyWhileLegacyDataRemainsReadable()
    {
        var store = Store();
        await store.InitializeAsync();
        await store.SaveSettingsAsync(new AppSettings { ReducedMotion = true });
        await Service(store).SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        await ScalarAsync("UPDATE Preferences SET protected_value = x'010203' WHERE key = 'encouragements';");
        await Assert.ThrowsAsync<DataStoreException>(() => Store().ReadEncouragementsAsync());
        Assert.IsTrue((await Store().ReadSettingsAsync()).ReducedMotion);
        Assert.IsEmpty(await store.ReadEntriesAsync());
        Assert.AreEqual(2L, await ScalarAsync("SELECT COUNT(*) FROM Preferences;"));
    }

    [TestMethod]
    public async Task ProtectedPurposePreventsSubstitutingTheAppSettingsRow()
    {
        var store = Store();
        await store.InitializeAsync();
        await store.SaveSettingsAsync(new AppSettings());
        await Service(store).SavePreferencesAsync(new EncouragementPreferences { Enabled = true });
        await ScalarAsync("UPDATE Preferences SET protected_value = (SELECT protected_value FROM Preferences WHERE key='app') WHERE key='encouragements';");
        await Assert.ThrowsAsync<DataStoreException>(() => store.ReadEncouragementsAsync());
        Assert.AreEqual(new AppSettings(), await store.ReadSettingsAsync());
    }

    [TestMethod]
    public async Task FullResetClearsCustomMessagesAndScheduleWithoutChangingSchema()
    {
        var store = Store();
        await store.InitializeAsync();
        await Service(store).SavePreferencesAsync(new EncouragementPreferences { Enabled = true, Messages = "Synthetic custom text" });
        await store.ResetAsync();
        Assert.AreEqual(new EncouragementState(), await store.ReadEncouragementsAsync());
        Assert.AreEqual(new AppSettings(), await store.ReadSettingsAsync());
        Assert.AreEqual(0L, await ScalarAsync("SELECT COUNT(*) FROM Preferences;"));
        Assert.AreEqual(1L, await ScalarAsync("PRAGMA user_version;"));
    }

    [TestMethod]
    public async Task RepeatedConflictsAreBoundedAndSurfaceAnError()
    {
        var store = Store();
        await store.InitializeAsync();
        var conflict = new ConflictingStore(new EncouragementState
        {
            Preferences = new EncouragementPreferences { Enabled = true }, NextUtc = Now
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store, conflict).PrepareAsync());
        Assert.AreEqual(3, conflict.Attempts);
        conflict.Attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store, conflict).SavePreferencesAsync(new EncouragementPreferences { Enabled = false }));
        Assert.AreEqual(3, conflict.Attempts);
    }

    [TestMethod]
    public async Task PersistenceFailureDoesNotReturnADelivery()
    {
        var store = Store();
        await store.InitializeAsync();
        var failing = new FailingStore();
        await Assert.ThrowsAsync<DataStoreException>(() => Service(store, failing).PrepareAsync());
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EncouragementActivationIsReadOnlyForRealAndPreviewNotifications(bool preview)
    {
        var intent = new NotificationIntent(Guid.NewGuid(), Now, NotificationAction.Encouragement, null, preview);
        var payload = XDocument.Parse(NotificationPayload.CreateEncouragement(intent, "RUOK", "You & your pace < everything else."));
        Assert.IsEmpty(payload.Descendants("action"));
        Assert.AreEqual("You & your pace < everything else.", payload.Descendants("text").Last().Value);
        var arguments = payload.Root!.Attribute("launch")!.Value;
        Assert.IsFalse(arguments.Contains("pace", StringComparison.Ordinal));
        Assert.IsTrue(NotificationIntent.TryParse(arguments, out var parsed));
        Assert.AreEqual(intent, parsed);
        Assert.AreEqual(NotificationOutcome.Encouragement, parsed!.Evaluate(new AppSettings(), Now));
        var store = Store();
        await store.InitializeAsync();
        await store.SaveSettingsAsync(new AppSettings
        {
            PrivacyAccepted = true, Notifications = new NotificationPreferences { Mode = ReminderMode.Timed }
        });
        var wellbeing = new WellbeingService(store, _clock, TimeZoneInfo.Utc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => wellbeing.SaveNotificationPulseAsync(intent with { Mood = Mood.Great }));
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    public void EncouragementProtocolRejectsMoodArgumentsAndRetainsExpiryChecks()
    {
        var intent = new NotificationIntent(Guid.NewGuid(), Now, NotificationAction.Encouragement, null, false);
        Assert.IsFalse(NotificationIntent.TryParse(intent.ToArguments() + "&mood=5", out _));
        Assert.AreEqual(NotificationOutcome.Expired, intent.Evaluate(new AppSettings(), Now.AddHours(25)));
        Assert.Throws<ArgumentException>(() =>
            NotificationPayload.CreateEncouragement(intent with { Mood = Mood.Low }, "RUOK", "Safe text"));
        Assert.Throws<ArgumentException>(() =>
            NotificationPayload.CreateEncouragement(intent with { Action = NotificationAction.Mood }, "RUOK", "Safe text"));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private async Task<byte[]> BlobAsync(string key) =>
        (byte[])(await ScalarAsync($"SELECT protected_value FROM Preferences WHERE key='{key}';"))!;

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    private sealed class MinimumRandom : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private sealed class BoundaryRandom(bool upper) : Random
    {
        public override int Next(int minValue, int maxValue) => upper ? maxValue - 1 : minValue;
    }

    private sealed class ConflictingStore(EncouragementState state) : IEncouragementStore
    {
        public int Attempts { get; set; }
        public Task<EncouragementState> ReadEncouragementsAsync(CancellationToken cancellationToken = default) => Task.FromResult(state);
        public Task<bool> TrySaveEncouragementsAsync(EncouragementState expected, EncouragementState replacement,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromResult(false);
        }
    }

    private sealed class FailingStore : IEncouragementStore
    {
        public Task<EncouragementState> ReadEncouragementsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new EncouragementState
            {
                Preferences = new EncouragementPreferences { Enabled = true }, NextUtc = Now
            });
        public Task<bool> TrySaveEncouragementsAsync(EncouragementState expected, EncouragementState replacement,
            CancellationToken cancellationToken = default) => throw new DataStoreException("Synthetic write failure.");
    }
}
