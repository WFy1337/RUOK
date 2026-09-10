using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RUOK.Application;
using RUOK.Domain;

namespace RUOK.Infrastructure.Tests;

[TestClass]
public sealed class StoreTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RUOK.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "test.db");
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private SqliteWellbeingStore Create(IDataProtector? protector = null) =>
        new(DatabasePath, protector ?? new DpapiDataProtector());

    private static PulseEntry Entry(DateTimeOffset? timestamp = null) => new(
        Guid.NewGuid(), timestamp ?? Timestamp, "private-zone-marker", Mood.Low, 4, null,
        new[] { ContextFactor.Finances, ContextFactor.Sleep });

    [TestMethod]
    public async Task EntryRoundTripsWithOriginalOffsetAndMissingEnergy()
    {
        var store = Create();
        await store.InitializeAsync();
        var entry = Entry(Timestamp.ToOffset(TimeSpan.FromHours(-4)));
        await store.AddAsync(entry);
        var loaded = (await Create().ReadEntriesAsync()).Single();
        Assert.AreEqual(entry.Id, loaded.Id);
        Assert.AreEqual(entry.RecordedAt.Offset, loaded.RecordedAt.Offset);
        Assert.AreEqual(entry.RecordedAt, loaded.RecordedAt);
        Assert.AreEqual(Mood.Low, loaded.Mood);
        Assert.AreEqual(4, loaded.Stress);
        Assert.IsNull(loaded.Energy);
        CollectionAssert.AreEqual(entry.Factors.ToArray(), loaded.Factors.ToArray());
    }

    [TestMethod]
    public async Task DatabaseAndJournalDoNotContainPlaintextPayload()
    {
        var store = Create();
        await store.InitializeAsync();
        await store.AddAsync(Entry());
        foreach (var path in Directory.GetFiles(_directory))
        {
            var content = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
            Assert.IsFalse(content.Contains("private-zone-marker", StringComparison.Ordinal));
            Assert.IsFalse(content.Contains("\"recordedAt\"", StringComparison.Ordinal));
            Assert.IsFalse(content.Contains("\"stress\"", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task DuplicateEntryDoesNotCreateAnotherRecord()
    {
        var store = Create();
        await store.InitializeAsync();
        var entry = Entry();
        await store.AddAsync(entry);
        await Assert.ThrowsAsync<DataStoreException>(() => store.AddAsync(entry));
        Assert.HasCount(1, await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task DeleteRangeUsesInclusiveStartAndExclusiveEnd()
    {
        var store = Create();
        await store.InitializeAsync();
        await store.AddAsync(Entry());
        await store.AddAsync(Entry(Timestamp.AddHours(1)));
        var retained = Entry(Timestamp.AddHours(2));
        await store.AddAsync(retained);
        Assert.AreEqual(2, await store.DeleteRangeAsync(Timestamp, Timestamp.AddHours(2)));
        Assert.AreEqual(retained.Id, (await store.ReadEntriesAsync()).Single().Id);
    }

    [TestMethod]
    public async Task DeleteEntryPreservesOtherEntriesAndMissingDeleteIsExplicit()
    {
        var store = Create();
        await store.InitializeAsync();
        var removed = Entry();
        var retained = Entry();
        await store.AddAsync(removed);
        await store.AddAsync(retained);
        await store.DeleteAsync(removed.Id);
        Assert.AreEqual(retained.Id, (await store.ReadEntriesAsync()).Single().Id);
        await Assert.ThrowsAsync<DataStoreException>(() => store.DeleteAsync(removed.Id));
    }

    [TestMethod]
    public async Task SettingsPersistAndResetRequiresPrivacyAcknowledgementAgain()
    {
        var store = Create();
        await store.InitializeAsync();
        var settings = new AppSettings
        {
            Appearance = AppearanceMode.Dark, PrivacyAccepted = true, ReducedMotion = true, RetentionDays = 90
        };
        await store.SaveSettingsAsync(settings);
        await store.AddAsync(Entry());
        Assert.AreEqual(settings, await Create().ReadSettingsAsync());
        await store.ResetAsync();
        Assert.IsEmpty(await store.ReadEntriesAsync());
        Assert.AreEqual(new AppSettings(), await store.ReadSettingsAsync());
    }

    [TestMethod]
    public async Task FailedProtectionNeverWritesPlaintext()
    {
        var store = Create(new FailingProtector(failProtection: true));
        await store.InitializeAsync();
        await Assert.ThrowsAsync<DataStoreException>(() => store.AddAsync(Entry()));
        Assert.AreEqual(0L, ExecuteScalar("SELECT COUNT(*) FROM Entries;"));
    }

    [TestMethod]
    public async Task FailedDecryptionIsNotAnEmptyHistoryOrAutomaticReset()
    {
        var store = Create();
        await store.InitializeAsync();
        await store.AddAsync(Entry());
        var unavailableProfile = Create(new FailingProtector(failProtection: false));
        await Assert.ThrowsAsync<DataStoreException>(() => unavailableProfile.ReadEntriesAsync());
        Assert.AreEqual(1L, ExecuteScalar("SELECT COUNT(*) FROM Entries;"));
        Assert.HasCount(1, await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task CorruptRowBlocksRangeDeletionButExplicitResetCanRemoveIt()
    {
        var store = Create();
        await store.InitializeAsync();
        await store.AddAsync(Entry());
        ExecuteScalar("UPDATE Entries SET protected_payload = x'01020304';");
        await Assert.ThrowsAsync<DataStoreException>(() => store.DeleteRangeAsync(Timestamp.AddDays(-1), Timestamp.AddDays(1)));
        Assert.AreEqual(1L, ExecuteScalar("SELECT COUNT(*) FROM Entries;"));
        await store.ResetAsync();
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task SwappingRowIdentifiersFailsPurposeProtection()
    {
        var store = Create();
        await store.InitializeAsync();
        await store.AddAsync(Entry());
        ExecuteScalar($"UPDATE Entries SET entry_id = '{Guid.NewGuid():N}';");
        await Assert.ThrowsAsync<DataStoreException>(() => store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task NewerSchemaIsNotMigratedOrReset()
    {
        var store = Create();
        await store.InitializeAsync();
        ExecuteScalar("PRAGMA user_version = 2;");
        await Assert.ThrowsAsync<DataStoreException>(() => store.InitializeAsync());
        await Assert.ThrowsAsync<DataStoreException>(() => store.ResetAsync());
        Assert.AreEqual(2L, ExecuteScalar("PRAGMA user_version;"));
    }

    [TestMethod]
    public async Task CancellationDoesNotAddAnEntry()
    {
        var store = Create();
        await store.InitializeAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.AddAsync(Entry(), cancelled.Token));
        Assert.IsEmpty(await store.ReadEntriesAsync());
    }

    [TestMethod]
    public async Task ConcurrentAddsAreSerializedWithoutLosingEntries()
    {
        var store = Create();
        await store.InitializeAsync();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => store.AddAsync(Entry(Timestamp.AddMinutes(index)))));
        Assert.HasCount(12, await store.ReadEntriesAsync());
    }

    [TestMethod]
    public void DpapiRoundTripFailsWhenPurposeOrCiphertextChanges()
    {
        var protector = new DpapiDataProtector();
        var plaintext = Encoding.UTF8.GetBytes("synthetic test content");
        var ciphertext = protector.Protect(plaintext, "test-one");
        CollectionAssert.AreEqual(plaintext, protector.Unprotect(ciphertext, "test-one"));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(ciphertext, "test-two"));
        ciphertext[^1] ^= 1;
        Assert.Throws<CryptographicException>(() => protector.Unprotect(ciphertext, "test-one"));
    }

    private object? ExecuteScalar(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private sealed class FailingProtector(bool failProtection) : IDataProtector
    {
        private readonly DpapiDataProtector _inner = new();
        public byte[] Protect(byte[] plaintext, string purpose) => failProtection
            ? throw new CryptographicException("Synthetic protection failure.") : _inner.Protect(plaintext, purpose);
        public byte[] Unprotect(byte[] ciphertext, string purpose) => failProtection
            ? _inner.Unprotect(ciphertext, purpose) : throw new CryptographicException("Synthetic profile failure.");
    }
}
