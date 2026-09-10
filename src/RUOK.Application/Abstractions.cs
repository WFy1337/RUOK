using RUOK.Domain;

namespace RUOK.Application;

public interface IWellbeingStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PulseEntry>> ReadEntriesAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PulseEntry entry, CancellationToken cancellationToken = default);
    Task<bool> TryAddAsync(PulseEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> DeleteRangeAsync(DateTimeOffset startInclusive, DateTimeOffset endExclusive, CancellationToken cancellationToken = default);
    Task<AppSettings> ReadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public interface IDataProtector
{
    byte[] Protect(byte[] plaintext, string purpose);
    byte[] Unprotect(byte[] ciphertext, string purpose);
}

public interface ICsvExporter
{
    Task ExportAsync(string path, IReadOnlyList<PulseEntry> entries, CancellationToken cancellationToken = default);
}

public sealed class DataStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record AppSnapshot(AppSettings Settings, IReadOnlyList<PulseEntry> Entries, int RemovedByRetention);
