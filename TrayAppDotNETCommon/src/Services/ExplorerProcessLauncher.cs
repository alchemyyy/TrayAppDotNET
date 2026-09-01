using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Services;

public readonly record struct ProcessLaunchResult(
    bool Succeeded,
    int ProcessID,
    int ParentProcessID,
    string ErrorMessage);

/// <summary>Keeps the nominated Windows parent object alive across process termination.</summary>
public sealed class ProcessParentHandle : IDisposable
{
    private IntPtr _handle;

    internal ProcessParentHandle(IntPtr handle, int processID)
    {
        _handle = handle;
        ProcessID = processID;
    }

    internal IntPtr Handle => _handle;

    public int ProcessID { get; }

    public bool IsValid => _handle != IntPtr.Zero && ProcessID > 0;

    public void Dispose()
    {
        IntPtr handleToClose = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handleToClose != IntPtr.Zero)
            _ = Kernel32.CloseHandle(handleToClose);
    }
}

/// <summary>Runs user shell actions through Explorer and explicitly parents a restarted shell.</summary>
public static unsafe class ExplorerProcessLauncher
{
    internal const uint CreateNoWindow = 0x08000000;

    private const uint CoinitApartmentThreaded = 0x2;
    private const uint ClassContextAll = 0x17;
    private const int DesktopShellWindowClass = 8;
    private const uint DispatchMethod = 0x1;
    private const uint DispatchPropertyGet = 0x2;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint LocaleUserDefault = 0x0400;
    private const nuint ParentProcessAttribute = 0x00020000;
    private const uint ProcessCreateProcess = 0x0080;
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private const int ShellViewBackground = 0;
    private const int ShellWindowFindNeedsDispatch = 1;
    private const int ShowNormal = 1;
    private const ushort VariantTypeBString = 8;
    private const ushort VariantTypeDispatch = 9;
    private const ushort VariantTypeEmpty = 0;
    private const ushort VariantTypeInt32 = 3;

    private static readonly Guid DispatchInterfaceID = new("00020400-0000-0000-C000-000000000046");
    private static readonly Guid ServiceProviderInterfaceID = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid ShellBrowserInterfaceID = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid ShellWindowsClassID = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid ShellWindowsInterfaceID = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid TopLevelBrowserServiceID = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    /// <summary>Asks the desktop Explorer process to perform a shell operation.</summary>
    public static bool TryShellExecute(
        string target,
        string? arguments,
        string? workingDirectory,
        string? verb,
        out int errorCode,
        out string errorMessage)
    {
        errorCode = 0;
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            errorCode = Win32ErrorInvalidParameter;
            errorMessage = "The shell target is empty.";
            return false;
        }

