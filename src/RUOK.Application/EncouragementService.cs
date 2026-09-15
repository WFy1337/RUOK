namespace RUOK.Application;

public interface IEncouragementStore
{
    Task<EncouragementState> ReadEncouragementsAsync(CancellationToken cancellationToken = default);
    Task<bool> TrySaveEncouragementsAsync(
        EncouragementState expected, EncouragementState replacement, CancellationToken cancellationToken = default);
}

public sealed record PreparedEncouragement(EncouragementState State, string? Message);

public sealed class EncouragementService(
    IEncouragementStore store, IWellbeingStore wellbeingStore, TimeProvider clock,
    TimeZoneInfo timeZone, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    public async Task<EncouragementState> ReadAsync(CancellationToken cancellationToken = default)
    {
        var state = await store.ReadEncouragementsAsync(cancellationToken).ConfigureAwait(false);
        state.Validate();
        return state;
    }

    public async Task<EncouragementState> SavePreferencesAsync(
        EncouragementPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = preferences.Normalize();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (current.Preferences.Normalize() == normalized)
                return current;
            var updated = new EncouragementState
            {
                Preferences = normalized,
                NextUtc = normalized.Enabled ? NextTime(normalized, clock.GetUtcNow()) : null,
                LastMessage = normalized.Enabled && current.Preferences.Normalize().Messages == normalized.Messages
                    ? current.LastMessage : null
            };
            if (await store.TrySaveEncouragementsAsync(current, updated, cancellationToken).ConfigureAwait(false))
                return updated;
        }
        throw new InvalidOperationException("Encouragement settings changed. Refresh and try again.");
    }

    public async Task<PreparedEncouragement> PrepareAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!current.Preferences.Enabled)
                return new PreparedEncouragement(current, null);
            var settings = await wellbeingStore.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
            settings.Validate();
            var now = clock.GetUtcNow();
            if (current.NextUtc is { } next
                && (now < next || !ReminderSchedule.IsWithinWindow(settings.Notifications, now, timeZone)))
                return new PreparedEncouragement(current, null);
            var message = current.NextUtc is null ? null : SelectMessage(current.Preferences, current.LastMessage);
            var updated = current with
            {
                NextUtc = NextTime(current.Preferences, now),
                LastMessage = message ?? current.LastMessage
            };
            // Advance before native delivery: a restart or uncertain Show must not replay the same attempt.
            if (await store.TrySaveEncouragementsAsync(current, updated, cancellationToken).ConfigureAwait(false))
                return new PreparedEncouragement(updated, message);
        }
        throw new InvalidOperationException("The encouragement schedule changed. Refresh and try again.");
    }

    public string SelectMessage(EncouragementPreferences preferences, string? previous = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        var messages = preferences.GetMessages();
        var candidates = messages.Count > 1
            ? messages.Where(message => !string.Equals(message, previous, StringComparison.OrdinalIgnoreCase)).ToArray()
            : messages.ToArray();
        return candidates[_random.Next(candidates.Length)];
    }

    private DateTimeOffset NextTime(EncouragementPreferences preferences, DateTimeOffset now)
    {
        var minimumSeconds = preferences.MinimumIntervalMinutes * 60;
        return now.ToUniversalTime().AddSeconds(_random.Next(minimumSeconds, minimumSeconds * 2 + 1));
    }
}
