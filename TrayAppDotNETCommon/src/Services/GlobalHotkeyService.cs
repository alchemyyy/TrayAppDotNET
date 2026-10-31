using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.Services;

public sealed class HotkeyFiredEventArgs<TAction>(TAction action, string parameter) : EventArgs
    where TAction : struct, Enum
{
    public TAction Action { get; } = action;
    public string Parameter { get; } = parameter;
}

/// <summary>Why a binding is not registered.</summary>
public enum HotkeyRegistrationError
{
    NoKey,
    NoModifier,
    ReservedKey,

    /// <summary>An earlier binding of the same app already uses the key combination.</summary>
    DuplicateInApp,

    /// <summary>Windows refused the combination because another app (or Windows) registered it (error 1409).</summary>
    InUseByAnotherApp,
    Failed
}

/// <summary>
/// A binding that is not registered. <see cref="ConflictingBinding"/> names the binding that keeps the combination
/// for <see cref="HotkeyRegistrationError.DuplicateInApp"/>; <see cref="Win32Error"/> is set for
/// <see cref="HotkeyRegistrationError.Failed"/>.
/// </summary>
public sealed record HotkeyRegistrationFailure<TBinding>(
    HotkeyRegistrationError Error,
    TBinding? ConflictingBinding = null,
    int Win32Error = 0)
    where TBinding : class;

public sealed class HotkeyApplyResult<TBinding>
    where TBinding : class
{
    public static readonly HotkeyApplyResult<TBinding> Empty = new([], new Dictionary<TBinding, HotkeyRegistrationFailure<TBinding>>());

    public HotkeyApplyResult(
        IReadOnlyList<TBinding> registered,
        IReadOnlyDictionary<TBinding, HotkeyRegistrationFailure<TBinding>> failed)
    {
        Registered = registered;
        Failed = failed;
    }

    public IReadOnlyList<TBinding> Registered { get; }

    /// <summary>Enabled, bound bindings that are not registered, keyed by reference.</summary>
    public IReadOnlyDictionary<TBinding, HotkeyRegistrationFailure<TBinding>> Failed { get; }
}

