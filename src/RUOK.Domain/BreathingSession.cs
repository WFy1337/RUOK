namespace RUOK.Domain;

public sealed class BreathingSession
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan _inhale;
    private readonly TimeSpan _exhale;
    private readonly TimeSpan _duration;
    private ExerciseState _state = ExerciseState.Ready;
    private TimeSpan _elapsedBeforeRun;
    private long _runStartedAt;

    public BreathingSession(TimeProvider clock, TimeSpan inhale, TimeSpan exhale, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidatePhaseDuration(inhale, nameof(inhale));
        ValidatePhaseDuration(exhale, nameof(exhale));
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be between 1 and 30 minutes.");
        }

        _clock = clock;
        _inhale = inhale;
        _exhale = exhale;
        _duration = duration;
    }

    public void Start()
    {
        RequireState(ExerciseState.Ready);
        BeginRunning();
    }

    public void Pause()
    {
        var elapsed = ObserveElapsed();
        RequireState(ExerciseState.Running);
        _elapsedBeforeRun = elapsed;
        _state = ExerciseState.Paused;
    }

    public void Resume()
    {
        RequireState(ExerciseState.Paused);
        BeginRunning();
    }

    public void Restart()
    {
        _elapsedBeforeRun = TimeSpan.Zero;
        BeginRunning();
    }

    public void Stop()
    {
        _state = ExerciseState.Ready;
        _elapsedBeforeRun = TimeSpan.Zero;
        _runStartedAt = 0;
    }

    public BreathingSnapshot GetSnapshot()
    {
        var elapsed = ObserveElapsed();
        if (_state == ExerciseState.Ready)
        {
            return new BreathingSnapshot(_state, BreathPhase.Ready, TimeSpan.Zero, _duration, 0);
        }

        if (_state == ExerciseState.Completed)
        {
            return new BreathingSnapshot(_state, BreathPhase.Complete, TimeSpan.Zero, TimeSpan.Zero, 0);
        }

        var totalRemaining = _duration - elapsed;
        var positionTicks = elapsed.Ticks % (_inhale + _exhale).Ticks;
        var inhaling = positionTicks < _inhale.Ticks;
        var phaseElapsedTicks = inhaling ? positionTicks : positionTicks - _inhale.Ticks;
        var phaseDuration = inhaling ? _inhale : _exhale;
        var progress = (double)phaseElapsedTicks / phaseDuration.Ticks;
        var phaseRemaining = TimeSpan.FromTicks(Math.Min(
            phaseDuration.Ticks - phaseElapsedTicks,
            totalRemaining.Ticks));

        return new BreathingSnapshot(
            _state,
            inhaling ? BreathPhase.Inhale : BreathPhase.Exhale,
            phaseRemaining,
            totalRemaining,
            inhaling ? progress : 1 - progress);
    }

    private TimeSpan ObserveElapsed()
    {
        if (_state != ExerciseState.Running)
        {
            return _elapsedBeforeRun;
        }

        var elapsedThisRun = _clock.GetElapsedTime(_runStartedAt, _clock.GetTimestamp());
        if (elapsedThisRun >= _duration - _elapsedBeforeRun)
        {
            _elapsedBeforeRun = _duration;
            _state = ExerciseState.Completed;
            return _duration;
        }

        return _elapsedBeforeRun + elapsedThisRun;
    }

    private void BeginRunning()
    {
        _runStartedAt = _clock.GetTimestamp();
        _state = ExerciseState.Running;
    }

    private void RequireState(ExerciseState expected)
    {
        if (_state != expected)
        {
            throw new InvalidOperationException($"This operation requires state {expected}; the session is {_state}.");
        }
    }

    private static void ValidatePhaseDuration(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.FromSeconds(2) || value > TimeSpan.FromSeconds(20))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A breathing phase must be between 2 and 20 seconds.");
        }
    }
}
