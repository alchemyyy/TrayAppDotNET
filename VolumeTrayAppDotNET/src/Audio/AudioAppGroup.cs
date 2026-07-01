using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;
using VolumeTrayAppDotNET.Interop;


namespace VolumeTrayAppDotNET.Audio;

// Aggregates every audio session that shares an AppID into a single slider, mirroring EarTrumpet's
// AudioDeviceSessionGroup. Discord, Chromium-based browsers, and Electron apps spawn several child
// processes that each register their own IAudioSessionControl with WASAPI; without grouping, the
// flyout would show two or three sliders for one app.
//
// The group exposes the same bindable surface area as a single AudioSession (DisplayName, Icon,
// Volume, IsMuted, PeakValue, State) so the flyout DataTemplate can bind to either type. Volume
// and IsMuted writes fan out to every session in the group; reads return the first session's value.
// PeakValue is the max across all sessions so the loudest stream drives the meter.
internal sealed class AudioAppGroup(string appID, Dispatcher dispatcher) : INotifyPropertyChanged, IDisposable
{
    // Aggregate-peak quantization: per-session lerps already fire on float-level deltas, so the
    // group bar's "did anything change" gate sits at 0.001 (~half a pixel for a 0..1 meter).
    // Group bar can lag a per-session bar by up to this on micro-twitches; intentional.
    private const float PeakAggregateEpsilon = 0.001f;

    private readonly List<AudioSession> _sessions = [];

    // Bg-thread-readable snapshot. UI-thread Add/RemoveSession rebuilds this so the bg sample
    // tick reads the field once per pass without allocating an AudioSession[] each time.
    private volatile AudioSession[] _sessionsSnapshot = [];
    private readonly Dispatcher _dispatcher = dispatcher;

    private float _peakValueMin;
    private float _peakValueMax;
    private bool _isPeakMeterVisible;
    private bool _batchingSessionPeakUpdates;
    private bool _aggregatePeakDirty;
    private bool _applyingVolume;
    private bool _applyingMute;
    private bool _disposed;

    public string AppID { get; } = appID;

    /// <summary>The sessions inside this group. Mutated only on the UI thread by AudioDevice.</summary>
    public IReadOnlyList<AudioSession> Sessions => _sessions;

    public string DisplayName => _sessions.Count > 0 ? _sessions[0].DisplayName : AudioLocalization.UnknownAppName;
    public IImage? Icon => _sessions.Count > 0 ? _sessions[0].Icon : null;
    public bool IsSystemSounds => _sessions.Count > 0 && _sessions[0].IsSystemSounds;
    public uint ProcessID => _sessions.Count > 0 ? _sessions[0].ProcessID : 0;

    // Tooltip surface for the per-app icon. Computed (rather than MultiBinding in XAML) so the
    // binding stays a plain Path="TooltipText" - matches the rest of the bindable surface and
    // avoids quirks WPF has resolving MultiBindings against internal types.
    public string TooltipText => _sessions.Count > 0
        ? AudioLocalization.AppTooltip(_sessions[0].DisplayName, _sessions[0].ProcessID)
        : AudioLocalization.UnknownAppName;

    /// <summary>Active if any session in the group is active; expired only when every session has expired.</summary>
    public AudioSessionState State
    {
        get
        {
            bool isInactive = false;
            foreach (AudioSession session in _sessions)
            {
                if (session.State == AudioSessionState.Active)
                    return AudioSessionState.Active;
                if (session.State != AudioSessionState.Expired)
                    isInactive = true;
            }

            return isInactive ? AudioSessionState.Inactive : AudioSessionState.Expired;
        }
    }