/// <summary>
/// Owns a hidden message-only window, registers the enabled bindings with RegisterHotKey, and translates WM_HOTKEY
/// back into the (action, parameter) pair the user bound. <see cref="Apply"/> diffs: a key combination that stays
/// bound keeps its registration, so applying an unchanged list touches no Win32 state.
/// </summary>
public sealed class GlobalHotkeyService<TAction, TBinding>(
    string messageWindowClassPrefix = "TrayAppDotNETCommon.GlobalHotkeyService")
    : IDisposable
    where TAction : struct, Enum
    where TBinding : HotkeyBindingBase<TAction>
{
    // Registered combinations keyed by (modifiers without MOD_NOREPEAT, virtual key)
    private readonly Dictionary<(uint Modifiers, uint VirtualKey), Registration> _registered = [];
    private readonly Dictionary<int, Registration> _byID = [];
    private Win32Window? _window;
    private int _nextID = 1;
    private bool _disposed;

    /// <summary>Fired on the UI thread when a registered hotkey is pressed.</summary>
    public event EventHandler<HotkeyFiredEventArgs<TAction>>? Fired;

    /// <summary>Result of the latest <see cref="Apply"/>; settings pages read it instead of applying again.</summary>
    public HotkeyApplyResult<TBinding> LastResult { get; private set; } = HotkeyApplyResult<TBinding>.Empty;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window != null) return;

        Win32Window window = new();
        window.Initialize(messageWindowClassPrefix + ".MessageWindow", WndProc, User32.HWND_MESSAGE);
        _window = window;
    }

    /// <summary>
    /// Brings the registrations in line with <paramref name="bindings"/>: unregisters combinations no longer bound,
    /// registers new ones, and keeps the rest. The first binding of a combination wins; later bindings with the same
    /// combination report <see cref="HotkeyRegistrationError.DuplicateInApp"/>. The result is stored in
    /// <see cref="LastResult"/> and every failure is logged.
    /// </summary>
    public HotkeyApplyResult<TBinding> Apply(IEnumerable<TBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IntPtr hwnd = _window?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Initialize must be called before Apply.");

        Dictionary<(uint Modifiers, uint VirtualKey), TBinding> wanted = [];
        Dictionary<TBinding, HotkeyRegistrationFailure<TBinding>> failed = new(ReferenceEqualityComparer.Instance);
        foreach (TBinding binding in bindings)
        {
            if (binding.RemovedByUser || !binding.Enabled || !binding.IsBound) continue;

            if (Validate(binding) is { } invalid)
            {
                failed[binding] = new HotkeyRegistrationFailure<TBinding>(invalid);
                continue;
            }

            (uint, uint) chord = (binding.Modifiers, binding.VirtualKey);
            if (wanted.TryGetValue(chord, out TBinding? owner))
            {
                failed[binding] = new HotkeyRegistrationFailure<TBinding>(
                    HotkeyRegistrationError.DuplicateInApp,
                    owner);
                continue;
            }

            wanted[chord] = binding;
        }

        foreach (((uint, uint) chord, Registration registration) in _registered.ToArray())
        {
            if (wanted.ContainsKey(chord)) continue;

            Unregister(hwnd, registration);
            _registered.Remove(chord);
        }

        if (_registered.Count == 0) _nextID = 1;

        List<TBinding> registered = [];
        foreach (((uint modifiers, uint virtualKey) chord, TBinding binding) in wanted)
        {
            if (_registered.TryGetValue(chord, out Registration? existing))
            {
                existing.Binding = binding;
                registered.Add(binding);
                continue;
            }

            int id = _nextID++;
            if (!User32.RegisterHotKey(hwnd, id, chord.modifiers | HotkeyModifiers.NoRepeat, chord.virtualKey))
            {
                int lastError = Marshal.GetLastWin32Error();
                failed[binding] = lastError == ErrorHotkeyAlreadyRegistered
                    ? new HotkeyRegistrationFailure<TBinding>(HotkeyRegistrationError.InUseByAnotherApp)
                    : new HotkeyRegistrationFailure<TBinding>(HotkeyRegistrationError.Failed, Win32Error: lastError);
                continue;
            }

            Registration registration = new(id, binding);
            _registered[chord] = registration;
            _byID[id] = registration;
            registered.Add(binding);
        }

        LastResult = new HotkeyApplyResult<TBinding>(registered, failed);
        foreach ((TBinding binding, HotkeyRegistrationFailure<TBinding> failure) in failed)
            TADNLog.Log($"Hotkey {binding.Action}({binding.Parameter}) #{binding.BindingID} not registered: {failure}");
        return LastResult;
    }

    /// <summary>The RegisterHotKey id the binding's combination is registered under, or null when it is not.</summary>
    internal int? RegistrationIDOf(TBinding binding) =>
        _registered.TryGetValue((binding.Modifiers, binding.VirtualKey), out Registration? registration)
        && ReferenceEquals(registration.Binding, binding)
            ? registration.ID
            : null;

    /// <summary>
    /// Defence-in-depth validator. Mirrors the capture UI's core rules so hand-edited settings cannot bypass them.
    /// Returns null for a valid binding.
    /// </summary>
    public static HotkeyRegistrationError? Validate(TBinding binding)
    {
        if (binding.VirtualKey == 0) return HotkeyRegistrationError.NoKey;
        if ((binding.Modifiers & HotkeyModifiers.Required) == 0) return HotkeyRegistrationError.NoModifier;
        if (binding.VirtualKey == HotkeyVirtualKeys.DebuggerReserved) return HotkeyRegistrationError.ReservedKey;
        return null;
    }

    private void UnregisterAll()
    {
        IntPtr hwnd = _window?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            foreach (Registration registration in _registered.Values)
                Unregister(hwnd, registration);
        }

        _registered.Clear();
        _byID.Clear();
        _nextID = 1;
    }

    private void Unregister(IntPtr hwnd, Registration registration)
    {
        _byID.Remove(registration.ID);
        if (!User32.UnregisterHotKey(hwnd, registration.ID))
            TADNLog.Log($"UnregisterHotKey({registration.ID}) failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != User32.WM_HOTKEY) return IntPtr.Zero;
        if (!_byID.TryGetValue(wParam.ToInt32(), out Registration? registration)) return IntPtr.Zero;

        TBinding binding = registration.Binding;
        try { Fired?.Invoke(this, new HotkeyFiredEventArgs<TAction>(binding.Action, binding.Parameter)); }
        catch (Exception ex) { TADNLog.Log($"GlobalHotkeyService<{typeof(TAction).Name}>.Fired handler threw: {ex}"); }

        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        UnregisterAll();
        _window?.Dispose();
        _window = null;
        Fired = null;
    }

    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private sealed class Registration(int id, TBinding binding)
    {
        public int ID { get; } = id;
        public TBinding Binding { get; set; } = binding;
    }
}
