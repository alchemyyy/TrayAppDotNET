namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Step-counter linear-interpolation state machine for the stereo peak meter. Composed by
/// <see cref="AudioDevice"/> and <see cref="AudioSession"/> so the lerp state plus its
/// OnNewSample / OnRenderTick body lives in one place. Mutable struct - hosts store as a field
/// and mutate in place; pass by ref to helpers (pass by value clones state silently).
///
/// Thread model: <see cref="WriteRawPeaks"/> and <see cref="PinRawPeaksToSilence"/> may run on
/// either the bg sample thread or the dispatcher (dispatcher-side lifecycle pins). Float writes
/// are atomic and the UI only consumes _raw* in OnNewSample, so torn intermediate states never
/// reach DisplayMin / DisplayMax. OnNewSample / OnRenderTick / DisplayMin / DisplayMax are
/// UI-thread only.
/// </summary>
internal struct MeterLerp
{
    // Bg-thread writes these from one IAudioMeterInformation call (min and max over the first
    // two channels). The dispatched UI half copies them into the _target* fields below.
    private float _rawPeakMin, _rawPeakMax;

    // Display fields backing the bound PeakValueMin / PeakValueMax. Mutated only in OnRenderTick.
    private float _displayPeakMin, _displayPeakMax;

    // Lerp origins captured at the start of each sample interval.
    private float _prevPeakMin, _prevPeakMax;

    // Lerp targets - the most recent _raw* values copied across in OnNewSample.
    private float _targetPeakMin, _targetPeakMax;

    // Shared step counter. Both min and max advance in lockstep across _interpolationSteps frames.
    private int _interpolationStep;
    private int _interpolationSteps;

    /// <summary>Current smoothed min(L, R) peak. UI-thread read; never set externally.</summary>
    public readonly float DisplayMin => _displayPeakMin;

    /// <summary>Current smoothed max(L, R) peak. UI-thread read; never set externally.</summary>
    public readonly float DisplayMax => _displayPeakMax;

    /// <summary>
    /// Bg-thread write of the most recently sampled raw peaks. The host has already called
    /// <see cref="MeterReader.ReadStereoPeaks"/> and applied any unified-mode collapse; this
    /// helper just stashes the values for the next OnNewSample dispatch.
    /// </summary>
    public void WriteRawPeaks(float min, float max)
    {
        _rawPeakMin = min;
        _rawPeakMax = max;
    }

    /// <summary>
    /// UI-thread sample-arm. Snapshots the current display values as the new lerp origins,
    /// copies the most recent raw peaks into the target fields, and resets the step counter to
    /// span <paramref name="interpolationSteps"/> render frames (clamped to at least 1).
    /// </summary>
    public void OnNewSample(int interpolationSteps)
    {
        _prevPeakMin = _displayPeakMin;
        _prevPeakMax = _displayPeakMax;
        _targetPeakMin = _rawPeakMin;
        _targetPeakMax = _rawPeakMax;
        _interpolationStep = 0;
        _interpolationSteps = interpolationSteps < 1 ? 1 : interpolationSteps;
    }

    /// <summary>
    /// UI-thread render-tick. Computes the natural lerp value for this tick, then advances
    /// <c>_displayPeak*</c> toward it by at most <paramref name="maxStep"/>. The rate limit
    /// lives here (not in VolumeSlider) so it runs on every render frame whether or not the
    /// display value changed - if it lived downstream of PropertyChanged it would only advance
    /// when the lerp moved, which means once the lerp converged the rate-limited rendered value
    /// could be stuck arbitrarily far from the real target. <paramref name="minChanged"/> /
    /// <paramref name="maxChanged"/> are true iff the corresponding display field actually moved,
    /// so the host can fire PropertyChanged only when there's a real update to bind.
    /// <paramref name="maxStep"/> of 0 freezes the meter (matches the user-facing
    /// MeterPeakChangeCeiling=0 setting).
    /// </summary>
    public void OnRenderTick(float maxStep, out bool minChanged, out bool maxChanged)
    {
        _interpolationStep++;

        float lerpedMin, lerpedMax;
        if (_interpolationStep >= _interpolationSteps)
        {
            // Reached or passed the targets - snap so a long render-only burst (e.g. paused
            // samples) can't drift past the most recent sample.
            lerpedMin = _targetPeakMin;
            lerpedMax = _targetPeakMax;
        }
        else
        {
            float t = (float)_interpolationStep / _interpolationSteps;
            lerpedMin = _prevPeakMin + (_targetPeakMin - _prevPeakMin) * t;
            lerpedMax = _prevPeakMax + (_targetPeakMax - _prevPeakMax) * t;
        }

        float newMin = StepTowardTarget(_displayPeakMin, lerpedMin, maxStep);
        float newMax = StepTowardTarget(_displayPeakMax, lerpedMax, maxStep);

        minChanged = newMin != _displayPeakMin;
        maxChanged = newMax != _displayPeakMax;
        _displayPeakMin = newMin;
        _displayPeakMax = newMax;
    }

    // Move current toward target by up to maxStep, snapping the final fractional step so the
    // display lands exactly on target instead of oscillating around it. maxStep=0 returns
    // current unchanged (meter frozen by user setting).
    private static float StepTowardTarget(float current, float target, float maxStep)
    {
        float delta = target - current;
        if (delta > maxStep) return current + maxStep;
        if (delta < -maxStep) return current - maxStep;
        return target;
    }

    /// <summary>
    /// Forces the raw peaks to 0 so the next OnNewSample lerps smoothly to silence. Used by the
    /// background sample gate and by dispatcher-side stream lifecycle events.
    /// </summary>
    public void PinRawPeaksToSilence()
    {
        _rawPeakMin = 0f;
        _rawPeakMax = 0f;
    }
}
