namespace RUOK.Domain;

public sealed record PulseEntry
{
    public PulseEntry(
        Guid id,
        DateTimeOffset recordedAt,
        string timeZoneId,
        Mood mood,
        int? stress,
        int? energy,
        IReadOnlyList<ContextFactor> factors)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An entry ID must not be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        if (timeZoneId.Length > 128)
        {
            throw new ArgumentException("A time zone ID must not exceed 128 characters.", nameof(timeZoneId));
        }

        if (!Enum.IsDefined(mood))
        {
            throw new ArgumentOutOfRangeException(nameof(mood), mood, "Mood must be between 1 and 5.");
        }

        ValidateScore(stress, nameof(stress));
        ValidateScore(energy, nameof(energy));
        ArgumentNullException.ThrowIfNull(factors);

        var factorCopy = factors.ToArray();
        if (factorCopy.Length > 10)
        {
            throw new ArgumentException("An entry must not have more than 10 context factors.", nameof(factors));
        }

        var distinctFactors = new HashSet<ContextFactor>();
        foreach (var factor in factorCopy)
        {
            if (!Enum.IsDefined(factor))
            {
                throw new ArgumentOutOfRangeException(nameof(factors), factor, "Every context factor must be defined.");
            }

            if (!distinctFactors.Add(factor))
            {
                throw new ArgumentException("Context factors must be distinct.", nameof(factors));
            }
        }

        if (distinctFactors.Contains(ContextFactor.PreferNotToSay) && factorCopy.Length > 1)
        {
            throw new ArgumentException("PreferNotToSay cannot be combined with other context factors.", nameof(factors));
        }

        Id = id;
        RecordedAt = recordedAt;
        TimeZoneId = timeZoneId;
        Mood = mood;
        Stress = stress;
        Energy = energy;
        Factors = Array.AsReadOnly(factorCopy);
    }

    public Guid Id { get; }
    public DateTimeOffset RecordedAt { get; }
    public string TimeZoneId { get; }
    public Mood Mood { get; }
    public int? Stress { get; }
    public int? Energy { get; }
    public IReadOnlyList<ContextFactor> Factors { get; }

    private static void ValidateScore(int? value, string parameterName)
    {
        if (value is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A supplied score must be between 1 and 5.");
        }
    }
}
