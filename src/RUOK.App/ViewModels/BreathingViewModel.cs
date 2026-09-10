using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUOK.Domain;
using RUOK_App.Resources;

namespace RUOK_App.ViewModels;

public sealed class BreathingViewModel : ObservableObject
{
    private readonly TimeProvider _clock;
    private BreathingSession? _session;
    private Choice<int> _selectedDuration;
    private double _inhaleSeconds = 4;
    private double _exhaleSeconds = 6;
    private bool _reducedMotion;
    private bool _systemReducedMotion;
    private bool _highContrast;
    private string _phase;
    private string _remaining = "";
    private double _scale = 0.76;
    private ExerciseState _state = ExerciseState.Ready;
    private bool _isRefreshing;
    private bool _refreshRequested;
    private bool _canStart;
    private bool _canPauseResume;
    private bool _canRestart;
    private bool _canStop;

    public BreathingViewModel(TimeProvider clock)
    {
        _clock = clock;
        Durations = new[] { 1, 3, 5, 10 }.Select(minutes => new Choice<int>(minutes, UiText.Format("Minutes", minutes))).ToArray();
        _selectedDuration = Durations[0];
        _phase = UiText.Get("BreathReady");
        StartCommand = new RelayCommand(Start, () => CanConfigure && IsPacingValid);
        PauseResumeCommand = new RelayCommand(PauseResume, () => _state is ExerciseState.Running or ExerciseState.Paused);
        RestartCommand = new RelayCommand(() => { _session?.Restart(); Refresh(); }, () => _session is not null);
        StopCommand = new RelayCommand(() => { _session?.Stop(); Refresh(); }, () => _state != ExerciseState.Ready);
        Tick();
    }

