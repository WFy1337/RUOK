using RUOK.Domain;

namespace RUOK.Application;

public sealed class WellbeingService(
    IWellbeingStore store,
    TimeProvider clock,
    TimeZoneInfo timeZone)
{
    public async Task<AppSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var settings = await store.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        settings.Validate();
        var removed = 0;
        if (settings.PrivacyAccepted && settings.RetentionDays > 0)
        {
            removed = await store.DeleteRangeAsync(
                DateTimeOffset.MinValue, clock.GetUtcNow().AddDays(-settings.RetentionDays), cancellationToken)
                .ConfigureAwait(false);
        }

        var entries = await store.ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
        return new AppSnapshot(settings, entries, removed);
    }

    public async Task<PulseEntry> SavePulseAsync(
        Mood mood, int? stress, int? energy, IReadOnlyList<ContextFactor> factors,
        CancellationToken cancellationToken = default)
    {
        var settings = await store.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.PrivacyAccepted)
            throw new InvalidOperationException("Acknowledge the privacy information before recording a check-in.");

        var entry = new PulseEntry(
            Guid.NewGuid(), TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone),
            timeZone.Id, mood, stress, energy, factors);
        await store.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An entry identifier is required.", nameof(id));
        return store.DeleteAsync(id, cancellationToken);
    }

    public async Task<bool> SaveNotificationPulseAsync(
        NotificationIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var settings = await store.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (intent.Evaluate(settings, clock.GetUtcNow()) != NotificationOutcome.Save || intent.Mood is not { } mood)
            throw new InvalidOperationException("This notification is not authorized to record a check-in.");
        var entry = new PulseEntry(intent.Id, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone),
            timeZone.Id, mood, null, null, Array.Empty<ContextFactor>());
        return await store.TryAddAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppSettings> SaveReminderPreferencesAsync(
        NotificationPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        var current = await store.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var updated = current with
        {
            Notifications = preferences,
            NextReminderUtc = preferences.Mode == ReminderMode.Disabled
                ? null : clock.GetUtcNow().AddMinutes(preferences.IntervalMinutes)
        };
        await store.SaveSettingsAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<AppSettings> MarkReminderSentAsync(
        DateTimeOffset sentAt, CancellationToken cancellationToken = default)
    {
        var current = await store.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var updated = current with
        {
            NextReminderUtc = current.Notifications.Mode == ReminderMode.Disabled
                ? null : sentAt.AddMinutes(current.Notifications.IntervalMinutes)
        };
        await store.SaveSettingsAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public Task<int> DeleteRangeAsync(
        DateTimeOffset startInclusive, DateTimeOffset endExclusive, CancellationToken cancellationToken = default)
    {
        if (startInclusive >= endExclusive)
            throw new ArgumentException("The start of the range must be before its end.");
        return store.DeleteRangeAsync(startInclusive, endExclusive, cancellationToken);
    }

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return store.SaveSettingsAsync(settings, cancellationToken);
    }

    public async Task<AppSettings> SavePresentationAsync(
        AppearanceMode appearance, bool reducedMotion, CancellationToken cancellationToken = default)
    {
        var current = await store.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var updated = current with { Appearance = appearance, ReducedMotion = reducedMotion };
        updated.Validate();
        await store.SaveSettingsAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public Task ResetAsync(CancellationToken cancellationToken = default) => store.ResetAsync(cancellationToken);
}
