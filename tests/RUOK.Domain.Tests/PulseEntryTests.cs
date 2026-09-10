using System.Text.Json;

namespace RUOK.Domain.Tests;

[TestClass]
public sealed class PulseEntryTests
{
    private static readonly Guid EntryId = new("72e3585a-6d19-4b35-8cdb-d6f93400b111");
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 9, 12, 30, 0, TimeSpan.FromHours(3));

    [TestMethod]
    public void EnumValuesMatchThePublicContract()
    {
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, Enum.GetValues<Mood>().Select(value => (int)value).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "Work", "Family", "Health", "Finances", "Sleep", "SocialActivity",
                "Exercise", "CurrentTask", "Other", "PreferNotToSay"
            },
            Enum.GetNames<ContextFactor>());
    }

    [TestMethod]
    public void ConstructorPreservesAllValuesWithoutResolvingTheTimeZone()
    {
        var entry = new PulseEntry(
            EntryId, RecordedAt, "Synthetic/NotInstalled", Mood.Good, 2, 5,
            new[] { ContextFactor.Work, ContextFactor.Sleep });

        Assert.AreEqual(EntryId, entry.Id);
        Assert.AreEqual(RecordedAt, entry.RecordedAt);
        Assert.AreEqual(RecordedAt.Offset, entry.RecordedAt.Offset);
        Assert.AreEqual("Synthetic/NotInstalled", entry.TimeZoneId);
        Assert.AreEqual(Mood.Good, entry.Mood);
        Assert.AreEqual(2, entry.Stress);
        Assert.AreEqual(5, entry.Energy);
        CollectionAssert.AreEqual(new[] { ContextFactor.Work, ContextFactor.Sleep }, entry.Factors.ToArray());
    }

    [TestMethod]
    public void OptionalScoresAndFactorsCanBeAbsent()
    {
        var entry = Create();

        Assert.IsNull(entry.Stress);
        Assert.IsNull(entry.Energy);
        Assert.IsEmpty(entry.Factors);
    }

    [TestMethod]
    [DataRow(Mood.VeryLow)]
    [DataRow(Mood.Low)]
    [DataRow(Mood.Neutral)]
    [DataRow(Mood.Good)]
    [DataRow(Mood.Great)]
    public void EveryDefinedMoodIsAccepted(Mood mood)
    {
        Assert.AreEqual(mood, Create(mood: mood).Mood);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(1, 5)]
    [DataRow(5, 1)]
    [DataRow(5, 5)]
    public void OptionalScoreBoundsAreInclusive(int stress, int energy)
    {
        var entry = Create(stress: stress, energy: energy);

        Assert.AreEqual(stress, entry.Stress);
        Assert.AreEqual(energy, entry.Energy);
    }

    [TestMethod]
    public void EmptyIdIsRejected()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => new PulseEntry(
            Guid.Empty, RecordedAt, "UTC", Mood.Neutral, null, null, Array.Empty<ContextFactor>()));

        Assert.AreEqual("id", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(6)]
    [DataRow(int.MaxValue)]
    public void UndefinedMoodIsRejected(int mood)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(mood: (Mood)mood));

        Assert.AreEqual("mood", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(6)]
    [DataRow(int.MaxValue)]
    public void InvalidStressIsRejected(int stress)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(stress: stress));

        Assert.AreEqual("stress", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(6)]
    [DataRow(int.MaxValue)]
    public void InvalidEnergyIsRejected(int energy)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(energy: energy));

        Assert.AreEqual("energy", error.ParamName);
    }

    [TestMethod]
    public void NullTimeZoneIsRejected()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() => Create(timeZoneId: null!));

        Assert.AreEqual("timeZoneId", error.ParamName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t\r\n")]
    public void BlankTimeZoneIsRejected(string timeZoneId)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Create(timeZoneId: timeZoneId));

        Assert.AreEqual("timeZoneId", error.ParamName);
    }

    [TestMethod]
    public void TimeZoneLengthBoundIsInclusive()
    {
        Assert.AreEqual(128, Create(timeZoneId: new string('Z', 128)).TimeZoneId.Length);
        var error = Assert.ThrowsExactly<ArgumentException>(() => Create(timeZoneId: new string('Z', 129)));
        Assert.AreEqual("timeZoneId", error.ParamName);
    }

    [TestMethod]
    public void RecordedAtHasNoCurrentClockOrFutureRestriction()
    {
        var earliest = new PulseEntry(EntryId, DateTimeOffset.MinValue, "UTC", Mood.Neutral, null, null, []);
        var latest = new PulseEntry(EntryId, DateTimeOffset.MaxValue, "UTC", Mood.Neutral, null, null, []);

        Assert.AreEqual(DateTimeOffset.MinValue, earliest.RecordedAt);
        Assert.AreEqual(DateTimeOffset.MaxValue, latest.RecordedAt);
    }

    [TestMethod]
    public void NullFactorsAreRejected()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() => new PulseEntry(
            EntryId, RecordedAt, "UTC", Mood.Neutral, null, null, null!));

        Assert.AreEqual("factors", error.ParamName);
    }

    [TestMethod]
    public void MoreThanTenFactorsAreRejected()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() =>
            Create(factors: Enumerable.Repeat(ContextFactor.Work, 11).ToArray()));

        Assert.AreEqual("factors", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(10)]
    [DataRow(int.MaxValue)]
    public void UndefinedFactorsAreRejected(int factor)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Create(factors: new[] { (ContextFactor)factor }));

        Assert.AreEqual("factors", error.ParamName);
    }

    [TestMethod]
    [DataRow(ContextFactor.Work)]
    [DataRow(ContextFactor.Other)]
    [DataRow(ContextFactor.PreferNotToSay)]
    public void DuplicateFactorsAreRejected(ContextFactor factor)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Create(factors: new[] { factor, factor }));

        Assert.AreEqual("factors", error.ParamName);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PreferNotToSayCannotBeCombinedWithAnotherFactor(bool preferFirst)
    {
        var factors = preferFirst
            ? new[] { ContextFactor.PreferNotToSay, ContextFactor.Work }
            : new[] { ContextFactor.Work, ContextFactor.PreferNotToSay };

        var error = Assert.ThrowsExactly<ArgumentException>(() => Create(factors: factors));

        Assert.AreEqual("factors", error.ParamName);
    }

    [TestMethod]
    public void PreferNotToSayAloneIsAccepted()
    {
        CollectionAssert.AreEqual(
            new[] { ContextFactor.PreferNotToSay },
            Create(factors: new[] { ContextFactor.PreferNotToSay }).Factors.ToArray());
    }

    [TestMethod]
    public void AllOtherFactorsMayBeCombined()
    {
        var factors = Enum.GetValues<ContextFactor>()
            .Where(factor => factor != ContextFactor.PreferNotToSay)
            .ToArray();

        CollectionAssert.AreEqual(factors, Create(factors: factors).Factors.ToArray());
    }

    [TestMethod]
    public void FactorsAreDefensivelyCopiedFromAList()
    {
        var factors = new List<ContextFactor> { ContextFactor.Work, ContextFactor.Exercise };
        var entry = Create(factors: factors);

        factors[0] = ContextFactor.Family;
        factors.Clear();

        CollectionAssert.AreEqual(new[] { ContextFactor.Work, ContextFactor.Exercise }, entry.Factors.ToArray());
    }

    [TestMethod]
    public void FactorsAreDefensivelyCopiedFromAnArray()
    {
        var factors = new[] { ContextFactor.Work };
        var entry = Create(factors: factors);

        factors[0] = ContextFactor.Sleep;

        Assert.AreEqual(ContextFactor.Work, entry.Factors[0]);
    }

    [TestMethod]
    public void ExposedFactorsCannotBeMutated()
    {
        var entry = Create(factors: new[] { ContextFactor.Work });
        var collection = (IList<ContextFactor>)entry.Factors;

        Assert.IsTrue(collection.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => collection[0] = ContextFactor.Family);
        Assert.ThrowsExactly<NotSupportedException>(() => collection.Add(ContextFactor.Sleep));
        Assert.ThrowsExactly<NotSupportedException>(() => collection.Clear());
        Assert.AreEqual(ContextFactor.Work, entry.Factors[0]);
    }

    [TestMethod]
    public void EntryIsSealedWithOnePublicConstructorAndNoPropertySetters()
    {
        Assert.IsTrue(typeof(PulseEntry).IsSealed);
        Assert.HasCount(1, typeof(PulseEntry).GetConstructors());
        Assert.HasCount(7, typeof(PulseEntry).GetConstructors()[0].GetParameters());
        Assert.IsTrue(typeof(PulseEntry).GetProperties().All(property => property.SetMethod is null));
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(1, null)]
    [DataRow(null, 5)]
    [DataRow(2, 4)]
    public void SystemTextJsonRoundTripsThroughTheSingleConstructor(int? stress, int? energy)
    {
        var entry = Create(
            timeZoneId: "Synthetic/UnknownZone",
            mood: Mood.Great,
            stress: stress,
            energy: energy,
            factors: new[] { ContextFactor.Sleep, ContextFactor.SocialActivity });

        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<PulseEntry>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(entry.Id, restored.Id);
        Assert.AreEqual(entry.RecordedAt, restored.RecordedAt);
        Assert.AreEqual(entry.RecordedAt.Offset, restored.RecordedAt.Offset);
        Assert.AreEqual(entry.TimeZoneId, restored.TimeZoneId);
        Assert.AreEqual(entry.Mood, restored.Mood);
        Assert.AreEqual(entry.Stress, restored.Stress);
        Assert.AreEqual(entry.Energy, restored.Energy);
        CollectionAssert.AreEqual(entry.Factors.ToArray(), restored.Factors.ToArray());
        Assert.IsTrue(((IList<ContextFactor>)restored.Factors).IsReadOnly);
    }

    [TestMethod]
    public void SystemTextJsonRoundTripsAnEmptyFactorList()
    {
        var restored = JsonSerializer.Deserialize<PulseEntry>(JsonSerializer.Serialize(Create()));

        Assert.IsNotNull(restored);
        Assert.IsEmpty(restored.Factors);
        Assert.IsNull(restored.Stress);
        Assert.IsNull(restored.Energy);
    }

    [TestMethod]
    public void SystemTextJsonWebDefaultsStillBindTheConstructor()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var entry = Create(factors: new[] { ContextFactor.PreferNotToSay });

        var restored = JsonSerializer.Deserialize<PulseEntry>(JsonSerializer.Serialize(entry, options), options);

        Assert.IsNotNull(restored);
        Assert.AreEqual(entry.Id, restored.Id);
        Assert.AreEqual(entry.Mood, restored.Mood);
        CollectionAssert.AreEqual(entry.Factors.ToArray(), restored.Factors.ToArray());
    }

    [TestMethod]
    public void SystemTextJsonCannotBypassConstructorValidation()
    {
        const string json = """
            {
              "Id": "00000000-0000-0000-0000-000000000000",
              "RecordedAt": "2026-09-09T12:30:00+03:00",
              "TimeZoneId": "UTC",
              "Mood": 3,
              "Stress": null,
              "Energy": null,
              "Factors": []
            }
            """;

        Assert.ThrowsExactly<ArgumentException>(() => JsonSerializer.Deserialize<PulseEntry>(json));
    }

    private static PulseEntry Create(
        string timeZoneId = "UTC",
        Mood mood = Mood.Neutral,
        int? stress = null,
        int? energy = null,
        IReadOnlyList<ContextFactor>? factors = null) =>
        new(EntryId, RecordedAt, timeZoneId, mood, stress, energy, factors ?? Array.Empty<ContextFactor>());
}
