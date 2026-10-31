using System.Globalization;
using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Interop.Wmi;
using TrayAppDotNETCommon.Utils;

namespace BrightnessTrayAppDotNET.Interop.WindowsBrightness;

internal sealed record WindowsBrightnessTarget(
    string InstanceName,
    string DisplayInstancePath,
    string MethodPath,
    int CurrentBrightness);

/// <summary>
/// Client for Windows' built-in display brightness provider (<c>ROOT\WMI</c>), built on the Common Native AOT
/// friendly <see cref="WmiConnection"/> (no System.Management). Every WMI wait is bounded by
/// <see cref="WmiConnection.DefaultQueryTimeout"/>, so a stalled WMI service fails the call instead of hanging it.
/// </summary>
internal static class WindowsBrightnessWmi
{
    private const string WmiNamespace = @"ROOT\WMI";

    public static bool TryGetActiveTargets(out IReadOnlyList<WindowsBrightnessTarget> targets, out string? error)
    {
        targets = [];
        error = null;

        try
        {
            using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_MULTITHREADED);
            if (!TryRequireApartment(apartment.HResult, apartment.IsUsable, out error)) return false;
            if (!WmiConnection.TryConnect(WmiNamespace, out WmiConnection? connection, out error)) return false;
            using (connection)
            {
                Dictionary<string, string> methodPathByInstance = QueryBrightnessMethodPaths(connection);
                List<WindowsBrightnessTarget> found = [];

                connection.Query(
                    wql: "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE",
                    obj =>
                    {
                        if (!WmiConnection.TryGetString(obj, name: "InstanceName", out string instanceName)
                            || string.IsNullOrWhiteSpace(instanceName))
                            return;

                        if (!WmiConnection.TryGetUInt32(obj, name: "CurrentBrightness", out uint currentRaw))
                            return;

                        if (!methodPathByInstance.TryGetValue(instanceName, out string? methodPath)
                            || string.IsNullOrWhiteSpace(methodPath))
                            return;

                        found.Add(new WindowsBrightnessTarget(
                            instanceName,
                            NormalizeDisplayInstancePath(instanceName),
                            methodPath,
                            (int)Math.Clamp(currentRaw, min: 0, max: 100)));
                    });

                targets = found;
                return true;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryRequireApartment(int hResult, bool isUsable, out string? error)
    {
        error = isUsable ? null : $"CoInitializeEx(MTA) failed ({WmiNative.FormatHResult(hResult)}).";
        return isUsable;
    }

    public static bool TryGetBrightness(string instanceName, out int brightness, out string? error)
    {
        brightness = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(instanceName))
        {
            error = "Windows brightness instance name is empty.";
            return false;
        }

        if (!TryGetActiveTargets(out IReadOnlyList<WindowsBrightnessTarget> targets, out error))
            return false;

        WindowsBrightnessTarget? target = targets.FirstOrDefault(t =>
            string.Equals(t.InstanceName, instanceName, StringComparison.Ordinal));
        if (target == null)
        {
            error = $"Windows brightness instance '{instanceName}' is not active.";
            return false;
        }

        brightness = target.CurrentBrightness;
        return true;
    }

    public static bool TrySetBrightness(string methodPath, int brightness, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(methodPath))
        {
            error = "Windows brightness method path is empty.";
            return false;
        }

        int clamped = Math.Clamp(brightness, min: 0, max: 100);

        try
        {
            using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_MULTITHREADED);
            if (!TryRequireApartment(apartment.HResult, apartment.IsUsable, out error)) return false;
            if (!WmiConnection.TryConnect(WmiNamespace, out WmiConnection? connection, out error)) return false;
            using (connection)
            {
                IWbemClassObject? inParams = null;
                IWbemClassObject? outParams = null;
                try
                {
                    if (!TryBuildSetBrightnessParameters(connection.Services, clamped, out inParams, out error))
                        return false;

                    int hr = connection.Services.ExecMethod(
                        methodPath,
                        strMethodName: "WmiSetBrightness",
                        lFlags: 0,
                        IntPtr.Zero,
                        inParams,
                        out IntPtr outParamsPtr,
                        IntPtr.Zero);
                    if (hr < 0)
                    {
                        error = $"IWbemServices.ExecMethod(WmiSetBrightness) failed ({WmiNative.FormatHResult(hr)}).";
                        return false;
                    }

                    if (outParamsPtr == IntPtr.Zero) return true;

                    outParams = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                        outParamsPtr,
                        releaseInputReference: true);
                    if (!WmiConnection.TryGetUInt32(outParams, name: "ReturnValue", out uint returnValue))
                        return true;

                    if (returnValue == 0) return true;

                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        format: "WmiSetBrightness returned error {0}.",
                        returnValue);
                    return false;
                }
                finally
                {
                    Safe.Release(outParams);
                    Safe.Release(inParams);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string NormalizeDisplayInstancePath(string instanceName)
    {
        string trimmed = instanceName.Trim();
        if (trimmed.Length == 0) return string.Empty;

        int slash = trimmed.LastIndexOf('\\');
        if (slash < 0 || slash == trimmed.Length - 1) return trimmed.ToUpperInvariant();

        string prefix = trimmed[..(slash + 1)];
        string last = trimmed[(slash + 1)..];
        int underscore = last.LastIndexOf('_');
        if (underscore > 0 && underscore < last.Length - 1)
        {
            bool suffixIsNumeric = true;
            for (int i = underscore + 1; i < last.Length; i++)
            {
                if (char.IsDigit(last[i])) continue;
                suffixIsNumeric = false;
                break;
            }

            if (suffixIsNumeric) last = last[..underscore];
        }

        return (prefix + last).ToUpperInvariant();
    }

    private static Dictionary<string, string> QueryBrightnessMethodPaths(WmiConnection connection)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        connection.Query(
            wql: "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE",
            obj =>
            {
                if (!WmiConnection.TryGetString(obj, name: "InstanceName", out string instanceName)
                    || string.IsNullOrWhiteSpace(instanceName))
                    return;

                if (!WmiConnection.TryGetString(obj, name: "__PATH", out string path)
                    || string.IsNullOrWhiteSpace(path))
                    return;

                result[instanceName] = path;
            });

        return result;
    }