    public IReadOnlyList<Choice<int>> Durations { get; }
    public Choice<int> SelectedDuration
    {
        get => _selectedDuration;
        set
        {
            if (value is not null && SetProperty(ref _selectedDuration, value))
                Refresh();
        }
    }
    public double InhaleSeconds
    {
        get => _inhaleSeconds;
        set => SetPacing(ref _inhaleSeconds, value, nameof(InhaleSeconds));
    }
    public double ExhaleSeconds
    {
        get => _exhaleSeconds;
        set => SetPacing(ref _exhaleSeconds, value, nameof(ExhaleSeconds));
    }
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            if (SetProperty(ref _reducedMotion, value))
            {
                OnPropertyChanged(nameof(MotionEnabled));
                Refresh();
            }
        }
    }
    public bool SystemReducedMotion
    {
        get => _systemReducedMotion;
        set
        {
            if (SetProperty(ref _systemReducedMotion, value))
            {
                OnPropertyChanged(nameof(MotionEnabled));
                Refresh();
            }
        }
    }
    public bool HighContrast { get => _highContrast; set => SetProperty(ref _highContrast, value); }
    public bool MotionEnabled => !ReducedMotion && !SystemReducedMotion;
    public bool CanConfigure => _state is ExerciseState.Ready or ExerciseState.Completed;
    public bool IsRunning => _state == ExerciseState.Running;
    public bool IsPacingValid => double.IsFinite(InhaleSeconds) && double.IsFinite(ExhaleSeconds)
        && InhaleSeconds is >= 2 and <= 20 && ExhaleSeconds is >= 2 and <= 20;
    public string PauseResumeLabel => UiText.Get(_state == ExerciseState.Paused ? "Resume" : "Pause");
    public string Phase => _phase;
    public string Remaining => _remaining;
    public double Scale => _scale;
    public IRelayCommand StartCommand { get; }
    public IRelayCommand PauseResumeCommand { get; }
    public IRelayCommand RestartCommand { get; }
    public IRelayCommand StopCommand { get; }

    private void Start()
    {
        var state = _session?.GetSnapshot().State ?? ExerciseState.Ready;
        if (state is not (ExerciseState.Ready or ExerciseState.Completed) || !IsPacingValid)
        {
            Refresh();
            return;
        }

        _session = new BreathingSession(_clock, TimeSpan.FromSeconds(InhaleSeconds),
            TimeSpan.FromSeconds(ExhaleSeconds), TimeSpan.FromMinutes(SelectedDuration.Value));
        _session.Start();
        Refresh();
    }

    private void PauseResume()
    {
        var session = _session;
        var state = session?.GetSnapshot().State;
        if (state == ExerciseState.Running)
        {
            PauseRunningSession(session!);
            return;
        }

        if (state == ExerciseState.Paused)
            session!.Resume();
        Refresh();
    }

    public void PauseWhenHidden()
    {
        var session = _session;
        if (session?.GetSnapshot().State == ExerciseState.Running)
        {
            PauseRunningSession(session);
            return;
        }

        Refresh();
    }

    public void Tick()
    {
        if (!_isRefreshing)
            Refresh();
    }

    private void PauseRunningSession(BreathingSession session)
    {
        try
        {
            session.Pause();
        }
        catch (InvalidOperationException) when (session.GetSnapshot().State == ExerciseState.Completed)
        {
            // The monotonic deadline can pass between checking Running and calling Pause.
            Refresh();
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        _refreshRequested = true;
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            while (_refreshRequested)
            {
                _refreshRequested = false;
                PublishSnapshot();
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void PublishSnapshot()
    {
        var snapshot = _session?.GetSnapshot();
        var state = snapshot?.State ?? ExerciseState.Ready;
        var phase = UiText.Get(state == ExerciseState.Paused ? "BreathPaused" : $"Breath{snapshot?.Phase ?? BreathPhase.Ready}");
        var remaining = snapshot is null || state == ExerciseState.Ready
            ? TimeSpan.FromMinutes(SelectedDuration.Value) : snapshot.TotalRemaining;
        var remainingText = UiText.Format("BreathRemaining",
            $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}",
            Math.Ceiling(snapshot?.PhaseRemaining.TotalSeconds ?? 0));
        var scale = ReducedMotion || SystemReducedMotion ? 1 : 0.76 + 0.24 * (snapshot?.Expansion ?? 0);
        var wasConfigurable = CanConfigure;
        var wasRunning = IsRunning;
        var wasPaused = _state == ExerciseState.Paused;
        var phaseChanged = phase != _phase;
        var remainingChanged = remainingText != _remaining;
        var scaleChanged = scale != _scale;

        // Publish a coherent snapshot before callbacks; reentrant commands request a fresh pass.
        _state = state;
        _phase = phase;
        _remaining = remainingText;
        _scale = scale;

        if (wasConfigurable != CanConfigure)
            OnPropertyChanged(nameof(CanConfigure));
        if (wasRunning != IsRunning)
            OnPropertyChanged(nameof(IsRunning));
        if (wasPaused != (_state == ExerciseState.Paused))
            OnPropertyChanged(nameof(PauseResumeLabel));
        if (phaseChanged)
            OnPropertyChanged(nameof(Phase));
        if (remainingChanged)
            OnPropertyChanged(nameof(Remaining));
        if (scaleChanged)
            OnPropertyChanged(nameof(Scale));
        NotifyCommands();
    }

    private void SetPacing(ref double field, double value, string propertyName)
    {
        var wasValid = IsPacingValid;
        if (!SetProperty(ref field, value, propertyName))
            return;

        if (wasValid != IsPacingValid)
            OnPropertyChanged(nameof(IsPacingValid));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        NotifyCommand(StartCommand, ref _canStart, CanConfigure && IsPacingValid);
        NotifyCommand(PauseResumeCommand, ref _canPauseResume, _state is ExerciseState.Running or ExerciseState.Paused);
        NotifyCommand(RestartCommand, ref _canRestart, _session is not null);
        NotifyCommand(StopCommand, ref _canStop, _state != ExerciseState.Ready);
    }

    private static void NotifyCommand(IRelayCommand command, ref bool previous, bool current)
    {
        if (previous == current)
            return;

        previous = current;
        command.NotifyCanExecuteChanged();
    }
}
