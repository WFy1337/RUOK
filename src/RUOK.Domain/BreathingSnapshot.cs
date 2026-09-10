namespace RUOK.Domain;

public enum ExerciseState
{
    Ready,
    Running,
    Paused,
    Completed
}

public enum BreathPhase
{
    Ready,
    Inhale,
    Exhale,
    Complete
}

public record BreathingSnapshot(
    ExerciseState State,
    BreathPhase Phase,
    TimeSpan PhaseRemaining,
    TimeSpan TotalRemaining,
    double Expansion);
