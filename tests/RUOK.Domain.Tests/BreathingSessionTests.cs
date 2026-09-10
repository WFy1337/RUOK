namespace RUOK.Domain.Tests;

[TestClass]
public sealed class BreathingSessionTests
{
    [TestMethod]
    public void NullClockIsRejected()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() =>
            new BreathingSession(null!, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), TimeSpan.FromMinutes(1)));

        Assert.AreEqual("clock", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1L)]
    [DataRow(0L)]
    [DataRow(19_999_999L)]
    [DataRow(200_000_001L)]
    public void InvalidInhaleDurationsAreRejected(long ticks)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new BreathingSession(new ManualTimeProvider(), TimeSpan.FromTicks(ticks), TimeSpan.FromSeconds(6), TimeSpan.FromMinutes(1)));

        Assert.AreEqual("inhale", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1L)]
    [DataRow(0L)]
    [DataRow(19_999_999L)]
    [DataRow(200_000_001L)]
    public void InvalidExhaleDurationsAreRejected(long ticks)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new BreathingSession(new ManualTimeProvider(), TimeSpan.FromSeconds(4), TimeSpan.FromTicks(ticks), TimeSpan.FromMinutes(1)));

        Assert.AreEqual("exhale", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1L)]
    [DataRow(0L)]
    [DataRow(599_999_999L)]
    [DataRow(18_000_000_001L)]
    public void InvalidSessionDurationsAreRejected(long ticks)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new BreathingSession(new ManualTimeProvider(), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), TimeSpan.FromTicks(ticks)));

        Assert.AreEqual("duration", error.ParamName);
    }

    [TestMethod]
    [DataRow(2, 2, 1)]
    [DataRow(20, 20, 30)]
    [DataRow(2, 20, 1)]
    [DataRow(20, 2, 30)]
    public void AllDurationBoundsAreInclusive(int inhaleSeconds, int exhaleSeconds, int durationMinutes)
    {
        var session = new BreathingSession(
            new ManualTimeProvider(),
            TimeSpan.FromSeconds(inhaleSeconds),
            TimeSpan.FromSeconds(exhaleSeconds),
            TimeSpan.FromMinutes(durationMinutes));

        session.Start();

        var snapshot = session.GetSnapshot();
        Assert.AreEqual(ExerciseState.Running, snapshot.State);
        Assert.AreEqual(TimeSpan.FromSeconds(inhaleSeconds), snapshot.PhaseRemaining);
        Assert.AreEqual(TimeSpan.FromMinutes(durationMinutes), snapshot.TotalRemaining);
    }

    [TestMethod]
    public void ReadyDoesNotConsumeTimeAndHasNoActivePhase()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.AreEqual(
            new BreathingSnapshot(ExerciseState.Ready, BreathPhase.Ready, TimeSpan.Zero, TimeSpan.FromMinutes(1), 0),
            session.GetSnapshot());
    }

    [TestMethod]
    public void StartBeginsAtTheBeginningOfInhale()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        clock.Advance(TimeSpan.FromMinutes(2));

        session.Start();

        Assert.AreEqual(
            new BreathingSnapshot(ExerciseState.Running, BreathPhase.Inhale, TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(1), 0),
            session.GetSnapshot());
    }

    [TestMethod]
    [DataRow(ExerciseState.Running)]
    [DataRow(ExerciseState.Paused)]
    [DataRow(ExerciseState.Completed)]
    public void StartIsInvalidUnlessReady(ExerciseState state)
    {
        var clock = new ManualTimeProvider();
        var session = AtState(clock, state);
        var before = session.GetSnapshot();

        Assert.ThrowsExactly<InvalidOperationException>(session.Start);

        Assert.AreEqual(before, session.GetSnapshot());
    }

    [TestMethod]
    [DataRow(ExerciseState.Ready)]
    [DataRow(ExerciseState.Paused)]
    [DataRow(ExerciseState.Completed)]
    public void PauseIsInvalidUnlessRunning(ExerciseState state)
    {
        var clock = new ManualTimeProvider();
        var session = AtState(clock, state);
        var before = session.GetSnapshot();

        Assert.ThrowsExactly<InvalidOperationException>(session.Pause);

        Assert.AreEqual(before, session.GetSnapshot());
    }

    [TestMethod]
    [DataRow(ExerciseState.Ready)]
    [DataRow(ExerciseState.Running)]
    [DataRow(ExerciseState.Completed)]
    public void ResumeIsInvalidUnlessPaused(ExerciseState state)
    {
        var clock = new ManualTimeProvider();
        var session = AtState(clock, state);
        var before = session.GetSnapshot();

        Assert.ThrowsExactly<InvalidOperationException>(session.Resume);

        Assert.AreEqual(before, session.GetSnapshot());
    }

    [TestMethod]
    [DataRow(0, BreathPhase.Inhale, 4, 0d)]
    [DataRow(1, BreathPhase.Inhale, 3, 0.25)]
    [DataRow(2, BreathPhase.Inhale, 2, 0.5)]
    [DataRow(4, BreathPhase.Exhale, 6, 1d)]
    [DataRow(7, BreathPhase.Exhale, 3, 0.5)]
    [DataRow(10, BreathPhase.Inhale, 4, 0d)]
    [DataRow(12, BreathPhase.Inhale, 2, 0.5)]
    [DataRow(14, BreathPhase.Exhale, 6, 1d)]
    public void PhaseEdgesAndExpansionAreLinear(int elapsedSeconds, BreathPhase phase, int phaseSeconds, double expansion)
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));

        var snapshot = session.GetSnapshot();

        Assert.AreEqual(ExerciseState.Running, snapshot.State);
        Assert.AreEqual(phase, snapshot.Phase);
        Assert.AreEqual(TimeSpan.FromSeconds(phaseSeconds), snapshot.PhaseRemaining);
        Assert.AreEqual(TimeSpan.FromSeconds(60 - elapsedSeconds), snapshot.TotalRemaining);
        Assert.AreEqual(expansion, snapshot.Expansion, 1e-12);
    }

    [TestMethod]
    public void InhaleAndExhaleBoundariesAreAccurateToOneTick()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(4).Subtract(TimeSpan.FromTicks(1)));

        var beforeInhaleEnds = session.GetSnapshot();
        Assert.AreEqual(BreathPhase.Inhale, beforeInhaleEnds.Phase);
        Assert.AreEqual(TimeSpan.FromTicks(1), beforeInhaleEnds.PhaseRemaining);
        Assert.IsLessThan(1d, beforeInhaleEnds.Expansion);

        clock.Advance(TimeSpan.FromTicks(1));
        var exhaleStarts = session.GetSnapshot();
        Assert.AreEqual(BreathPhase.Exhale, exhaleStarts.Phase);
        Assert.AreEqual(1d, exhaleStarts.Expansion);
        Assert.AreEqual(TimeSpan.FromSeconds(6), exhaleStarts.PhaseRemaining);

        clock.Advance(TimeSpan.FromSeconds(6).Subtract(TimeSpan.FromTicks(1)));
        var beforeExhaleEnds = session.GetSnapshot();
        Assert.AreEqual(BreathPhase.Exhale, beforeExhaleEnds.Phase);
        Assert.AreEqual(TimeSpan.FromTicks(1), beforeExhaleEnds.PhaseRemaining);
        Assert.IsGreaterThan(0d, beforeExhaleEnds.Expansion);

        clock.Advance(TimeSpan.FromTicks(1));
        var nextInhale = session.GetSnapshot();
        Assert.AreEqual(BreathPhase.Inhale, nextInhale.Phase);
        Assert.AreEqual(TimeSpan.FromSeconds(4), nextInhale.PhaseRemaining);
        Assert.AreEqual(0d, nextInhale.Expansion);
    }

    [TestMethod]
    public void PauseFreezesPhaseAndTotalUntilResume()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(2));

        session.Pause();
        var paused = session.GetSnapshot();
        clock.Advance(TimeSpan.FromHours(2));

        Assert.AreEqual(ExerciseState.Paused, paused.State);
        Assert.AreEqual(BreathPhase.Inhale, paused.Phase);
        Assert.AreEqual(TimeSpan.FromSeconds(2), paused.PhaseRemaining);
        Assert.AreEqual(TimeSpan.FromSeconds(58), paused.TotalRemaining);
        Assert.AreEqual(0.5, paused.Expansion);
        Assert.AreEqual(paused, session.GetSnapshot());

        session.Resume();
        Assert.AreEqual(paused with { State = ExerciseState.Running }, session.GetSnapshot());
        clock.Advance(TimeSpan.FromSeconds(2));

        var resumed = session.GetSnapshot();
        Assert.AreEqual(BreathPhase.Exhale, resumed.Phase);
        Assert.AreEqual(TimeSpan.FromSeconds(56), resumed.TotalRemaining);
        Assert.AreEqual(1d, resumed.Expansion);
    }

    [TestMethod]
    public void MultiplePausesAccumulateOnlyRunningTime()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(3));
        session.Pause();
        clock.Advance(TimeSpan.FromMinutes(20));
        session.Resume();
        clock.Advance(TimeSpan.FromSeconds(4));
        session.Pause();
        clock.Advance(TimeSpan.FromMinutes(40));

        var paused = session.GetSnapshot();
        Assert.AreEqual(TimeSpan.FromSeconds(53), paused.TotalRemaining);
        Assert.AreEqual(BreathPhase.Exhale, paused.Phase);
        Assert.AreEqual(TimeSpan.FromSeconds(3), paused.PhaseRemaining);
        Assert.AreEqual(0.5, paused.Expansion);

        session.Resume();
        clock.Advance(TimeSpan.FromSeconds(3));

        var resumed = session.GetSnapshot();
        Assert.AreEqual(TimeSpan.FromSeconds(50), resumed.TotalRemaining);
        Assert.AreEqual(BreathPhase.Inhale, resumed.Phase);
        Assert.AreEqual(0d, resumed.Expansion);
    }

    [TestMethod]
    public void PausingExactlyAtAPhaseBoundaryRetainsTheNewPhase()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(4));

        session.Pause();
        clock.Advance(TimeSpan.FromMinutes(5));

        var snapshot = session.GetSnapshot();
        Assert.AreEqual(ExerciseState.Paused, snapshot.State);
        Assert.AreEqual(BreathPhase.Exhale, snapshot.Phase);
        Assert.AreEqual(TimeSpan.FromSeconds(6), snapshot.PhaseRemaining);
        Assert.AreEqual(1d, snapshot.Expansion);
    }

    [TestMethod]
    [DataRow(60)]
    [DataRow(61)]
    [DataRow(172800)]
    public void SnapshotNaturallyObservesCompletionAndClampsAllValues(int elapsedSeconds)
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));

        var completed = session.GetSnapshot();

        Assert.AreEqual(
            new BreathingSnapshot(ExerciseState.Completed, BreathPhase.Complete, TimeSpan.Zero, TimeSpan.Zero, 0),
            completed);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.AreEqual(completed, session.GetSnapshot());
    }

    [TestMethod]
    public void PauseAfterTheDeadlineObservesCompletionRatherThanFreezingAnExpiredSession()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.ThrowsExactly<InvalidOperationException>(session.Pause);

        Assert.AreEqual(ExerciseState.Completed, session.GetSnapshot().State);
    }

    [TestMethod]
    public void AResumedSessionCompletesAtItsRemainingRunningDuration()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(59));
        session.Pause();
        clock.Advance(TimeSpan.FromDays(1));
        session.Resume();
        clock.Advance(TimeSpan.FromSeconds(1).Subtract(TimeSpan.FromTicks(1)));

        Assert.AreEqual(ExerciseState.Running, session.GetSnapshot().State);
        Assert.AreEqual(TimeSpan.FromTicks(1), session.GetSnapshot().TotalRemaining);

        clock.Advance(TimeSpan.FromTicks(1));

        Assert.AreEqual(ExerciseState.Completed, session.GetSnapshot().State);
    }

    [TestMethod]
    [DataRow(ExerciseState.Ready)]
    [DataRow(ExerciseState.Running)]
    [DataRow(ExerciseState.Paused)]
    [DataRow(ExerciseState.Completed)]
    public void RestartResetsAndStartsFromEveryState(ExerciseState state)
    {
        var clock = new ManualTimeProvider();
        var session = AtState(clock, state);
        clock.Advance(TimeSpan.FromMinutes(10));

        session.Restart();

        Assert.AreEqual(
            new BreathingSnapshot(ExerciseState.Running, BreathPhase.Inhale, TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(1), 0),
            session.GetSnapshot());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.AreEqual(TimeSpan.FromSeconds(58), session.GetSnapshot().TotalRemaining);
    }

    [TestMethod]
    [DataRow(ExerciseState.Ready)]
    [DataRow(ExerciseState.Running)]
    [DataRow(ExerciseState.Paused)]
    [DataRow(ExerciseState.Completed)]
    public void StopIsHarmlessAndRestoresReadyFromEveryState(ExerciseState state)
    {
        var clock = new ManualTimeProvider();
        var session = AtState(clock, state);

        session.Stop();
        session.Stop();
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.AreEqual(
            new BreathingSnapshot(ExerciseState.Ready, BreathPhase.Ready, TimeSpan.Zero, TimeSpan.FromMinutes(1), 0),
            session.GetSnapshot());
        session.Start();
        Assert.AreEqual(TimeSpan.FromMinutes(1), session.GetSnapshot().TotalRemaining);
        Assert.AreEqual(0d, session.GetSnapshot().Expansion);
    }

    [TestMethod]
    public void AFinalPartialPhaseCannotHaveMoreRemainingTimeThanTheSession()
    {
        var clock = new ManualTimeProvider();
        var session = new BreathingSession(clock, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(11), TimeSpan.FromMinutes(1));
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(59));

        var snapshot = session.GetSnapshot();

        Assert.AreEqual(BreathPhase.Inhale, snapshot.Phase);
        Assert.AreEqual(TimeSpan.FromSeconds(1), snapshot.TotalRemaining);
        Assert.AreEqual(TimeSpan.FromSeconds(1), snapshot.PhaseRemaining);
        Assert.AreEqual(5d / 7, snapshot.Expansion, 1e-12);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(BreathPhase.Complete, session.GetSnapshot().Phase);
        Assert.AreEqual(0d, session.GetSnapshot().Expansion);
    }

    [TestMethod]
    public void TimestampFrequencyAndNonzeroOriginAreRespectedWithoutWallClockAccess()
    {
        var clock = new ManualTimeProvider(timestampFrequency: 1000, startingTimestamp: 8_000_000);
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromMilliseconds(2500));

        var snapshot = session.GetSnapshot();

        Assert.AreEqual(BreathPhase.Inhale, snapshot.Phase);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1500), snapshot.PhaseRemaining);
        Assert.AreEqual(TimeSpan.FromMilliseconds(57500), snapshot.TotalRemaining);
        Assert.AreEqual(0.625, snapshot.Expansion);
    }

    [TestMethod]
    public void RepeatedSnapshotsDoNotRestartOrDoubleCountElapsedTime()
    {
        var clock = new ManualTimeProvider();
        var session = Create(clock);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(2));
        var first = session.GetSnapshot();

        Assert.AreEqual(first, session.GetSnapshot());
        Assert.AreEqual(first, session.GetSnapshot());
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.AreEqual(TimeSpan.FromSeconds(57), session.GetSnapshot().TotalRemaining);
        Assert.AreEqual(0.75, session.GetSnapshot().Expansion);
    }

    private static BreathingSession Create(ManualTimeProvider clock) =>
        new(clock, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), TimeSpan.FromMinutes(1));

    private static BreathingSession AtState(ManualTimeProvider clock, ExerciseState state)
    {
        var session = Create(clock);
        if (state == ExerciseState.Ready)
        {
            return session;
        }

        session.Start();
        clock.Advance(TimeSpan.FromSeconds(2));
        if (state == ExerciseState.Paused)
        {
            session.Pause();
        }
        else if (state == ExerciseState.Completed)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            _ = session.GetSnapshot();
        }

        return session;
    }
}
