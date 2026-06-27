using Microsoft.Win32;
using TrayAppDotNETCommon.Services;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Access layer for Windows Night Light via the CloudStore registry blobs. Format is Microsoft Bond
/// CompactBinary v1 (CB v1) inside a CloudStore envelope:
/// <c>4B magic ("CB",1,0) * 6B header * varint Unix-seconds timestamp
/// * 3B inner-prefix * 1B inner length * inner CB struct * trailing zero-stops</c>.
///
/// Inner STATE struct fields:
/// <list type="bullet">
///   <item>0 = INT32 (presence = ON)</item>
///   <item>10 = INT32 = 1 (initialized)</item>
///   <item>20 = UINT64 FILETIME (last-transition)</item>
/// </list>
/// Inner SETTINGS struct: field 40 = INT16 kelvin, plus schedule sub-structs.
///
/// Fields are located by Bond tag rather than fixed offsets so schedule edits don't shift our reads. There is
/// no hash/signature - Windows trusts the blob - but on 24H2/26200 the BlueLightReduction service has a
/// regression where settings-only writes aren't applied to the live filter (Microsoft's own UI is also
/// affected). The workaround, used here, is to follow every settings write with a state-blob rewrite that
/// bumps the inner FILETIME, mirroring what Windows itself does on every toggle. That triggers the service's
/// full re-read without flicker.
/// </summary>
internal static class NightLightRegistry
{
    // Latest-pending-wins scheduler for fire-and-forget callers (slider drag, env curve). Cooldown is zero:
    // the bottleneck is the registry I/O itself (~5ms per round-trip), and the throttler's job here is
    // single-flight + intermediate-collapse, not rate-limiting. Synchronous callers (tests,
    // EnsureNonZeroStrengthBeforeEnable's pre-enable write) still call the sync SetStrength directly so they
    // observe the readback.
    private const string ThrottlerKey = "nightlight";
    private static readonly AsyncThrottler<string> _throttler = new(0, StringComparer.Ordinal);

    private const string CloudStoreCurrentPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\";

    private const string StateKeyPath =
        CloudStoreCurrentPath
        + @"default$windows.data.bluelightreduction.bluelightreductionstate\"
        + "windows.data.bluelightreduction.bluelightreductionstate";

    private const string SettingsKeyPath =
        CloudStoreCurrentPath
        + @"default$windows.data.bluelightreduction.settings\"
        + "windows.data.bluelightreduction.settings";

    private const string DataValueName = "Data";

    private static readonly byte[] OuterMagic = [0x43, 0x42, 0x01, 0x00];
    private static readonly byte[] OuterHeader = [0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06];
    private static readonly byte[] OuterInnerPrefix = [0x2A, 0x2B, 0x0E];
    private static readonly byte[] EnabledMarker = [0x10, 0x00]; // STATE inner field 0 INT32=0 (presence = ON)
    private static readonly byte[] TempTag = [0xCF, 0x28]; // SETTINGS inner field 40 INT16 marker

    private static readonly byte[] FileTimeTag = [0xC6, 0x14]; // STATE inner field 20 UINT64 marker

    // SETTINGS inner field 70 BOOL marker (IsDragging). 0xC2 = (wide-encoding-prefix 0b110 << 5) | T_BOOL(2).
    // Captured empirically by ChainProbeTester Probe F. Bond CB v1 omits this field entirely when it equals
    // its default (false), so it only appears in the blob when IsDragging is true.
    private static readonly byte[] IsDraggingTag = [0xC2, 0x46];

    private const string CallerName = "NightLightRegistry";

