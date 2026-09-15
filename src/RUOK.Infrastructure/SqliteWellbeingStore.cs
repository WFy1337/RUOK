using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using RUOK.Application;
using RUOK.Domain;

namespace RUOK.Infrastructure;

public sealed class SqliteWellbeingStore : IWellbeingStore, IEncouragementStore
{
    private const int SchemaVersion = 1;
    private const int MaximumPayloadBytes = 128 * 1024;
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public SqliteWellbeingStore(string path, IDataProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(protector);
        _path = Path.GetFullPath(path);
        _protector = protector;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(versionCommand.ExecuteScalar());
            if (version is not (0 or SchemaVersion))
                throw new DataStoreException("This database uses an unsupported schema. No data was changed.");

            if (version == 0)
            {
                using var tables = connection.CreateCommand();
                tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
                if (Convert.ToInt32(tables.ExecuteScalar()) != 0)
                    throw new DataStoreException("An unrecognized database was found. No data was changed.");

                using var transaction = connection.BeginTransaction();
                using var create = connection.CreateCommand();
                create.Transaction = transaction;
                create.CommandText = """
                    CREATE TABLE Entries (
                        entry_id TEXT PRIMARY KEY NOT NULL,
                        protected_payload BLOB NOT NULL,
                        protection_version INTEGER NOT NULL CHECK (protection_version = 1)
                    );
                    CREATE TABLE Preferences (
                        key TEXT PRIMARY KEY NOT NULL,
                        protected_value BLOB NOT NULL,
                        value_version INTEGER NOT NULL CHECK (value_version = 1)
                    );
                    PRAGMA user_version = 1;
                    """;
                create.ExecuteNonQuery();
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Commit();
            }

            using var journal = connection.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode = WAL;";
            journal.ExecuteScalar();
            return true;
        }, cancellationToken, createDatabase: true);

    public Task<IReadOnlyList<PulseEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<PulseEntry>>(connection => ReadEntries(connection, null, cancellationToken), cancellationToken);

    public Task AddAsync(PulseEntry entry, CancellationToken cancellationToken = default) =>
        InsertAsync(entry, allowDuplicate: false, cancellationToken);

    public Task<bool> TryAddAsync(PulseEntry entry, CancellationToken cancellationToken = default) =>
        InsertAsync(entry, allowDuplicate: true, cancellationToken);

    private Task<bool> InsertAsync(PulseEntry entry, bool allowDuplicate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return RunAsync(connection =>
        {
            var payload = Protect(entry, EntryPurpose(entry.Id));
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO Entries(entry_id, protected_payload, protection_version) VALUES ($id, $payload, 1)"
                + (allowDuplicate ? " ON CONFLICT(entry_id) DO NOTHING;" : ";");
            insert.Parameters.AddWithValue("$id", entry.Id.ToString("N"));
            insert.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
            cancellationToken.ThrowIfCancellationRequested();
            return insert.ExecuteNonQuery() == 1;
        }, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An entry identifier is required.", nameof(id));
        return RunAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Entries WHERE entry_id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString("N"));
            if (command.ExecuteNonQuery() != 1)
                throw new DataStoreException("The selected entry no longer exists. Refresh history and try again.");
            return true;
        }, cancellationToken);
    }

    public Task<int> DeleteRangeAsync(
        DateTimeOffset startInclusive, DateTimeOffset endExclusive, CancellationToken cancellationToken = default)
    {
        if (startInclusive >= endExclusive)
            throw new ArgumentException("The start of the range must be before its end.");
        return RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            // Decrypt before selecting a range; timestamps must never be plaintext SQL indices.
            var matches = ReadEntries(connection, transaction, cancellationToken)
                .Where(entry => entry.RecordedAt >= startInclusive && entry.RecordedAt < endExclusive)
                .Select(entry => entry.Id).ToArray();
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Entries WHERE entry_id = $id;";
            var idParameter = delete.Parameters.Add("$id", SqliteType.Text);
            foreach (var id in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                idParameter.Value = id.ToString("N");
                delete.ExecuteNonQuery();
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return matches.Length;
        }, cancellationToken);
    }

    public Task<AppSettings> ReadSettingsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT protected_value, value_version FROM Preferences WHERE key = 'app';";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return new AppSettings();
            if (reader.GetInt32(1) != 1)
                throw new DataStoreException("The settings format is not supported. No settings were reset.");
            var settings = Unprotect<AppSettings>((byte[])reader[0], "RUOK:settings:v1");
            settings.Validate();
            return settings;
        }, cancellationToken);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return RunAsync(connection =>
        {
            var payload = Protect(settings, "RUOK:settings:v1");
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Preferences(key, protected_value, value_version) VALUES ('app', $value, 1)
                ON CONFLICT(key) DO UPDATE SET protected_value = excluded.protected_value, value_version = 1;
                """;
            command.Parameters.Add("$value", SqliteType.Blob).Value = payload;
            cancellationToken.ThrowIfCancellationRequested();
            command.ExecuteNonQuery();
            return true;
        }, cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt32(version.ExecuteScalar()) != SchemaVersion)
                throw new DataStoreException("This database format cannot be reset by this version of RUOK.");
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Entries; DELETE FROM Preferences;";
            command.ExecuteNonQuery();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }, cancellationToken);

    public Task<EncouragementState> ReadEncouragementsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(connection => ReadEncouragements(connection, null), cancellationToken);

    public Task<bool> TrySaveEncouragementsAsync(
        EncouragementState expected, EncouragementState replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        expected.Validate();
        replacement.Validate();
        return RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            var current = ReadEncouragements(connection, transaction);
            if (current != expected)
                return false;
            if (current == replacement)
                return true;
            var payload = Protect(replacement, "RUOK:encouragements:v1");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Preferences(key, protected_value, value_version) VALUES ('encouragements', $value, 1)
                ON CONFLICT(key) DO UPDATE SET protected_value = excluded.protected_value, value_version = 1;
                """;
            command.Parameters.Add("$value", SqliteType.Blob).Value = payload;
            cancellationToken.ThrowIfCancellationRequested();
            command.ExecuteNonQuery();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }, cancellationToken);
    }

    private EncouragementState ReadEncouragements(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT protected_value, value_version FROM Preferences WHERE key = 'encouragements';";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return new EncouragementState();
        if (reader.GetInt32(1) != 1)
            throw new DataStoreException("The encouragement settings format is not supported. No settings were reset.");
        var state = Unprotect<EncouragementState>((byte[])reader[0], "RUOK:encouragements:v1");
        state.Validate();
        return state;
    }

    private IReadOnlyList<PulseEntry> ReadEntries(
        SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT entry_id, protected_payload, protection_version FROM Entries;";
        using var reader = command.ExecuteReader();
        var entries = new List<PulseEntry>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(reader.GetString(0), "N", out var id) || reader.GetInt32(2) != 1)
                throw new DataStoreException("An entry has an unsupported format. No data was changed.");
            var entry = Unprotect<PulseEntry>((byte[])reader[1], EntryPurpose(id));
            if (entry.Id != id)
                throw new DataStoreException("An entry failed its identity check. No data was changed.");
            entries.Add(entry);
        }
        return entries.OrderByDescending(entry => entry.RecordedAt).ThenBy(entry => entry.Id).ToArray();
    }

    private byte[] Protect<T>(T value, string purpose)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new Payload<T>(1, value), _json);
        try
        {
            if (plaintext.Length > MaximumPayloadBytes)
                throw new DataStoreException("The record exceeds the supported size.");
            return _protector.Protect(plaintext, purpose);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private T Unprotect<T>(byte[] ciphertext, string purpose)
    {
        if (ciphertext.Length > MaximumPayloadBytes)
            throw new DataStoreException("A protected record exceeds the supported size.");
        var plaintext = _protector.Unprotect(ciphertext, purpose);
        try
        {
            var payload = JsonSerializer.Deserialize<Payload<T>>(plaintext, _json);
            if (payload is null || payload.Version != 1 || payload.Value is null)
                throw new DataStoreException("A protected record has an unsupported format.");
            return payload.Value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<T> RunAsync<T>(
        Func<SqliteConnection, T> action, CancellationToken cancellationToken, bool createDatabase = false)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Microsoft.Data.Sqlite has synchronous I/O. Serialize it off the UI thread.
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (createDatabase)
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = _path,
                    Mode = createDatabase ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
                    Pooling = false,
                    DefaultTimeout = 5
                }.ToString());
                connection.Open();
                return action(connection);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or CryptographicException
            or JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new DataStoreException(
                "RUOK could not safely read or save local data. Check storage access and the Windows profile. Existing data was not reset.",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string EntryPurpose(Guid id) => $"RUOK:entry:v1:{id:N}";
    private sealed record Payload<T>(int Version, T Value);
}