    public float Volume
    {
        get => _sessions.Count > 0 ? _sessions[0].Volume : 0f;
        set
        {
            // Fan out to every session. Each AudioSession.Volume.set already filters near-equal writes
            // and tolerates COM failures, so no extra guard is needed here.
            _applyingVolume = true;
            try
            {
                foreach (AudioSession session in _sessions)
                    session.Volume = value;
            }
            finally
            {
                _applyingVolume = false;
            }

            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => _sessions.Count > 0 && _sessions[0].IsMuted;
        set
        {
            _applyingMute = true;
            try
            {
                foreach (AudioSession session in _sessions)
                    session.IsMuted = value;
            }
            finally
            {
                _applyingMute = false;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Coalesced peak payload for the WPF meter binding.</summary>
    public MeterPeakValues PeakValues => new(_peakValueMin, _peakValueMax);

    /// <summary>
    /// True when one of this group's sessions is the process currently holding the parent device
    /// in exclusive mode. Drives the mini-glyph lock overlay on the app icon. Backend stub:
    /// AudioDevice pushes this true when its <see cref="AudioDevice.ExclusiveControlHolderPID"/>
    /// matches any session's PID. Until that detection lands the flag stays false.
    /// </summary>
    public bool IsExclusiveControlHolder
    {
        get;
        internal set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True only while this group is represented by a visible app drawer. Device meters continue
    /// updating regardless; this gate keeps hidden per-app session meters out of the hot loop.
    /// </summary>
    internal bool IsPeakMeterVisible
    {
        get => _isPeakMeterVisible;
        set => _isPeakMeterVisible = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the last session is removed so AudioDevice can drop the group from its list.</summary>
    internal event Action<AudioAppGroup>? Empty;

    /// <summary>
    /// Adds a session to the group. New sessions inherit the group's existing mute state so a freshly
    /// spawned Discord renderer doesn't unmute an app that the user had silenced.
    /// </summary>
    public void AddSession(AudioSession session)
    {
        if (_sessions.Count > 0)
        {
            // Inherit current mute state. AudioSession.IsMuted.set guards against echo and COM failure
            // internally, so a best-effort write is safe.
            try { session.IsMuted = _sessions[0].IsMuted || session.IsMuted; }
            catch
            {
                /* session may already be torn down */
            }
        }

        _sessions.Add(session);
        _sessionsSnapshot = [.. _sessions];
        session.PropertyChanged += OnSessionPropertyChanged;

        // First session populates the bindable surface; subsequent ones don't change the
        // representative-derived properties so re-emitting them on every add wastes binding work.
        if (_sessions.Count == 1)
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(ProcessID));
            OnPropertyChanged(nameof(TooltipText));
        }
    }

    /// <summary>
    /// Removes a session from the group. Raises <see cref="Empty"/> when the last session leaves so
    /// the device can prune the group; otherwise re-emits property change for any representative-derived
    /// fields since the head session may have shifted.
    /// </summary>
    public void RemoveSession(AudioSession session)
    {
        if (!_sessions.Remove(session)) return;
        _sessionsSnapshot = [.. _sessions];
        session.PropertyChanged -= OnSessionPropertyChanged;

        if (_sessions.Count == 0)
        {
            Empty?.Invoke(this);
            return;
        }

        // Representative session (index 0) may have changed; refresh anything keyed off it.
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(ProcessID));
        OnPropertyChanged(nameof(TooltipText));
        RefreshAggregatePeak();
    }

    /// <summary>
    /// Bg-thread fan-out. Reads the published session snapshot (rebuilt by UI-thread Add/Remove)
    /// and forwards the COM-read into every session so per-session raw peaks are populated in
    /// parallel off the UI thread.
    /// The (unified, biasMultiplier) pair flows down unchanged so per-session bars collapse in
    /// lockstep with the device bar when unified mode is on.
    /// </summary>
    internal void UpdatePeakValueBackground(bool unified, int biasMultiplier)
    {
        if (_disposed || !_isPeakMeterVisible) return;

        AudioSession[] sessions = _sessionsSnapshot;

        for (int i = 0; i < sessions.Length; i++)
        {
            try { sessions[i].UpdatePeakValueBackground(unified, biasMultiplier); }
            catch
            {
                /* session may have died between callbacks */
            }
        }
    }

    /// <summary>
    /// Sample-timer fan-out (UI thread). Forwards into every session so each session arms its
    /// own lerp from the latest cached raw peak. The group's own <see cref="PeakValue"/> doesn't
    /// interpolate - it just maxes over the sessions, which are already smoothed individually.
    /// </summary>
    internal void OnNewSample(int interpolationSteps)
    {
        if (_disposed || !_isPeakMeterVisible) return;
        for (int i = _sessions.Count - 1; i >= 0; i--) _sessions[i].OnNewSample(interpolationSteps);
    }

    /// <summary>
    /// Render-timer fan-out. Each session's render tick fires PeakValue PropertyChanged when it
    /// shifts, which OnSessionPropertyChanged observes to recompute the group max - so this single
    /// pass is enough to keep both per-session and per-group meters smooth.
    /// </summary>
    internal void OnRenderTick(float maxStep)
    {
        if (_disposed || !_isPeakMeterVisible) return;

        _batchingSessionPeakUpdates = true;
        try
        {
            for (int i = _sessions.Count - 1; i >= 0; i--) _sessions[i].OnRenderTick(maxStep);
        }
        finally
        {
            _batchingSessionPeakUpdates = false;
        }

        if (!_aggregatePeakDirty) return;
        _aggregatePeakDirty = false;
        RefreshAggregatePeak();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-emit representative-derived properties so the UI rebinds when an underlying session
        // mutates (volume changed via Windows Volume Mixer, icon path updated by Discord, etc.).
        switch (e.PropertyName)
        {
            case nameof(AudioSession.Volume):
                if (!_applyingVolume && ReferenceEquals(sender, _sessions.Count > 0 ? _sessions[0] : null))
                    OnPropertyChanged(nameof(Volume));
                break;
            case nameof(AudioSession.IsMuted):
                if (!_applyingMute && ReferenceEquals(sender, _sessions.Count > 0 ? _sessions[0] : null))
                    OnPropertyChanged(nameof(IsMuted));
                break;
            case nameof(AudioSession.PeakValues):
                if (_batchingSessionPeakUpdates) _aggregatePeakDirty = true;
                else RefreshAggregatePeak();
                break;
            case nameof(AudioSession.Icon):
                if (ReferenceEquals(sender, _sessions.Count > 0 ? _sessions[0] : null))
                    OnPropertyChanged(nameof(Icon));
                break;
            case nameof(AudioSession.DisplayName):
                if (ReferenceEquals(sender, _sessions.Count > 0 ? _sessions[0] : null))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(TooltipText));
                }

                break;
            case nameof(AudioSession.State):
                OnPropertyChanged(nameof(State));
                break;
        }
    }

    private void RefreshAggregatePeak()
    {
        float maxOfMins = 0f;
        float maxOfMaxes = 0f;
        foreach (AudioSession session in _sessions)
        {
            MeterPeakValues peaks = session.PeakValues;
            if (peaks.Min > maxOfMins) maxOfMins = peaks.Min;
            if (peaks.Max > maxOfMaxes) maxOfMaxes = peaks.Max;
        }

        SetPeakValues(maxOfMins, maxOfMaxes);
    }

    private void SetPeakValues(float min, float max)
    {
        bool minChanged = Math.Abs(min - _peakValueMin) > PeakAggregateEpsilon;
        bool maxChanged = Math.Abs(max - _peakValueMax) > PeakAggregateEpsilon;
        if (!minChanged && !maxChanged) return;

        _peakValueMin = min;
        _peakValueMax = max;
        OnPropertyChanged(nameof(PeakValues));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (AudioSession session in _sessions)
            session.PropertyChanged -= OnSessionPropertyChanged;

        _sessions.Clear();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
