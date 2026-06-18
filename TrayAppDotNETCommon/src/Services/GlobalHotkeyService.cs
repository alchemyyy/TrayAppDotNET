using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.Services;

public sealed class HotkeyFiredEventArgs(HotkeyAction action, string parameter) : EventArgs
{
    public HotkeyAction Action { get; } = action;
    public string Parameter { get; } = parameter;
}

public sealed class HotkeyFiredEventArgs<TAction>(TAction action, string parameter) : EventArgs
    where TAction : struct, Enum
{
    public TAction Action { get; } = action;
    public string Parameter { get; } = parameter;
}

public sealed class HotkeyApplyResult
{
    public List<HotkeyBinding> Registered { get; } = [];

    /// <summary>Bindings that failed to register (combo already taken by another app, reserved, etc.).</summary>
    public Dictionary<HotkeyBinding, string> Failed { get; } = [];
}

public sealed class HotkeyApplyResult<TAction, TBinding>
    where TAction : struct, Enum
    where TBinding : class, IHotkeyBinding<TAction>
{
    public List<TBinding> Registered { get; } = [];

    /// <summary>Bindings that failed to register (combo already taken by another app, reserved, etc.).</summary>
    public Dictionary<TBinding, string> Failed { get; } = [];
}

/// <summary>
/// Owns a hidden message-only window, listens for WM_HOTKEY,
/// and translates the fired ID back into the (action, parameter) pair the user bound.
/// </summary>
public sealed class GlobalHotkeyService(string messageWindowClassPrefix = "TrayAppDotNETCommon.GlobalHotkeyService")
    : IDisposable
{
    private Win32MessageWindow? _window;
    private IntPtr _hwnd;
    private int _nextId = 1;
    private readonly Dictionary<int, HotkeyBinding> _byId = [];
    private bool _disposed;

    /// <summary>Fired on the UI thread when a registered hotkey is pressed.</summary>
    public event EventHandler<HotkeyFiredEventArgs>? Fired;

    public void Initialize()
    {
        if (_window != null) return;

        _window = new Win32MessageWindow(messageWindowClassPrefix);
        _window.Initialize(WndProc);
        _hwnd = _window.Handle;
    }

    /// <summary>
    /// Diff-and-apply: unregister everything currently registered,
    /// then re-register each enabled binding from <paramref name="bindings"/>.
    /// </summary>
    public HotkeyApplyResult Apply(IEnumerable<HotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(GlobalHotkeyService));

        if (_window == null) throw new InvalidOperationException("Initialize must be called before Apply.");

        UnregisterAll();

        HotkeyApplyResult result = new();
        foreach (HotkeyBinding binding in bindings)
        {
            if (binding.RemovedByUser) continue;
            if (!binding.Enabled || !binding.IsBound) continue;

            if (TryRegisterInternal(binding, out string? error))
                result.Registered.Add(binding);
            else
                result.Failed[binding] = error ?? "Registration failed.";
        }

        return result;
    }

    /// <summary>
    /// Validate-and-register a single binding. Used by capture UI to give immediate feedback.
    /// </summary>
    public bool TryRegister(HotkeyBinding binding, out string? error)
    {
        if (_disposed)
        {
            error = "Service disposed.";
            return false;
        }

        if (_window == null)
        {
            error = "Service not initialized.";
            return false;
        }

        if (!binding.IsBound)
        {
            error = "Binding is incomplete.";
            return false;
        }

        return TryRegisterInternal(binding, out error);
    }

    private bool TryRegisterInternal(HotkeyBinding binding, out string? error)
    {
        if (!Validate(binding, out error)) return false;

        int id = _nextId++;
        uint modifiers = binding.Modifiers | HotkeyModifiers.NoRepeat;
        bool ok = HotkeyUser32.RegisterHotKey(_hwnd, id, modifiers, binding.VirtualKey);
        if (!ok)
        {
            int lastError = Marshal.GetLastWin32Error();
            error = lastError == 1409
                ? "Already in use by another app."
                : $"Registration failed (Win32 error {lastError}).";
            return false;
        }

        _byId[id] = binding;
        return true;
    }

    /// <summary>
    /// Defence-in-depth validator. Mirrors the capture UI's core rules so hand-edited settings cannot bypass them.
    /// </summary>
    public static bool Validate(HotkeyBinding binding, out string? error)
    {
        error = null;
        if (binding.VirtualKey == 0)
        {
            error = "No key set.";
            return false;
        }

        if ((binding.Modifiers &
             (HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)) == 0)
        {
            error = "At least one modifier (Ctrl, Alt, Shift, Win) is required.";
            return false;
        }

        if (binding.VirtualKey == 0x7B)
        {
            error = "F12 is reserved by the debugger.";
            return false;
        }

        return true;
    }

    private void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (int id in _byId.Keys)
        {
            try { HotkeyUser32.UnregisterHotKey(_hwnd, id); }
            catch
            {
                /* best effort */
            }
        }

        _byId.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != HotkeyUser32.WM_HOTKEY) return IntPtr.Zero;

        int id = wParam.ToInt32();
        if (!_byId.TryGetValue(id, out HotkeyBinding? binding)) return IntPtr.Zero;

        try { Fired?.Invoke(this, new HotkeyFiredEventArgs(binding.Action, binding.Parameter)); }
        catch (Exception ex) { TADNLog.Log($"GlobalHotkeyService.Fired handler threw: {ex}"); }

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
        _hwnd = IntPtr.Zero;
    }
}