        int initializationResult = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);
        bool shouldUninitialize = initializationResult is HResultSuccess or HResultFalse;
        if (initializationResult < 0 && initializationResult != RpcEChangedMode)
        {
            errorCode = ExtractWin32Error(initializationResult);
            errorMessage = DescribeHResult(initializationResult, "COM initialization failed");
            return false;
        }

        try
        {
            if (!TryGetDesktopFolderViewDispatch(
                    out IntPtr folderViewDispatch,
                    out errorCode,
                    out errorMessage))
                return false;

            try
            {
                if (!TryGetApplicationDispatch(
                        folderViewDispatch,
                        out IntPtr applicationDispatch,
                        out AutomationVariant applicationVariant,
                        out errorCode,
                        out errorMessage))
                    return false;

                try
                {
                    return TryInvokeShellExecute(
                        applicationDispatch,
                        target.Trim(),
                        arguments ?? string.Empty,
                        workingDirectory ?? string.Empty,
                        verb ?? string.Empty,
                        out errorCode,
                        out errorMessage);
                }
                finally
                {
                    ClearVariant(ref applicationVariant);
                }
            }
            finally
            {
                ReleaseCOMInterface(folderViewDispatch);
            }
        }
        finally
        {
            if (shouldUninitialize) CoUninitialize();
        }
    }

    /// <summary>Creates an executable with the explicitly nominated process as its Windows parent.</summary>
    public static ProcessLaunchResult StartWithParent(
        ProcessParentHandle parent,
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        uint additionalCreationFlags = 0)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!parent.IsValid)
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: 0,
                ErrorMessage: "The nominated parent process handle is closed.");
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: 0,
                ErrorMessage: "The executable path is empty.");
        }

        if (!Path.IsPathFullyQualified(executablePath))
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: 0,
                ErrorMessage: "The executable path must be fully qualified.");
        }

        IntPtr parentProcessHandle = parent.Handle;
        int parentProcessID = parent.ProcessID;

        IntPtr attributeList = IntPtr.Zero;
        IntPtr parentHandleValue = IntPtr.Zero;
        bool isAttributeListInitialized = false;
        try
        {
            nuint attributeListSize = 0;
            _ = InitializeProcThreadAttributeList(
                IntPtr.Zero,
                attributeCount: 1,
                flags: 0,
                ref attributeListSize);
            if (attributeListSize == 0)
                return CreateWin32Failure("Sizing the process attribute list", parentProcessID);

            attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
            if (!InitializeProcThreadAttributeList(
                    attributeList,
                    attributeCount: 1,
                    flags: 0,
                    ref attributeListSize))
                return CreateWin32Failure("Initializing the process attribute list", parentProcessID);
            isAttributeListInitialized = true;

            parentHandleValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(parentHandleValue, parentProcessHandle);
            if (!UpdateProcThreadAttribute(
                    attributeList,
                    flags: 0,
                    ParentProcessAttribute,
                    parentHandleValue,
                    (nuint)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
                return CreateWin32Failure("Assigning the process parent", parentProcessID);

            StartupInfoEx startupInfo = new()
            {
                StartupInfo = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() },
                AttributeList = attributeList
            };
            StringBuilder commandLine = BuildCommandLine(executablePath, arguments);
            if (!CreateProcessW(
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: false,
                    ExtendedStartupInfoPresent | additionalCreationFlags,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out ProcessInformation processInformation))
                return CreateWin32Failure($"Starting '{executablePath}'", parentProcessID);

            try
            {
                return new ProcessLaunchResult(
                    Succeeded: true,
                    ProcessID: checked((int)processInformation.ProcessID),
                    ParentProcessID: parentProcessID,
                    ErrorMessage: string.Empty);
            }
            finally
            {
                _ = Kernel32.CloseHandle(processInformation.ThreadHandle);
                _ = Kernel32.CloseHandle(processInformation.ProcessHandle);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException or OutOfMemoryException)
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: parentProcessID,
                exception.Message);
        }
        finally
        {
            if (parentHandleValue != IntPtr.Zero) Marshal.FreeHGlobal(parentHandleValue);
            if (isAttributeListInitialized) DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
        }
    }

    internal static StringBuilder BuildCommandLine(
        string executablePath,
        IReadOnlyList<string> arguments)
    {
        StringBuilder commandLine = new();
        AppendQuotedCommandLineArgument(commandLine, executablePath);
        foreach (string argument in arguments)
        {
            commandLine.Append(' ');
            AppendQuotedCommandLineArgument(commandLine, argument);
        }

        return commandLine;
    }

    private static bool TryGetDesktopFolderViewDispatch(
        out IntPtr folderViewDispatch,
        out int errorCode,
        out string errorMessage)
    {
        folderViewDispatch = IntPtr.Zero;
        Guid shellWindowsClassID = ShellWindowsClassID;
        Guid shellWindowsInterfaceID = ShellWindowsInterfaceID;
        int activationResult = CoCreateInstance(
            in shellWindowsClassID,
            IntPtr.Zero,
            ClassContextAll,
            in shellWindowsInterfaceID,
            out IntPtr shellWindows);
        if (activationResult < 0 || shellWindows == IntPtr.Zero)
        {
            errorCode = ExtractWin32Error(activationResult);
            errorMessage = DescribeHResult(
                activationResult,
                "The Windows Explorer automation service could not be opened");
            return false;
        }

        try
        {
            AutomationVariant desktopLocation = AutomationVariant.FromInt32(value: 0);
            AutomationVariant locationRoot = default;
            IntPtr desktopWindowDispatch = IntPtr.Zero;
            int desktopWindowHandle = 0;
            void** shellWindowsVirtualTable = *(void***)shellWindows;
            delegate* unmanaged[Stdcall]<IntPtr, AutomationVariant*, AutomationVariant*, int,
                int*, int, IntPtr*, int> findWindow =
                (delegate* unmanaged[Stdcall]<IntPtr, AutomationVariant*, AutomationVariant*, int,
                    int*, int, IntPtr*, int>)shellWindowsVirtualTable[15];
            int findResult = findWindow(
                shellWindows,
                &desktopLocation,
                &locationRoot,
                DesktopShellWindowClass,
                &desktopWindowHandle,
                ShellWindowFindNeedsDispatch,
                &desktopWindowDispatch);
            if (findResult < 0 || desktopWindowDispatch == IntPtr.Zero)
            {
                errorCode = ExtractWin32Error(findResult);
                errorMessage = DescribeHResult(
                    findResult,
                    "The Windows desktop Explorer window could not be found");
                return false;
            }

            try
            {
                Guid serviceProviderInterfaceID = ServiceProviderInterfaceID;
                int queryResult = QueryCOMInterface(
                    desktopWindowDispatch,
                    in serviceProviderInterfaceID,
                    out IntPtr serviceProvider);
                if (queryResult < 0 || serviceProvider == IntPtr.Zero)
                {
                    errorCode = ExtractWin32Error(queryResult);
                    errorMessage = DescribeHResult(
                        queryResult,
                        "The desktop Explorer service provider could not be opened");
                    return false;
                }

                try
                {
                    Guid topLevelBrowserServiceID = TopLevelBrowserServiceID;
                    Guid shellBrowserInterfaceID = ShellBrowserInterfaceID;
                    void** serviceProviderVirtualTable = *(void***)serviceProvider;
                    delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int> queryService =
                        (delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)
                        serviceProviderVirtualTable[3];
                    IntPtr shellBrowser = IntPtr.Zero;
                    int serviceResult = queryService(
                        serviceProvider,
                        &topLevelBrowserServiceID,
                        &shellBrowserInterfaceID,
                        &shellBrowser);
                    if (serviceResult < 0 || shellBrowser == IntPtr.Zero)
                    {
                        errorCode = ExtractWin32Error(serviceResult);
                        errorMessage = DescribeHResult(
                            serviceResult,
                            "The desktop Explorer browser service could not be opened");
                        return false;
                    }

                    try
                    {
                        void** shellBrowserVirtualTable = *(void***)shellBrowser;
                        delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int> queryActiveShellView =
                            (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)
                            shellBrowserVirtualTable[15];
                        IntPtr shellView = IntPtr.Zero;
                        int viewResult = queryActiveShellView(shellBrowser, &shellView);
                        if (viewResult < 0 || shellView == IntPtr.Zero)
                        {
                            errorCode = ExtractWin32Error(viewResult);
                            errorMessage = DescribeHResult(
                                viewResult,
                                "The desktop Explorer view could not be opened");
                            return false;
                        }

                        try
                        {
                            Guid dispatchInterfaceID = DispatchInterfaceID;
                            void** shellViewVirtualTable = *(void***)shellView;
                            delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int> getItemObject =
                                (delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)
                                shellViewVirtualTable[15];
                            IntPtr resolvedFolderViewDispatch = IntPtr.Zero;
                            int itemResult = getItemObject(
                                shellView,
                                ShellViewBackground,
                                &dispatchInterfaceID,
                                &resolvedFolderViewDispatch);
                            folderViewDispatch = resolvedFolderViewDispatch;
                            if (itemResult < 0 || folderViewDispatch == IntPtr.Zero)
                            {
                                errorCode = ExtractWin32Error(itemResult);
                                errorMessage = DescribeHResult(
                                    itemResult,
                                    "The desktop Explorer folder view could not be opened");
                                return false;
                            }

                            errorCode = 0;
                            errorMessage = string.Empty;
                            return true;
                        }
                        finally
                        {
                            ReleaseCOMInterface(shellView);
                        }
                    }
                    finally
                    {
                        ReleaseCOMInterface(shellBrowser);
                    }
                }
                finally
                {
                    ReleaseCOMInterface(serviceProvider);
                }
            }
            finally
            {
                ReleaseCOMInterface(desktopWindowDispatch);
            }
        }
        finally
        {
            ReleaseCOMInterface(shellWindows);
        }
    }

    private static int QueryCOMInterface(
        IntPtr sourceInterface,
        in Guid requestedInterfaceID,
        out IntPtr requestedInterface)
    {
        requestedInterface = IntPtr.Zero;
        Guid requestedInterfaceIDValue = requestedInterfaceID;
        IntPtr resolvedInterface = IntPtr.Zero;
        void** virtualTable = *(void***)sourceInterface;
        delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int> queryInterface =
            (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)virtualTable[0];
        int result = queryInterface(
            sourceInterface,
            &requestedInterfaceIDValue,
            &resolvedInterface);
        requestedInterface = resolvedInterface;
        return result;
    }

    private static bool TryGetApplicationDispatch(
        IntPtr folderViewDispatch,
        out IntPtr applicationDispatch,
        out AutomationVariant applicationVariant,
        out int errorCode,
        out string errorMessage)
    {
        applicationDispatch = IntPtr.Zero;
        applicationVariant = default;
        if (!TryGetDispatchID(folderViewDispatch, "Application", out int applicationDispatchID,
                out errorCode, out errorMessage))
            return false;

        DispatchParameters parameters = default;
        int invocationResult = InvokeDispatch(
            folderViewDispatch,
            applicationDispatchID,
            DispatchPropertyGet,
            ref parameters,
            ref applicationVariant,
            out string exceptionDescription,
            out int exceptionResult);
        if (invocationResult < 0)
        {
            int effectiveResult = exceptionResult < 0 ? exceptionResult : invocationResult;
            errorCode = ExtractWin32Error(effectiveResult);
            errorMessage = string.IsNullOrWhiteSpace(exceptionDescription)
                ? DescribeHResult(effectiveResult, "Reading the Explorer application object failed")
                : exceptionDescription;
            ClearVariant(ref applicationVariant);
            return false;
        }

        if (applicationVariant.Type != VariantTypeDispatch ||
            applicationVariant.PointerValue == IntPtr.Zero)
        {
            errorCode = Win32ErrorInvalidData;
            errorMessage = "Explorer returned an invalid application automation object.";
            ClearVariant(ref applicationVariant);
            return false;
        }

        applicationDispatch = applicationVariant.PointerValue;
        errorCode = 0;
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryInvokeShellExecute(
        IntPtr applicationDispatch,
        string target,
        string arguments,
        string workingDirectory,
        string verb,
        out int errorCode,
        out string errorMessage)
    {
        if (!TryGetDispatchID(applicationDispatch, "ShellExecute", out int shellExecuteDispatchID,
                out errorCode, out errorMessage))
            return false;

        AutomationVariant* argumentsPointer = stackalloc AutomationVariant[5];
        argumentsPointer[0] = AutomationVariant.FromInt32(ShowNormal);
        argumentsPointer[1] = AutomationVariant.FromBString(verb);
        argumentsPointer[2] = AutomationVariant.FromBString(workingDirectory);
        argumentsPointer[3] = AutomationVariant.FromBString(arguments);
        argumentsPointer[4] = AutomationVariant.FromBString(target);
        AutomationVariant resultVariant = default;
        try
        {
            DispatchParameters parameters = new()
            {
                Arguments = argumentsPointer,
                ArgumentCount = 5
            };
            int invocationResult = InvokeDispatch(
                applicationDispatch,
                shellExecuteDispatchID,
                DispatchMethod,
                ref parameters,
                ref resultVariant,
                out string exceptionDescription,
                out int exceptionResult);
            if (invocationResult < 0)
            {
                int effectiveResult = exceptionResult < 0 ? exceptionResult : invocationResult;
                errorCode = ExtractWin32Error(effectiveResult);
                errorMessage = string.IsNullOrWhiteSpace(exceptionDescription)
                    ? DescribeHResult(effectiveResult, $"Explorer could not launch '{target}'")
                    : exceptionDescription;
                return false;
            }

            if (resultVariant.Type == VariantTypeInt32 && resultVariant.Int32Value is > 0 and <= 32)
            {
                errorCode = resultVariant.Int32Value;
                errorMessage = new Win32Exception(errorCode).Message;
                return false;
            }

            errorCode = 0;
            errorMessage = string.Empty;
            return true;
        }
        finally
        {
            ClearVariant(ref resultVariant);
            for (int argumentIndex = 0; argumentIndex < 5; argumentIndex++)
                ClearVariant(ref argumentsPointer[argumentIndex]);
        }
    }

    private static bool TryGetDispatchID(
        IntPtr dispatch,
        string memberName,
        out int dispatchID,
        out int errorCode,
        out string errorMessage)
    {
        dispatchID = 0;
        Guid emptyInterfaceID = Guid.Empty;
        fixed (char* memberNameCharacters = memberName)
        {
            char* memberNamePointer = memberNameCharacters;
            int resolvedDispatchID = 0;
            void** virtualTable = *(void***)dispatch;
            delegate* unmanaged[Stdcall]<IntPtr, Guid*, char**, uint, uint, int*, int> getIDsOfNames =
                (delegate* unmanaged[Stdcall]<IntPtr, Guid*, char**, uint, uint, int*, int>)virtualTable[5];
            int result = getIDsOfNames(
                dispatch,
                &emptyInterfaceID,
                &memberNamePointer,
                1,
                LocaleUserDefault,
                &resolvedDispatchID);
            dispatchID = resolvedDispatchID;
            if (result >= 0)
            {
                errorCode = 0;
                errorMessage = string.Empty;
                return true;
            }

            errorCode = ExtractWin32Error(result);
            errorMessage = DescribeHResult(result, $"Explorer does not expose '{memberName}'");
            return false;
        }
    }

    private static int InvokeDispatch(
        IntPtr dispatch,
        int dispatchID,
        uint flags,
        ref DispatchParameters parameters,
        ref AutomationVariant resultVariant,
        out string exceptionDescription,
        out int exceptionResult)
    {
        Guid emptyInterfaceID = Guid.Empty;
        ExceptionInfo exceptionInfo = default;
        uint argumentErrorIndex = 0;
        int result;
        fixed (DispatchParameters* parametersPointer = &parameters)
        fixed (AutomationVariant* resultPointer = &resultVariant)
        {
            void** virtualTable = *(void***)dispatch;
            delegate* unmanaged[Stdcall]<IntPtr, int, Guid*, uint, ushort, DispatchParameters*,
                AutomationVariant*, ExceptionInfo*, uint*, int> invoke =
                (delegate* unmanaged[Stdcall]<IntPtr, int, Guid*, uint, ushort, DispatchParameters*,
                    AutomationVariant*, ExceptionInfo*, uint*, int>)virtualTable[6];
            result = invoke(
                dispatch,
                dispatchID,
                &emptyInterfaceID,
                LocaleUserDefault,
                checked((ushort)flags),
                parametersPointer,
                resultPointer,
                &exceptionInfo,
                &argumentErrorIndex);
        }

        exceptionDescription = exceptionInfo.Description == IntPtr.Zero
            ? string.Empty
            : Marshal.PtrToStringBSTR(exceptionInfo.Description);
        exceptionResult = exceptionInfo.Result;
        FreeExceptionInfo(ref exceptionInfo);
        return result;
    }

    /// <summary>Opens the desktop Explorer process before a shell restart terminates it.</summary>
    public static bool TryOpenDesktopExplorerParent(
        out ProcessParentHandle? parent,
        out string errorMessage)
    {
        parent = null;
        IntPtr shellWindow = GetShellWindow();
        if (shellWindow == IntPtr.Zero)
        {
            errorMessage = "The Windows desktop Explorer process is not available.";
            return false;
        }

        _ = GetWindowThreadProcessId(shellWindow, out uint processID);
        if (processID == 0 || processID > int.MaxValue)
        {
            errorMessage = "The Windows desktop Explorer process ID could not be resolved.";
            return false;
        }

        return TryOpenProcessAsParent(checked((int)processID), out parent, out errorMessage);
    }

    internal static bool TryOpenProcessAsParent(
        int processID,
        out ProcessParentHandle? parent,
        out string errorMessage)
    {
        parent = null;
        if (processID <= 0 || processID == Environment.ProcessId)
        {
            errorMessage = "The requested parent process is invalid.";
            return false;
        }

        IntPtr processHandle = Kernel32.OpenProcess(
            ProcessCreateProcess,
            bInheritHandle: false,
            (uint)processID);
        if (processHandle == IntPtr.Zero)
        {
            int openError = Marshal.GetLastWin32Error();
            errorMessage = $"Process {processID} cannot own a new process: " +
                           new Win32Exception(openError).Message;
            return false;
        }

        parent = new ProcessParentHandle(processHandle, processID);
        errorMessage = string.Empty;
        return true;
    }

    private static void AppendQuotedCommandLineArgument(StringBuilder commandLine, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            commandLine.Append(argument);
            return;
        }

        commandLine.Append('"');
        int pendingBackslashCount = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                pendingBackslashCount++;
                continue;
            }

            if (character == '"')
            {
                commandLine.Append('\\', pendingBackslashCount * 2 + 1);
                commandLine.Append('"');
                pendingBackslashCount = 0;
                continue;
            }

            commandLine.Append('\\', pendingBackslashCount);
            commandLine.Append(character);
            pendingBackslashCount = 0;
        }

        commandLine.Append('\\', pendingBackslashCount * 2);
        commandLine.Append('"');
    }

    private static ProcessLaunchResult CreateWin32Failure(string operation, int parentProcessID)
    {
        int errorCode = Marshal.GetLastWin32Error();
        return new ProcessLaunchResult(
            Succeeded: false,
            ProcessID: 0,
            ParentProcessID: parentProcessID,
            ErrorMessage: $"{operation} failed: {new Win32Exception(errorCode).Message}");
    }

    private static int ExtractWin32Error(int hResult) =>
        (hResult & HResultFacilityMask) == HResultFacilityWin32
            ? hResult & HResultCodeMask
            : 0;

    private static string DescribeHResult(int hResult, string operation) =>
        $"{operation}: HRESULT 0x{unchecked((uint)hResult):X8}.";

    private static void ClearVariant(ref AutomationVariant variant)
    {
        if (variant.Type == VariantTypeEmpty) return;
        fixed (AutomationVariant* variantPointer = &variant)
            _ = VariantClear(variantPointer);
        variant = default;
    }

    private static void FreeExceptionInfo(ref ExceptionInfo exceptionInfo)
    {
        FreeBString(ref exceptionInfo.Source);
        FreeBString(ref exceptionInfo.Description);
        FreeBString(ref exceptionInfo.HelpFile);
    }

    private static void FreeBString(ref IntPtr value)
    {
        if (value == IntPtr.Zero) return;
        SysFreeString(value);
        value = IntPtr.Zero;
    }

    private static void ReleaseCOMInterface(IntPtr interfacePointer)
    {
        if (interfacePointer == IntPtr.Zero) return;
        void** virtualTable = *(void***)interfacePointer;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)virtualTable[2];
        _ = release(interfacePointer);
    }

    private const int HResultCodeMask = 0x0000FFFF;
    private const int HResultFacilityMask = 0x1FFF0000;
    private const int HResultFacilityWin32 = 0x00070000;
    private const int HResultFalse = 1;
    private const int HResultSuccess = 0;
    private const int Win32ErrorInvalidData = 13;
    private const int Win32ErrorInvalidParameter = 87;

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct AutomationVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public int Int32Value;
        [FieldOffset(8)] public IntPtr PointerValue;

        public static AutomationVariant FromInt32(int value) =>
            new() { Type = VariantTypeInt32, Int32Value = value };

        public static AutomationVariant FromBString(string value) =>
            new() { Type = VariantTypeBString, PointerValue = Marshal.StringToBSTR(value) };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatchParameters
    {
        public AutomationVariant* Arguments;
        public int* NamedArguments;
        public uint ArgumentCount;
        public uint NamedArgumentCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionInfo
    {
        public ushort Code;
        public ushort Reserved;
        public IntPtr Source;
        public IntPtr Description;
        public IntPtr HelpFile;
        public uint HelpContext;
        public IntPtr ReservedPointer;
        public IntPtr DeferredFillIn;
        public int Result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountCharacters;
        public uint YCountCharacters;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessID;
        public uint ThreadID;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        in Guid classID,
        IntPtr outerUnknown,
        uint classContext,
        in Guid interfaceID,
        out IntPtr instance);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint concurrencyModel);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processID);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        uint attributeCount,
        uint flags,
        ref nuint size);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern void SysFreeString(IntPtr bString);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        nuint attribute,
        IntPtr value,
        nuint size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int VariantClear(AutomationVariant* variant);
}
