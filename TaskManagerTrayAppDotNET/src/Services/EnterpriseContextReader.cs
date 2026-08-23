using System.Runtime.InteropServices;
using System.Text;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads Windows Information Protection context only while its Details column is active.</summary>
internal sealed unsafe class EnterpriseContextReader : IDisposable
{
    public const string NotApplicable = "N/A";
    public const string Personal = "Personal";
    public const string Exempt = "Exempt";

    private const string EDPModuleName = "ext-ms-win-edputil-policy-l1-1-0.dll";
    private const string GetContextExportName = "EdpGetContextForProcess";
    private const string FreeContextExportName = "EdpFreeContext";
    private const string Enlightened = "Enlightened";
    private const string Unenlightened = "Unenlightened";
    private const string Permissive = "Permissive";
    private const string FileCopyExempt = "File copy exempt";
    private const int MaximumEnterpriseIDCount = 64;
    private const uint ExemptFlag = 0x01;
    private const uint EnlightenedFlag = 0x02;
    private const uint UnenlightenedFlag = 0x04;
    private const uint PermissiveFlag = 0x08;
    private const uint FileCopyExemptFlag = 0x10;
    private const uint PersonalFlag = 0x20;
    private const ulong EDPEnforcementLevelStateName = 1_410_189_807_866_174_581UL;

    private readonly Dictionary<string, string> _canonicalContexts = new(StringComparer.Ordinal);
    private IntPtr _module;
    private delegate* unmanaged[Stdcall]<uint, IntPtr*, int> _getContextForProcess;
    private delegate* unmanaged[Stdcall]<IntPtr, void> _freeContext;
    private bool _isEnforced;
    private bool _disposed;

    public EnterpriseContextReader()
    {
        if (!NativeLibrary.TryLoad(EDPModuleName, out _module)) return;
        if (!NativeLibrary.TryGetExport(_module, GetContextExportName, out IntPtr getContextAddress)
            || !NativeLibrary.TryGetExport(_module, FreeContextExportName, out IntPtr freeContextAddress))
        {
            NativeLibrary.Free(_module);
            _module = IntPtr.Zero;
            return;
        }

        _getContextForProcess =
            (delegate* unmanaged[Stdcall]<uint, IntPtr*, int>)getContextAddress;
        _freeContext = (delegate* unmanaged[Stdcall]<IntPtr, void>)freeContextAddress;
    }

    /// <summary>Refreshes the machine-wide enforcement state once for the upcoming process sample.</summary>
    public void BeginSample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isEnforced = _module != IntPtr.Zero && ReadEnforcementLevel() != 0;
    }

    public string Read(int processID)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isEnforced || processID < 0 || _getContextForProcess == null) return NotApplicable;

        IntPtr context = IntPtr.Zero;
        int result = _getContextForProcess((uint)processID, &context);
        if (result < 0 || context == IntPtr.Zero) return NativeProcessInfo.Unavailable;

        try
        {
            uint flags = unchecked((uint)Marshal.ReadInt32(context));
            int enterpriseIDCount = Marshal.ReadInt32(context, sizeof(uint));
            if (enterpriseIDCount is < 0 or > MaximumEnterpriseIDCount)
                return NativeProcessInfo.Unavailable;

            string[] enterpriseIDs = enterpriseIDCount == 0
                ? []
                : new string[enterpriseIDCount];
            for (int enterpriseIDIndex = 0; enterpriseIDIndex < enterpriseIDCount; enterpriseIDIndex++)
            {
                int pointerOffset = 2 * IntPtr.Size + enterpriseIDIndex * IntPtr.Size;
                IntPtr enterpriseIDAddress = Marshal.ReadIntPtr(context, pointerOffset);
                enterpriseIDs[enterpriseIDIndex] = enterpriseIDAddress == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUni(enterpriseIDAddress) ?? string.Empty;
            }

            return Canonicalize(FormatContext(flags, enterpriseIDs));
        }
        finally
        {
            _freeContext(context);
        }
    }

    internal static string FormatContext(uint flags, IReadOnlyList<string> enterpriseIDs)
    {
        ArgumentNullException.ThrowIfNull(enterpriseIDs);
        if ((flags & ExemptFlag) != 0) return Exempt;
        if (flags == 0 || (flags & PersonalFlag) != 0) return Personal;

        StringBuilder value = new();
        bool hasValue = false;
        for (int enterpriseIDIndex = 0; enterpriseIDIndex < enterpriseIDs.Count; enterpriseIDIndex++)
        {
            string enterpriseID = enterpriseIDs[enterpriseIDIndex];
            if (enterpriseID.Length == 0) continue;
            AppendSeparated(value, enterpriseID, ref hasValue);
        }

        bool hasEnterpriseID = value.Length > 0;
        bool hasDescriptor = false;
        if (hasEnterpriseID && HasDescriptor(flags)) value.Append(" (");
        AppendFlagDescriptor(value, flags, EnlightenedFlag, Enlightened, ref hasDescriptor);
        AppendFlagDescriptor(value, flags, UnenlightenedFlag, Unenlightened, ref hasDescriptor);
        AppendFlagDescriptor(value, flags, PermissiveFlag, Permissive, ref hasDescriptor);
        AppendFlagDescriptor(value, flags, FileCopyExemptFlag, FileCopyExempt, ref hasDescriptor);
        if (hasEnterpriseID && hasDescriptor) value.Append(')');

        return value.Length == 0 ? Personal : value.ToString();
    }

    private static bool HasDescriptor(uint flags) =>
        (flags & (EnlightenedFlag | UnenlightenedFlag | PermissiveFlag | FileCopyExemptFlag)) != 0;

    private static void AppendFlagDescriptor(
        StringBuilder value,
        uint flags,
        uint flag,
        string descriptor,
        ref bool hasValue)
    {
        if ((flags & flag) == 0) return;
        AppendSeparated(value, descriptor, ref hasValue);
    }

    private static void AppendSeparated(StringBuilder value, string text, ref bool hasValue)
    {
        if (hasValue) value.Append(", ");
        value.Append(text);
        hasValue = true;
    }

    private string Canonicalize(string value)
    {
        if (value is NotApplicable or Personal or Exempt or NativeProcessInfo.Unavailable) return value;
        if (_canonicalContexts.TryGetValue(value, out string? canonical)) return canonical;

        _canonicalContexts.Add(value, value);
        return value;
    }

    private static uint ReadEnforcementLevel()
    {
        ulong stateName = EDPEnforcementLevelStateName;
        uint changeStamp = 0;
        uint enforcementLevel = 0;
        uint bufferSize = sizeof(uint);
        int status = NtQueryWnfStateData(
            ref stateName,
            IntPtr.Zero,
            IntPtr.Zero,
            ref changeStamp,
            ref enforcementLevel,
            ref bufferSize);
        return status >= 0 && bufferSize == sizeof(uint) && changeStamp != 0
            ? enforcementLevel
            : 0;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _canonicalContexts.Clear();
        _getContextForProcess = null;
        _freeContext = null;
        if (_module == IntPtr.Zero) return;

        NativeLibrary.Free(_module);
        _module = IntPtr.Zero;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(
        ref ulong stateName,
        IntPtr typeID,
        IntPtr explicitScope,
        ref uint changeStamp,
        ref uint buffer,
        ref uint bufferSize);
}