/// <summary>
/// Generic variant for apps that keep their hotkey action enum in the app project.
/// </summary>
public sealed class GlobalHotkeyService<TAction, TBinding>(
    string messageWindowClassPrefix = "TrayAppDotNETCommon.GlobalHotkeyService")
    : IDisposable
    where TAction : struct, Enum
    where TBinding : class, IHotkeyBinding<TAction>
{
    private Win32MessageWindow? _window;
    private IntPtr _hwnd;
    private int _nextID = 1;
    private readonly Dictionary<int, TBinding> _byID = [];
    private bool _disposed;

    public event EventHandler<HotkeyFiredEventArgs<TAction>>? Fired;

    public void Initialize()
    {
        if (_window != null) return;

        _window = new Win32MessageWindow(messageWindowClassPrefix);
        _window.Initialize(WndProc);
        _hwnd = _window.Handle;
    }

    public HotkeyApplyResult<TAction, TBinding> Apply(IEnumerable<TBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(GlobalHotkeyService<TAction, TBinding>));

        if (_window == null) throw new InvalidOperationException("Initialize must be called before Apply.");

        UnregisterAll();

        HotkeyApplyResult<TAction, TBinding> result = new();
        foreach (TBinding binding in bindings)
        {
            if (binding.RemovedByUser) continue;
            if (!binding.Enabled || !binding.IsBound) continue;

            if (TryRegisterInternal(binding, out string? error))
                result.Registered.Add(binding);
            else
                result.Failed[binding] = error ?? "Registration failed.";
        }

        return result;
    }

    public bool TryRegister(TBinding binding, out string? error)
    {
        if (_disposed)
        {
            error = "Service disposed.";
            return false;
        }

        if (_window == null)
        {
            error = "Service not initialized.";
            return false;
        }

        if (!binding.IsBound)
        {
            error = "Binding is incomplete.";
            return false;
        }

        return TryRegisterInternal(binding, out error);
    }

    private bool TryRegisterInternal(TBinding binding, out string? error)
    {
        if (!Validate(binding, out error)) return false;

        int id = _nextID++;
        uint modifiers = binding.Modifiers | HotkeyModifiers.NoRepeat;
        bool ok = HotkeyUser32.RegisterHotKey(_hwnd, id, modifiers, binding.VirtualKey);
        if (!ok)
        {
            int lastError = Marshal.GetLastWin32Error();
            error = lastError == 1409
                ? "Already in use by another app."
                : $"Registration failed (Win32 error {lastError}).";
            return false;
        }

        _byID[id] = binding;
        return true;
    }

    public static bool Validate(IHotkeyBinding<TAction> binding, out string? error)
    {
        error = null;
        if (binding.VirtualKey == 0)
        {
            error = "No key set.";
            return false;
        }

        if ((binding.Modifiers &
             (HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)) == 0)
        {
            error = "At least one modifier (Ctrl, Alt, Shift, Win) is required.";
            return false;
        }

        if (binding.VirtualKey == 0x7B)
        {
            error = "F12 is reserved by the debugger.";
            return false;
        }

        return true;
    }

    private void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (int id in _byID.Keys)
        {
            try { HotkeyUser32.UnregisterHotKey(_hwnd, id); }
            catch { }
        }

        _byID.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != HotkeyUser32.WM_HOTKEY) return IntPtr.Zero;

        int id = wParam.ToInt32();
        if (!_byID.TryGetValue(id, out TBinding? binding)) return IntPtr.Zero;

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
        _hwnd = IntPtr.Zero;
    }
}
