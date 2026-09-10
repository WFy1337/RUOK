using System.Globalization;
using System.Text;
using System.Xml.Linq;
using RUOK.Application;
using RUOK.Domain;

namespace RUOK.Infrastructure.Tests;

[TestClass]
public sealed class ServiceAndExportTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RUOK.Tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private SqliteWellbeingStore Store() => new(Path.Combine(_directory, "test.db"), new DpapiDataProtector());
    private static PulseEntry Entry(DateTimeOffset time, string zone = "UTC") =>
        new(Guid.NewGuid(), time, zone, Mood.Good, null, 4, Array.Empty<ContextFactor>());

    [TestMethod]
    public async Task SaveRequiresPrivacyAcceptanceAndRecordsCurrentTime()
    {
        var store = Store();
        var service = new WellbeingService(store, new FixedTimeProvider(), TimeZoneInfo.Utc);
        var snapshot = await service.InitializeAsync();
        Assert.IsFalse(snapshot.Settings.PrivacyAccepted);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SavePulseAsync(Mood.Great, null, null, Array.Empty<ContextFactor>()));
        Assert.IsEmpty(await store.ReadEntriesAsync());
        await service.SaveSettingsAsync(snapshot.Settings with { PrivacyAccepted = true });
        var entry = await service.SavePulseAsync(Mood.Great, null, null, Array.Empty<ContextFactor>());
        Assert.AreEqual(Now, entry.RecordedAt);
        Assert.AreEqual("UTC", entry.TimeZoneId);
        await service.ResetAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SavePulseAsync(Mood.Great, null, null, Array.Empty<ContextFactor>()));
    }

    [TestMethod]
    public async Task PresentationChangesPreserveConsentRetentionAndEntries()
    {
        var store = Store();
        var service = new WellbeingService(store, new FixedTimeProvider(), TimeZoneInfo.Utc);
        await service.InitializeAsync();
        var original = new AppSettings { PrivacyAccepted = true, RetentionDays = 30 };
        await store.SaveSettingsAsync(original);
        await store.AddAsync(Entry(Now.AddDays(-90)));

        var updated = await service.SavePresentationAsync(AppearanceMode.Dark, reducedMotion: true);

        Assert.AreEqual(original with { Appearance = AppearanceMode.Dark, ReducedMotion = true }, updated);
        Assert.AreEqual(updated, await store.ReadSettingsAsync());
        Assert.HasCount(1, await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task PresentationChangesDoNotGrantConsent()
    {
        var store = Store();
        var service = new WellbeingService(store, new FixedTimeProvider(), TimeZoneInfo.Utc);
        await service.InitializeAsync();

        var updated = await service.SavePresentationAsync(AppearanceMode.Light, reducedMotion: false);

        Assert.IsFalse(updated.PrivacyAccepted);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SavePulseAsync(Mood.Good, null, null, Array.Empty<ContextFactor>()));
    }

    [TestMethod]
    public async Task InvalidOrCancelledPresentationChangesPreservePreferences()
    {
        var store = Store();
        var service = new WellbeingService(store, new FixedTimeProvider(), TimeZoneInfo.Utc);
        var original = (await service.InitializeAsync()).Settings;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SavePresentationAsync((AppearanceMode)999, reducedMotion: true));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.SavePresentationAsync(AppearanceMode.Dark, reducedMotion: true, cancelled.Token));

        Assert.AreEqual(original, await store.ReadSettingsAsync());
    }

    [TestMethod]
    public async Task RetentionKeepsItsBoundaryAndDisabledRetentionKeepsEverything()
    {
        var store = Store();
        var service = new WellbeingService(store, new FixedTimeProvider(), TimeZoneInfo.Utc);
        await service.InitializeAsync();
        await store.SaveSettingsAsync(new AppSettings { PrivacyAccepted = true, RetentionDays = 30 });
        await store.AddAsync(Entry(Now.AddDays(-30).AddTicks(-1)));
        await store.AddAsync(Entry(Now.AddDays(-30)));
        var result = await service.RefreshAsync();
        Assert.AreEqual(1, result.RemovedByRetention);
        Assert.HasCount(1, result.Entries);
        await store.SaveSettingsAsync(new AppSettings { PrivacyAccepted = true, RetentionDays = 0 });
        await store.AddAsync(Entry(Now.AddYears(-2)));
        Assert.HasCount(2, (await service.RefreshAsync()).Entries);
    }

    [TestMethod]
    public async Task CsvIsUtf8HasStableColumnsAndPreservesNumericNegativeOffset()
    {
        using var stream = new MemoryStream();
        await CsvExporter.WriteAsync(stream, new[] { Entry(Now.ToOffset(TimeSpan.FromHours(-4))) });
        var bytes = stream.ToArray();
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var csv = Encoding.UTF8.GetString(bytes);
        StringAssert.Contains(csv, string.Join(",", CsvExporter.Columns));
        StringAssert.Contains(csv, "\"-240\"");
        Assert.IsFalse(csv.Contains("\"'-240\"", StringComparison.Ordinal));
        StringAssert.Contains(csv, "\"Quick\",\"4\",\"Good\",\"\",\"4\"");
        Assert.IsFalse(CsvExporter.Columns.Contains("note"));
        StringAssert.Contains(csv, "\r\n");
    }

    [TestMethod]
    [DataRow("=SUM(1,2)")]
    [DataRow("  +SUM(1,2)")]
    [DataRow("@SUM(1,2)")]
    [DataRow("\tformula")]
    public async Task CsvNeutralizesUntrustedFormulaLikeText(string input)
    {
        using var stream = new MemoryStream();
        await CsvExporter.WriteAsync(stream, new[] { Entry(Now, input) });
        var csv = Encoding.UTF8.GetString(stream.ToArray());
        StringAssert.Contains(csv, "\"'" + input.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
    }

    [TestMethod]
    public async Task CsvQuotesCommasQuotesAndUnicodeWithInvariantTimestamps()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using var stream = new MemoryStream();
            await CsvExporter.WriteAsync(stream, new[] { Entry(Now, "test,\"zone\"\n\u00E9") });
            var csv = Encoding.UTF8.GetString(stream.ToArray());
            StringAssert.Contains(csv, "\"test,\"\"zone\"\"\n\u00E9\"");
            StringAssert.Contains(csv, "2026-09-09T09:00:00.0000000+00:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestMethod]
    public async Task CancelledExportDoesNotReplaceDestinationOrLeaveStagingFiles()
    {
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, "test.csv");
        await File.WriteAllTextAsync(destination, "existing export");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new CsvExporter().ExportAsync(destination, new[] { Entry(Now) }, cancelled.Token));
        Assert.AreEqual("existing export", await File.ReadAllTextAsync(destination));
        Assert.HasCount(1, Directory.GetFiles(_directory));
    }

    [TestMethod]
    public async Task EmptyExportIsAHeaderOnlyAndDoesNotCloseCallerStream()
    {
        using var stream = new MemoryStream();
        await CsvExporter.WriteAsync(stream, Array.Empty<PulseEntry>());
        Assert.IsTrue(stream.CanWrite);
        Assert.AreEqual(string.Join(",", CsvExporter.Columns) + "\r\n",
            Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF'));
    }

    [TestMethod]
    public void DateRangesRespectDaylightSavingAndInclusiveLastDay()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var spring = LocalDateRange.ToUtc(new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8), eastern);
        var autumn = LocalDateRange.ToUtc(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), eastern);
        Assert.AreEqual(TimeSpan.FromHours(23), spring.End - spring.Start);
        Assert.AreEqual(TimeSpan.FromHours(25), autumn.End - autumn.Start);
        Assert.Throws<ArgumentException>(() =>
            LocalDateRange.ToUtc(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 9), eastern));
    }

    [TestMethod]
    public void UnsupportedPreferencesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { RetentionDays = -1 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { Appearance = (AppearanceMode)999 }.Validate());
    }

    [TestMethod]
    public void MoodAccessibilityResourcesHaveDistinctTextAndNumericScale()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiStrings.resx"));
        var resources = document.Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")!.Value, element => element.Element("value")!.Value);
        var labels = Enumerable.Range(1, 5).Select(score => resources[$"Mood{score}"]).ToArray();
        Assert.AreEqual(5, labels.Distinct().Count());
        Assert.IsTrue(labels.All(label => !string.IsNullOrWhiteSpace(label)));
        StringAssert.Contains(resources["MoodAccessible"], "{0}");
        StringAssert.Contains(resources["MoodAccessible"], "{1}");
        StringAssert.Contains(resources["WelcomeBody"], "not a medical device");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