    public static bool IsSupported()
    {
        try
        {
            byte[]? state = ReadBlob(StateKeyPath);
            byte[]? settings = ReadBlob(SettingsKeyPath);
            return state is not null
                   && settings is not null
                   && TryParseOuter(state, out _)
                   && TryParseOuter(settings, out _);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsEnabled()
    {
        byte[]? blob = ReadBlob(StateKeyPath);
        return blob is not null
               && TryParseOuter(blob, out OuterLayout layout)
               && HasEnabledMarker(blob, layout);
    }

    /// <summary>Strength 0 (no tint) to 100 (max warmth).</summary>
    public static int GetStrength()
    {
        byte[]? blob = ReadBlob(SettingsKeyPath);
        if (blob is null || !TryParseOuter(blob, out OuterLayout layout)) return 0;

        int tempPos = FindTagValueStart(blob, layout, TempTag);
        if (tempPos < 0 || tempPos + 1 >= layout.InnerEnd) return 0;

        // Bond ZigZag+varint INT16 reverse:
        //   - drop the continuation bit on byte 0
        //   - splice byte 1's payload above it
        //   - ZigZag-decode (>> 1 since kelvin is positive, so the sign bit is always clear)
        // uint keeps the right shift logical - a malformed blob with byte 0 < 0x80 would otherwise
        // arithmetic-shift in sign bits and produce garbage.
        uint lo = blob[tempPos];
        uint hi = blob[tempPos + 1];
        uint zigzag = (lo & 0x7Fu) | (hi << 7);
        int kelvin = (int)(zigzag >> 1);
        return NightLightKelvin.KelvinToPercent(kelvin);
    }

    public static bool SetEnabled(bool enabled) => IsEnabled() == enabled || Toggle();

    /// <summary>
    /// Flips the enabled-flag presence (STATE inner field 0) and bumps the inner FILETIME (field 20) to "now",
    /// mirroring what Windows itself does in <c>Windows.Shell.BlueLightReduction.dll!State::save</c>. The
    /// FILETIME bump is what makes the BlueLightReduction service treat the write as a real transition.
    /// Returns true if the post-write enabled state matches the intended flip.
    /// </summary>
    public static bool Toggle()
    {
        byte[]? blob = ReadBlob(StateKeyPath);
        if (blob is null || !TryParseOuter(blob, out OuterLayout layout)) return false;

        bool wasEnabled = HasEnabledMarker(blob, layout);
        byte[] inner = Slice(blob, layout.InnerStart, layout.InnerLength);
        byte[] toggled = wasEnabled
            ? RemoveEnabledMarker(inner)
            : InsertEnabledMarker(inner);

        byte[] freshened = WithFreshFileTime(toggled);
        WriteBlob(StateKeyPath, RebuildOuter(blob, layout, freshened));

        bool nowEnabled = IsEnabled();
        return nowEnabled == !wasEnabled;
    }

    /// <summary>
    /// Writes the new strength to the SETTINGS blob, then bumps the STATE blob's inner FILETIME to "now". The
    /// state-blob rewrite is what forces Windows' live filter to re-evaluate against the new strength on
    /// builds where settings-only writes are silently ignored. Enabled state is preserved (we only touch the
    /// FILETIME). Returns true if the strength read back matches what we wrote within +/-1% rounding
    /// tolerance.
    /// </summary>
    public static bool SetStrength(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        WriteKelvin(NightLightKelvin.PercentToKelvin(percent));
        RefreshStateFileTime();

        int observed = GetStrength();
        return Math.Abs(observed - percent) <= 1;
    }

    /// <summary>
    /// Throttled fire-and-forget version of <see cref="SetStrength"/> for hot-path callers (slider drag, env
    /// curve). Returns immediately - the actual registry I/O runs on a thread pool worker. Concurrent calls
    /// collapse via latest-pending-wins, so a 60Hz drag produces at most one in-flight write plus one queued,
    /// with intermediates dropped before they hit the registry.
    ///
    /// Pulses (state-flip recovery) run inside the same throttled payload when
    /// <paramref name="pulseAfterIfEnabled"/> is true and night light is currently on, so the pulse always
    /// trails the strength write rather than racing it.
    /// </summary>
    public static void EnqueueSetStrength(int percent, bool pulseAfterIfEnabled)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        _ = _throttler.RunAsync(ThrottlerKey, _ => Task.Run(() =>
        {
            SetStrength(clamped);
            if (pulseAfterIfEnabled && IsEnabled())
                PulseApply();
        }));
    }

    /// <summary>
    /// Throttled fire-and-forget no-flicker bracket via raw registry writes only - the same IsDragging-edge
    /// trick the SettingsHandler bracket uses, but bypassing the <c>SettingsHandlers_Display</c> RVA dance
    /// entirely. Sequence:
    /// <list type="number">
    ///   <item>Save #1: SETTINGS blob with kelvin updated, IsDragging absent (default false).</item>
    ///   <item>Wait for the registry write to land via <c>RegNotifyChangeKeyValue</c>.</item>
    ///   <item>Save #2: SETTINGS blob with IsDragging tag inserted (true). The broker observes the
    ///         false-&gt;true edge as a real state change and queues <c>ColorTemperatureControl::fb3daf</c>,
    ///         which calls <c>ApplyTemperatureChangeToMonitorsImmediate</c> unconditionally - bypassing the
    ///         wedged <c>+36 inflight</c> gate that would otherwise silently drop kelvin-only updates.</item>
    ///   <item>Wait for the write to land.</item>
    ///   <item>Save #3: IsDragging tag removed (back to default). With active=true the broker runs telemetry
    ///         only on this edge - no Clear, no flicker.</item>
    /// </list>
    ///
    /// Whether the broker actually receives these writes depends on whether direct registry writes bump the
    /// CloudStore version counter (or whether something else - the broker's own
    /// <c>RegNotifyChangeKeyValue</c> subscription, periodic polling, etc. - propagates them). Empirically
    /// <see cref="PulseApply"/> works most of the time, so the path exists; this method just exercises it
    /// without the visible flicker.
    /// </summary>
    // Belt-and-suspenders re-fire: if no further EnqueueSetStrengthSpaced call comes in for
    // NightLightUIHandleryRegistryEnforceDelayMs after the latest one, fire one more bracket at the same value.
    // Without an
    // event signal that the broker actually applied a given write, this guards against the case where a
    // single bracket gets dropped (broker missed the IsDragging edge, raced with another process, etc).
    // One-shot per quiet period - the re-fire itself does NOT schedule another to avoid an infinite loop.
    //
    // Implemented as a single reusable System.Threading.Timer whose dueTime gets reset on every call
    // (Timer.Change is thread-safe and effectively free). Earlier implementation allocated a fresh
    // CancellationTokenSource per call and threw OperationCanceledException for every supersession - at
    // slider rates of 60+/sec that produced enough kernel-handle and exception churn to ripple into UI
    // hitches even though all the work happened on the thread pool.
    private static int _lastResettlePercent;

    private static Timer? _resendTimer;

    /// <summary>Fire-and-forget enqueue of a kelvin update; bracket runs on the thread pool.</summary>
    public static void EnqueueSetStrengthSpaced(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        // Push ALL of the entry-side work to the thread pool: CTS create/cancel/dispose for the resend timer,
        // throttler dictionary/lock ops, and the synchronous prefix of the throttler driver. At slider rates
        // (60+/sec) the cumulative microsecond cost of CancellationTokenSource churn alone has caused UI
        // hitches; pushing the lot off the UI thread eliminates that path entirely.
        _ = Task.Run(() =>
        {
            SchedulePostSettleResend(clamped);
            return _throttler.RunAsync(
                ThrottlerKey,
                ctx => SetStrengthSpacedAsync(clamped, ctx));
        });
    }

    /// <summary>
    /// Updates the value the resend will fire with and re-arms the resend timer to fire
    /// NightLightUIHandleryRegistryEnforceDelayMs from now.
    /// If <see cref="EnqueueSetStrengthSpaced"/> is called again before
    /// the timer fires, the timer is reset and the previously scheduled fire never happens. Allocations per
    /// call: zero (Timer is reused; int field is volatile-written).
    /// </summary>
    private static void SchedulePostSettleResend(int percent)
    {
        Volatile.Write(ref _lastResettlePercent, percent);

        Timer? timer = _resendTimer;
        if (timer == null)
        {
            // First-call lazy init. CompareExchange resolves the (rare) creation race so we never end up
            // with two timers; the loser disposes its candidate.
            Timer candidate = new(
                OnResendTimerFired, state: null, Timeout.Infinite, Timeout.Infinite);
            timer = Interlocked.CompareExchange(ref _resendTimer, candidate, null) ?? candidate;
            if (!ReferenceEquals(timer, candidate)) candidate.Dispose();
        }

        // Re-arm: fire once, NightLightUIHandleryRegistryEnforceDelayMs from now.
        // Cancels any pending fire from prior call.
        // Timer.Change is thread-safe and effectively free.
        timer.Change(TimeConstants.NightLightUIHandleryRegistryEnforceDelayMs, Timeout.Infinite);
    }

    private static void OnResendTimerFired(object? state)
    {
        // System.Threading.Timer callbacks run on a thread pool thread. An unhandled exception escaping the
        // callback tears down the process (timer faults are uncatchable from the caller). Belt-and-suspenders
        // catch-all: log and swallow so a transient throw doesn't crash the app.
        try
        {
            int percent = Volatile.Read(ref _lastResettlePercent);
            // Already on a thread pool thread. Same throttler key as user calls, so a real user call that
            // slips in between the timer firing and the throttler picking us up wins via latest-pending-wins.
            _ = _throttler.RunAsync(
                ThrottlerKey,
                ctx => SetStrengthSpacedAsync(percent, ctx));
        }
        catch (Exception ex)
        {
            WPFLog.Log($"NightLightRegistry.OnResendTimerFired: {ex}");
        }
    }

    /// <summary>
    /// Cancels any pending post-settle resend so the next callback never fires until a fresh
    /// <see cref="EnqueueSetStrengthSpaced"/> arms it again. Used by the auto-off-at-zero path to make sure
    /// the resend doesn't race against an off-state - the resend's bracket assumes on-state semantics.
    /// </summary>
    public static void CancelPendingResend()
    {
        Timer? timer = _resendTimer;
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static async Task SetStrengthSpacedAsync(int percent, ThrottlerContext ctx)
    {
        int targetKelvin = NightLightKelvin.PercentToKelvin(percent);

        // Each step: write SETTINGS, wait for our own RegNotify (confirms write landed), then sleep
        // NightLightInterWriteDelayMs to give the broker time to react before the next write. The post-write
        // sleep is heuristic - raw registry writes bypass CloudStore so there's no in-process signal that the
        // broker has been notified of our out-of-band write.

        // Step 1: write kelvin (IsDragging absent, default false). Broker may silently drop due to wedged
        // inflight, but the kelvin lands on disk for the next two saves to use.
        await AsyncUtils.IssueWithSaveNotifyAsync(
                SettingsKeyPath,
                () => WriteSettingsBlob(targetKelvin, dragging: false),
                TimeConstants.NightLightSaveNotifyTimeoutMs, TimeConstants.NightLightFallbackDwellMs, CallerName)
            .ConfigureAwait(false);
        if (ctx.HasReplacement) return;
        await Task.Delay(TimeConstants.NightLightInterWriteDelayMs).ConfigureAwait(false);
        if (ctx.HasReplacement) return;

        // Step 2: insert IsDragging tag. False->true edge triggers fb3daf -> Apply(target).
        await AsyncUtils.IssueWithSaveNotifyAsync(
                SettingsKeyPath,
                () => WriteSettingsBlob(targetKelvin, dragging: true),
                TimeConstants.NightLightSaveNotifyTimeoutMs, TimeConstants.NightLightFallbackDwellMs, CallerName)
            .ConfigureAwait(false);
        if (ctx.HasReplacement) return;
        await Task.Delay(TimeConstants.NightLightInterWriteDelayMs).ConfigureAwait(false);
        if (ctx.HasReplacement) return;

        // Step 3: remove IsDragging tag. True->false edge runs telemetry only when active.
        await AsyncUtils.IssueWithSaveNotifyAsync(
                SettingsKeyPath,
                () => WriteSettingsBlob(targetKelvin, dragging: false),
                TimeConstants.NightLightSaveNotifyTimeoutMs, TimeConstants.NightLightFallbackDwellMs, CallerName)
            .ConfigureAwait(false);
    }

    private static void WriteSettingsBlob(int kelvin, bool dragging)
    {
        byte[]? blob = ReadBlob(SettingsKeyPath);
        if (blob is null || !TryParseOuter(blob, out OuterLayout layout)) return;

        // Mutate kelvin field (existing logic, mirrors TryRebuildSettingsBlobWithKelvin).
        int tempPos = FindTagValueStart(blob, layout, TempTag);
        if (tempPos < 0 || tempPos + 1 >= layout.InnerEnd) return;

        uint zigzag = (uint)kelvin << 1;
        byte lo = (byte)((zigzag & 0x7Fu) | 0x80u);
        byte hi = (byte)(zigzag >> 7);

        byte[] inner = Slice(blob, layout.InnerStart, layout.InnerLength);
        int innerTempPos = tempPos - layout.InnerStart;
        inner[innerTempPos] = lo;
        inner[innerTempPos + 1] = hi;

        byte[] mutated = WithIsDragging(inner, dragging);
        WriteBlob(SettingsKeyPath, RebuildOuter(blob, layout, mutated));
    }

    /// <summary>
    /// Returns a copy of <paramref name="inner"/> with the IsDragging tag (field 70 BOOL) inserted just
    /// before the trailing BT_STOP when <paramref name="dragging"/> is true, or removed if it's already
    /// present and <paramref name="dragging"/> is false. No-op if the inner is malformed or the desired
    /// state is already present.
    /// </summary>
    private static byte[] WithIsDragging(byte[] inner, bool dragging)
    {
        int existingPos = IndexOf(inner, IsDraggingTag, OuterMagic.Length);
        bool currentlyPresent = existingPos >= 0;

        if (dragging == currentlyPresent) return inner;

        if (dragging)
        {
            // Insert "C2 46 01" immediately before the trailing BT_STOP byte.
            if (inner.Length == 0 || inner[^1] != 0x00) return inner;
            byte[] result = new byte[inner.Length + IsDraggingTag.Length + 1];
            Array.Copy(inner, 0, result, 0, inner.Length - 1);
            Array.Copy(IsDraggingTag, 0, result, inner.Length - 1, IsDraggingTag.Length);
            result[inner.Length - 1 + IsDraggingTag.Length] = 0x01; // BOOL value = true
            result[^1] = 0x00; // BT_STOP
            return result;
        }
        else
        {
            // Remove the 3 bytes "C2 46 ??" at existingPos.
            int removeLen = IsDraggingTag.Length + 1;
            if (existingPos + removeLen > inner.Length) return inner;
            byte[] result = new byte[inner.Length - removeLen];
            Array.Copy(inner, 0, result, 0, existingPos);
            Array.Copy(inner, existingPos + removeLen, result, existingPos, inner.Length - existingPos - removeLen);
            return result;
        }
    }

    /// <summary>Kelvin 1200 (warmest) to 6500 (coolest). Values outside the range are clamped.</summary>
    public static void SetTemperature(int kelvin)
    {
        WriteKelvin(Math.Clamp(kelvin, NightLightKelvin.MinKelvin, NightLightKelvin.MaxKelvin));
        RefreshStateFileTime();
    }

    /// <summary>
    /// Forces the live filter to re-evaluate by toggling the state blob off and back on. Two registry
    /// writes, briefly visible as a flicker. Reserved as a manual recovery path for when the silent FILETIME
    /// bump in <see cref="SetStrength"/> somehow isn't enough - should rarely be needed in normal operation.
    /// </summary>
    public static void PulseApply()
    {
        Toggle();
        Toggle();
    }

    private static void WriteKelvin(int kelvin)
    {
        byte[]? rebuilt = TryRebuildSettingsBlobWithKelvin(kelvin);
        if (rebuilt is null) return;
        WriteBlob(SettingsKeyPath, rebuilt);
    }

    /// <summary>
    /// Reads the current SETTINGS blob, mutates the kelvin field in place, and returns the rebuilt outer
    /// envelope WITHOUT writing it back to the registry. Used by <c>NightLightCloudStore</c> to construct
    /// the byte buffer for a direct <c>ICloudStore::Save</c> call - that path bypasses the SHTaskPool
    /// tag-258 dedup that eats most of our writes when we go through <c>SaveSettingsAsync</c>. Returns null
    /// if the registry blob is missing or malformed.
    /// </summary>
    internal static byte[]? BuildSettingsBlobForKelvin(int percent) =>
        TryRebuildSettingsBlobWithKelvin(NightLightKelvin.PercentToKelvin(percent));

    private static byte[]? TryRebuildSettingsBlobWithKelvin(int kelvin)
    {
        byte[]? blob = ReadBlob(SettingsKeyPath);
        if (blob is null || !TryParseOuter(blob, out OuterLayout layout)) return null;

        int tempPos = FindTagValueStart(blob, layout, TempTag);
        if (tempPos < 0 || tempPos + 1 >= layout.InnerEnd) return null;

        // Bond ZigZag+varint INT16. For positive kelvin in [1200,6500] the ZigZag value is just kelvin << 1
        // (sign bit clear -> no XOR-fold), and the varint encoding is always exactly 2 bytes:
        //   - byte 0 carries the low 7 bits with the continuation flag set
        //   - byte 1 carries the upper bits
        // uint here so the shifts are logical and the byte casts are explicit.
        uint zigzag = (uint)kelvin << 1;
        byte lo = (byte)((zigzag & 0x7Fu) | 0x80u);
        byte hi = (byte)(zigzag >> 7);

        byte[] inner = Slice(blob, layout.InnerStart, layout.InnerLength);
        int innerTempPos = tempPos - layout.InnerStart;
        inner[innerTempPos] = lo;
        inner[innerTempPos + 1] = hi;

        return RebuildOuter(blob, layout, inner);
    }

    /// <summary>
    /// Rewrites the STATE blob with a fresh inner FILETIME (field 20 = current Windows file time). Everything
    /// else in the inner is preserved - enabled-flag presence, initialized marker, etc. The outer Unix
    /// timestamp is bumped by RebuildOuter.
    /// </summary>
    private static void RefreshStateFileTime()
    {
        byte[]? blob = ReadBlob(StateKeyPath);
        if (blob is null || !TryParseOuter(blob, out OuterLayout layout)) return;

        byte[] inner = Slice(blob, layout.InnerStart, layout.InnerLength);
        byte[] freshened = WithFreshFileTime(inner);
        // No FILETIME field present - WithFreshFileTime returned the input unchanged.
        if (ReferenceEquals(freshened, inner)) return;

        WriteBlob(StateKeyPath, RebuildOuter(blob, layout, freshened));
    }

    /// <summary>
    /// Returns a copy of <paramref name="inner"/> with the FILETIME varint at field 20 (immediately after the
    /// <c>0xC6 0x14</c> tag) replaced with the current Windows file time. If the tag isn't found - which can
    /// happen on a freshly-recreated blob that Windows hasn't toggled yet - inserts it just before the closing
    /// BT_STOP so future writes have a varint to refresh. Returns the input unchanged if even that fallback
    /// can't be safely applied.
    /// </summary>
    private static byte[] WithFreshFileTime(byte[] inner)
    {
        byte[] newVarint = EncodeVarint((ulong)DateTime.UtcNow.ToFileTimeUtc());
        int tagPos = IndexOf(inner, FileTimeTag, OuterMagic.Length);

        if (tagPos >= 0)
        {
            int valueStart = tagPos + FileTimeTag.Length;
            int existingLen = VarintLength(inner, valueStart);
            if (existingLen <= 0 || valueStart + existingLen > inner.Length) return inner;

            byte[] result = new byte[inner.Length - existingLen + newVarint.Length];
            Array.Copy(inner, 0, result, 0, valueStart);
            Array.Copy(newVarint, 0, result, valueStart, newVarint.Length);
            int afterValue = valueStart + existingLen;
            Array.Copy(inner, afterValue, result, valueStart + newVarint.Length, inner.Length - afterValue);
            return result;
        }

        // FILETIME field absent: insert tag + varint immediately before the trailing BT_STOP byte. Bond
        // struct termination is a single 0x00, so a well-formed inner always ends with one. If it doesn't
        // (truncated/corrupt), leave it alone rather than risk a write that breaks future reads.
        if (inner.Length == 0 || inner[^1] != 0x00) return inner;

        byte[] inserted = new byte[inner.Length - 1 + FileTimeTag.Length + newVarint.Length + 1];
        Array.Copy(inner, 0, inserted, 0, inner.Length - 1);
        Array.Copy(FileTimeTag, 0, inserted, inner.Length - 1, FileTimeTag.Length);
        Array.Copy(newVarint, 0, inserted, inner.Length - 1 + FileTimeTag.Length, newVarint.Length);
        inserted[^1] = 0x00;
        return inserted;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        int end = haystack.Length - needle.Length;
        for (int i = start; i <= end; i++)
        {
            bool match = !needle.Where((t, j) => haystack[i + j] != t).Any();
            if (match) return i;
        }

        return -1;
    }

    // -- Parsing ---------------------------------------------------------

    private readonly record struct OuterLayout(
        int TimestampStart,
        int TimestampLength,
        int InnerStart,
        int InnerLength,
        int TailStart)
    {
        public int InnerEnd => InnerStart + InnerLength;
    }

    private static bool TryParseOuter(byte[] blob, out OuterLayout layout)
    {
        layout = default;
        if (blob.Length < 20) return false;

        if (!MatchesAt(blob, 0, OuterMagic)) return false;

        if (!MatchesAt(blob, 4, OuterHeader)) return false;

        const int tsStart = 10;
        int tsLen = VarintLength(blob, tsStart);
        if (tsLen <= 0) return false;

        int after = tsStart + tsLen;

        if (!MatchesAt(blob, after, OuterInnerPrefix)) return false;

        after += OuterInnerPrefix.Length;
        if (after >= blob.Length) return false;

        int innerLen = blob[after];
        int innerStart = after + 1;
        int innerEnd = innerStart + innerLen;
        if (innerEnd > blob.Length) return false;

        if (!MatchesAt(blob, innerStart, OuterMagic)) return false;

        layout = new OuterLayout(tsStart, tsLen, innerStart, innerLen, innerEnd);
        return true;
    }

    private static bool HasEnabledMarker(byte[] blob, OuterLayout layout)
    {
        int markerStart = layout.InnerStart + OuterMagic.Length;
        return markerStart + EnabledMarker.Length <= layout.InnerEnd
               && MatchesAt(blob, markerStart, EnabledMarker);
    }

    private static int FindTagValueStart(byte[] blob, OuterLayout layout, byte[] tag)
    {
        int end = layout.InnerEnd - tag.Length;
        for (int i = layout.InnerStart + OuterMagic.Length; i <= end; i++)
            if (MatchesAt(blob, i, tag))
                return i + tag.Length;

        return -1;
    }

    private static int VarintLength(byte[] data, int start)
    {
        for (int i = 0; i < 10; i++)
        {
            int p = start + i;
            if (p >= data.Length) return 0;

            if ((data[p] & 0x80) == 0) return i + 1;
        }

        return 0;
    }

    private static ulong DecodeVarint(byte[] data, int start, int length)
    {
        // Unsigned: Bond varints carry no sign bit,
        // and a 10-byte varint shifts its terminating byte's payload into bit 63
        // - using long here would wrap negative on big values.
        // We never actually decode 10-byte varints (Unix seconds + FILETIME both fit in <10 bytes),
        // but matching the wire-format semantics keeps callers honest.
        ulong result = 0;
        for (int i = 0; i < length; i++)
            result |= (ulong)(data[start + i] & 0x7F) << (7 * i);
        return result;
    }

    // -- Rebuilding ------------------------------------------------------

    private static byte[] RebuildOuter(byte[] original, OuterLayout layout, byte[] newInner)
    {
        // Inner length is encoded as a single byte in the outer envelope (the BT_LIST<INT8> count varint,
        // which is always one byte for these blobs). In practice the inner is <40B; if a future Windows
        // revision blows past 255 we'd need a multi-byte varint here. Refuse the write rather than corrupt.
        if (newInner.Length > 255)
        {
            WPFLog.Log(
                $"NightLightRegistry.RebuildOuter: inner too large ({newInner.Length}B)"
                + " - skipping write to avoid corruption.");
            return original;
        }

        byte[] freshTs = EncodeFreshTimestamp(original, layout);
        int tailLen = original.Length - layout.TailStart;

        int size = 10
                   + freshTs.Length
                   + OuterInnerPrefix.Length
                   + 1
                   + newInner.Length
                   + tailLen;

        byte[] result = new byte[size];
        int p = 0;

        Array.Copy(original, 0, result, p, 10);
        p += 10;
        Array.Copy(freshTs, 0, result, p, freshTs.Length);
        p += freshTs.Length;
        Array.Copy(OuterInnerPrefix, 0, result, p, OuterInnerPrefix.Length);
        p += OuterInnerPrefix.Length;
        result[p++] = (byte)newInner.Length;
        Array.Copy(newInner, 0, result, p, newInner.Length);
        p += newInner.Length;
        Array.Copy(original, layout.TailStart, result, p, tailLen);

        return result;
    }

    // Outer Unix-seconds timestamp. Always advances. If the existing value is wildly in the future
    // ("existing+1" can never catch up to real time - observed in the wild when a stray inflation pinned the
    // timestamp to year ~49,000), reset to "now" instead. Without this clamp, every subsequent write keeps
    // re-poisoning the blob and Windows ignores live strength changes.
    private static byte[] EncodeFreshTimestamp(byte[] blob, OuterLayout layout)
    {
        ulong existing = DecodeVarint(blob, layout.TimestampStart, layout.TimestampLength);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (existing > now + TimeConstants.NightLightMaxFutureSkewSeconds) existing = now - 1;

        return EncodeVarint(Math.Max(existing + 1, now));
    }

    private static byte[] EncodeVarint(ulong value)
    {
        Span<byte> varintBuffer = stackalloc byte[10];
        int i = 0;
        while (value >= 0x80)
        {
            varintBuffer[i++] = (byte)(value | 0x80);
            value >>= 7;
        }

        varintBuffer[i++] = (byte)value;
        return varintBuffer[..i].ToArray();
    }

    private static byte[] InsertEnabledMarker(byte[] inner)
    {
        byte[] result = new byte[inner.Length + EnabledMarker.Length];
        Array.Copy(inner, 0, result, 0, OuterMagic.Length);
        Array.Copy(EnabledMarker, 0, result, OuterMagic.Length, EnabledMarker.Length);
        Array.Copy(
            inner, OuterMagic.Length,
            result, OuterMagic.Length + EnabledMarker.Length,
            inner.Length - OuterMagic.Length);
        return result;
    }

    private static byte[] RemoveEnabledMarker(byte[] inner)
    {
        byte[] result = new byte[inner.Length - EnabledMarker.Length];
        Array.Copy(inner, 0, result, 0, OuterMagic.Length);
        Array.Copy(
            inner, OuterMagic.Length + EnabledMarker.Length,
            result, OuterMagic.Length,
            inner.Length - OuterMagic.Length - EnabledMarker.Length);
        return result;
    }

    // -- Misc ------------------------------------------------------------

    private static byte[] Slice(byte[] src, int start, int length)
    {
        byte[] dst = new byte[length];
        Array.Copy(src, start, dst, 0, length);
        return dst;
    }

    private static bool MatchesAt(byte[] data, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > data.Length) return false;

        for (int i = 0; i < pattern.Length; i++)
            if (data[offset + i] != pattern[i])
                return false;

        return true;
    }

    private static byte[]? ReadBlob(string path)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, writable: false);
            return key?.GetValue(DataValueName) as byte[];
        }
        catch (Exception ex)
        {
            WPFLog.Log($"NightLightRegistry.ReadBlob('{path}'): {ex.Message}");
            return null;
        }
    }

    private static void WriteBlob(string path, byte[] value)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, writable: true);
            if (key == null) return;

            key.SetValue(DataValueName, value, RegistryValueKind.Binary);
            // RegFlushKey: force write-through so RegNotifyChangeKeyValue watchers (the BlueLightReduction service)
            // see the change immediately rather than waiting for the registry's lazy flush.
            key.Flush();
        }
        catch (Exception ex)
        {
            // Registry write can fail on locked/roaming profiles - caller sees no-op.
            WPFLog.Log($"NightLightRegistry.WriteBlob('{path}'): {ex.Message}");
        }
    }
}
