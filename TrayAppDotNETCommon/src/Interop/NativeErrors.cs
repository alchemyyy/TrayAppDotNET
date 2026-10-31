namespace TrayAppDotNETCommon.Interop;

// Win32 error and HRESULT constants reused across the interop surface.
public static class NativeErrors
{
    public const int S_OK = 0;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>The user declined the UAC prompt (ShellExecute "runas" fails with it).</summary>
    public const int ERROR_CANCELLED = 1223;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED), which some shell paths report instead.</summary>
    public const int HRESULT_ERROR_CANCELLED = unchecked((int)0x800704C7);
}