    private static bool TryBuildSetBrightnessParameters(
        IWbemServices services,
        int brightness,
        out IWbemClassObject? inParams,
        out string? error)
    {
        inParams = null;
        error = null;

        int hr = services.GetObject(
            strObjectPath: "WmiMonitorBrightnessMethods",
            lFlags: 0,
            IntPtr.Zero,
            out IntPtr classObjectPtr,
            IntPtr.Zero);
        if (hr < 0 || classObjectPtr == IntPtr.Zero)
        {
            error = $"IWbemServices.GetObject(WmiMonitorBrightnessMethods) failed ({WmiNative.FormatHResult(hr)}).";
            return false;
        }

        IWbemClassObject? classObject = null;
        IWbemClassObject? inSignature = null;
        IWbemClassObject? parameters = null;
        try
        {
            classObject = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                classObjectPtr,
                releaseInputReference: true);

            hr = classObject.GetMethod(wszName: "WmiSetBrightness", lFlags: 0, out IntPtr inSignaturePtr,
                out IntPtr outSignaturePtr);
            if (outSignaturePtr != IntPtr.Zero) Marshal.Release(outSignaturePtr);
            if (hr < 0 || inSignaturePtr == IntPtr.Zero)
            {
                if (inSignaturePtr != IntPtr.Zero) Marshal.Release(inSignaturePtr);
                error = $"IWbemClassObject.GetMethod(WmiSetBrightness) failed ({WmiNative.FormatHResult(hr)}).";
                return false;
            }

            inSignature = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                inSignaturePtr,
                releaseInputReference: true);

            hr = inSignature.SpawnInstance(lFlags: 0, out IntPtr inParamsPtr);
            if (hr < 0 || inParamsPtr == IntPtr.Zero)
            {
                error = $"IWbemClassObject.SpawnInstance(WmiSetBrightness input) failed ({WmiNative.FormatHResult(hr)}).";
                return false;
            }

            parameters = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                inParamsPtr,
                releaseInputReference: true);

            if (!WmiConnection.TryPutInt32(parameters, name: "Timeout", value: 0, out error)) return false;
            if (!WmiConnection.TryPutUInt8(parameters, name: "Brightness", (byte)brightness, out error)) return false;

            inParams = parameters;
            parameters = null;
            return true;
        }
        finally
        {
            Safe.Release(parameters);
            Safe.Release(inSignature);
            Safe.Release(classObject);
        }
    }
}
