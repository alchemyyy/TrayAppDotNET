using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>COM library natives (ole32.dll). Every app declares these here only.</summary>
public static class Ole32
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    public const uint COINIT_MULTITHREADED = 0x0;
    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const uint COINIT_DISABLE_OLE1DDE = 0x4;

    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const uint CLSCTX_INPROC_HANDLER = 0x2;
    public const uint CLSCTX_LOCAL_SERVER = 0x4;
    public const uint CLSCTX_REMOTE_SERVER = 0x10;
    public const uint CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER;

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern void CoUninitialize();

    /// <summary>
    /// Raw activation for callers that drive the object through vtable pointers. Source-generated interfaces go
    /// through <see cref="COMActivation.CreateInstance{T}"/>, which wraps the result uniquely.
    /// </summary>
    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern int CoSetProxyBlanket(
        IntPtr pProxy,
        uint dwAuthnSvc,
        uint dwAuthzSvc,
        IntPtr pServerPrincName,
        uint dwAuthnLevel,
        uint dwImpLevel,
        IntPtr pAuthInfo,
        uint dwCapabilities);
}

/// <summary>OLE Automation natives (oleaut32.dll).</summary>
public static class OleAut32
{
    /// <summary>Releases what a VARIANT owns (BSTR, interface, array) and resets it to VT_EMPTY.</summary>
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    public static extern int VariantClear(IntPtr variant);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    public static extern void SysFreeString(IntPtr bString);
}

/// <summary>
/// One COM apartment entry with a single failure policy, used by every <c>CoInitializeEx</c> site: a successful
/// call (S_OK or S_FALSE) is balanced on dispose; RPC_E_CHANGED_MODE means the thread already lives in another
/// apartment, which is usable but not owned; any other failure is unusable, and the caller reports
/// <see cref="HResult"/>.
/// </summary>
public ref struct ComApartmentScope
{
    private bool _ownsInitialization;

    private ComApartmentScope(int hResult)
    {
        HResult = hResult;
        _ownsInitialization = hResult >= 0;
    }

    public int HResult { get; }

    public readonly bool OwnsInitialization => _ownsInitialization;

    public readonly bool IsUsable => HResult >= 0 || HResult == Ole32.RPC_E_CHANGED_MODE;

    public static ComApartmentScope Enter(uint coInit) => new(Ole32.CoInitializeEx(IntPtr.Zero, coInit));

    public void Dispose()
    {
        if (!_ownsInitialization) return;
        _ownsInitialization = false;
        Ole32.CoUninitialize();
    }
}
