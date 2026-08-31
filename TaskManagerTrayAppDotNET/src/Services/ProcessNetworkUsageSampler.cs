using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Collects the per-process network byte stream used by Windows Task Manager.</summary>
internal sealed unsafe class ProcessNetworkUsageSampler : IDisposable
{
    private const string SRUMModuleName = "srumapi.dll";
    private const string RegisterExportName = "SruRegisterRealTimeStats";
    private const string UnregisterExportName = "SruUnregisterRealTimeStats";
    private const string FreeRecordSetExportName = "SruFreeRecordSet";
    private const uint NetworkProviderClass = 0;
    private const uint CurrentUserScope = 1;
    private const uint AllUsersScope = 2;
    private const ushort SentBytesColumnID = 3;
    private const ushort ReceivedBytesColumnID = 4;
    private const ushort ProcessIDColumnID = 6;
    private const uint COMInitializeApartmentThreaded = 2;
    private const uint COMInitializeDisableOLE1DDE = 4;
    private const int RPCChangedMode = unchecked((int)0x80010106);
    private const uint Infinite = uint.MaxValue;
    private const uint WaitFailed = uint.MaxValue;
    private const uint QueueStatusAllInput = 0x04FF;
    private const uint MessageWaitInputAvailable = 0x0004;
    private const uint PeekMessageRemove = 0x0001;
    private const int RetryIntervalMilliseconds = 30_000;
    private const int ShutdownJoinTimeoutMilliseconds = 5_000;
    private const uint MaximumRecordCount = 65_536;
    private const ushort MaximumColumnCount = 256;

    private readonly Lock _counterGate = new();
    private readonly Dictionary<int, ulong> _cumulativeBytes = new(256);
    private readonly ManualResetEvent _shutdown = new(false);
    private readonly Thread _workerThread;
    private int _available;
    private int _failureLogged;
    private int _callbackFailureLogged;
    private int _disposed;
    private long _sampleGeneration;
    private long _sampleTimestamp;

    public ProcessNetworkUsageSampler()
    {
        _workerThread = new Thread(Run)
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".NetworkSampler",
            Priority = ThreadPriority.BelowNormal
        };
        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();
    }

    /// <summary>Gets whether registration has published its initial sample.</summary>
    public bool IsSampleAvailable
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _available) == 0) return false;

            lock (_counterGate)
                return _sampleGeneration != 0;
        }
    }

    /// <summary>Reads the latest cumulative SRUM bytes and provider-driven sample identity.</summary>
    public bool TryReadSample(int processID, out ProcessNetworkUsageSample sample)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _available) == 0)
        {
            sample = default;
            return false;
        }

        lock (_counterGate)
        {
            if (_sampleGeneration == 0)
            {
                sample = default;
                return false;
            }

            _cumulativeBytes.TryGetValue(processID, out ulong cumulativeBytes);
            sample = new ProcessNetworkUsageSample(
                cumulativeBytes,
                _sampleGeneration,
                _sampleTimestamp);
            return true;
        }
    }

    private void Run()
    {
        int initializeResult = CoInitializeEx(
            IntPtr.Zero,
            COMInitializeApartmentThreaded | COMInitializeDisableOLE1DDE);
        if (initializeResult == RPCChangedMode)
            initializeResult = CoInitializeEx(IntPtr.Zero, COMInitializeDisableOLE1DDE);
        if (initializeResult < 0)
        {
            LogFailureOnce($"COM initialization failed (0x{initializeResult:X8}).");
            return;
        }

        try
        {
            while (!_shutdown.WaitOne(0))
            {
                if (RunRegistration()) return;
                if (_shutdown.WaitOne(RetryIntervalMilliseconds)) return;
            }
        }
        catch (Exception exception)
        {
            LogFailureOnce(exception.ToString());
        }
        finally
        {
            Volatile.Write(ref _available, value: 0);
            CoUninitialize();
        }
    }

    /// <summary>Returns true after a successful registration has run through shutdown.</summary>
    private bool RunRegistration()
    {
        if (!TryLoadExports(out SRUMExports exports))
        {
            LogFailureOnce("Required srumapi.dll exports are unavailable.");
            return false;
        }

        GCHandle callbackContext = GCHandle.Alloc(this, GCHandleType.Normal);
        IntPtr registration = IntPtr.Zero;
        IntPtr initialRecordSet = IntPtr.Zero;
        try
        {
            ResetSampleState();
            SYSTEMTIME startTime;
            GetSystemTime(&startTime);
            uint requestedScope = IsElevated() ? AllUsersScope : CurrentUserScope;
            uint status = exports.Register(
                NetworkProviderClass,
                &startTime,
                requestedScope,
                GCHandle.ToIntPtr(callbackContext),
                &OnRecordSet,
                &registration,
                &initialRecordSet);
            if (status != 0 && requestedScope == AllUsersScope)
            {
                ReleaseRegistration(exports, ref registration, ref initialRecordSet);
                ResetSampleState();
                status = exports.Register(
                    NetworkProviderClass,
                    &startTime,
                    CurrentUserScope,
                    GCHandle.ToIntPtr(callbackContext),
                    &OnRecordSet,
                    &registration,
                    &initialRecordSet);
            }

            if (status != 0 || registration == IntPtr.Zero)
            {
                LogFailureOnce($"SruRegisterRealTimeStats failed ({status}).");
                return false;
            }

            if (!TryPublishInitialSample(initialRecordSet))
            {
                LogFailureOnce("The initial SRUM record set was invalid.");
                return false;
            }

            Volatile.Write(ref _available, value: 1);
            return WaitForShutdownWithMessagePump();
        }
        finally
        {
            Volatile.Write(ref _available, value: 0);
            ReleaseRegistration(exports, ref registration, ref initialRecordSet);
            callbackContext.Free();
            NativeLibrary.Free(exports.Module);
        }
    }

    private bool WaitForShutdownWithMessagePump()
    {
        IntPtr shutdownHandle = _shutdown.SafeWaitHandle.DangerousGetHandle();
        while (!_shutdown.WaitOne(0))
        {
            uint waitResult = MsgWaitForMultipleObjectsEx(
                count: 1,
                &shutdownHandle,
                Infinite,
                QueueStatusAllInput,
                MessageWaitInputAvailable);
            if (waitResult == 0) return true;
            if (waitResult == WaitFailed)
            {
                LogFailureOnce($"The SRUM message pump failed ({Marshal.GetLastPInvokeError()}).");
                return false;
            }

            if (waitResult != 1) continue;

            while (!_shutdown.WaitOne(0)
                   && PeekMessageW(out MSG message, IntPtr.Zero, messageFilterMinimum: 0, messageFilterMaximum: 0,
                       PeekMessageRemove))
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }
        }

        return true;
    }

    private void ResetSampleState()
    {
        lock (_counterGate)
        {
            _cumulativeBytes.Clear();
            _sampleGeneration = 0;
            _sampleTimestamp = 0;
        }
    }

    /// <summary>Publishes registration's one-time poll as the baseline for every process.</summary>
    private bool TryPublishInitialSample(IntPtr recordSetAddress)
    {
        lock (_counterGate)
        {
            if (!AccumulateInitialRecordSet(recordSetAddress, _cumulativeBytes)) return false;

            _sampleTimestamp = Stopwatch.GetTimestamp();
            _sampleGeneration++;
            return true;
        }
    }

    private static void ReleaseRegistration(
        SRUMExports exports,
        ref IntPtr registration,
        ref IntPtr initialRecordSet)
    {
        if (registration != IntPtr.Zero)
        {
            exports.Unregister(registration);
            registration = IntPtr.Zero;
        }

        if (initialRecordSet == IntPtr.Zero) return;
        exports.FreeRecordSet(initialRecordSet);
        initialRecordSet = IntPtr.Zero;
    }

    private static bool TryLoadExports(out SRUMExports exports)
    {
        exports = default;
        string modulePath = Path.Combine(Environment.SystemDirectory, SRUMModuleName);
        if (!NativeLibrary.TryLoad(modulePath, out IntPtr module)) return false;

        if (!NativeLibrary.TryGetExport(module, RegisterExportName, out IntPtr registerAddress)
            || !NativeLibrary.TryGetExport(module, UnregisterExportName, out IntPtr unregisterAddress)
            || !NativeLibrary.TryGetExport(module, FreeRecordSetExportName, out IntPtr freeRecordSetAddress))
        {
            NativeLibrary.Free(module);
            return false;
        }

        exports = new SRUMExports(
            module,
            (delegate* unmanaged[Stdcall]<
                uint,
                SYSTEMTIME*,
                uint,
                IntPtr,
                delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>,
                IntPtr*,
                IntPtr*,
                uint>)registerAddress,
            (delegate* unmanaged[Stdcall]<IntPtr, void>)unregisterAddress,
            (delegate* unmanaged[Stdcall]<IntPtr, void>)freeRecordSetAddress);
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnRecordSet(IntPtr callbackContext, IntPtr recordSetAddress)
    {
        try
        {
            GCHandle contextHandle = GCHandle.FromIntPtr(callbackContext);
            if (contextHandle.Target is not ProcessNetworkUsageSampler sampler) return;

            lock (sampler._counterGate)
            {
                if (!AccumulateRecordSet(recordSetAddress, sampler._cumulativeBytes)) return;

                sampler._sampleTimestamp = Stopwatch.GetTimestamp();
                sampler._sampleGeneration++;
            }
        }
        catch (Exception exception)
        {
            try
            {
                GCHandle contextHandle = GCHandle.FromIntPtr(callbackContext);
                if (contextHandle.Target is ProcessNetworkUsageSampler sampler
                    && Interlocked.Exchange(ref sampler._callbackFailureLogged, value: 1) == 0)
                    TADNLog.Log($"ProcessNetworkUsageSampler callback failed: {exception}");
            }
            catch
            {
                // Never allow managed exceptions to cross the native callback boundary
            }
        }
    }

    /// <summary>Copies borrowed SRUM records into caller-owned cumulative counters.</summary>
    internal static bool AccumulateRecordSet(
        IntPtr recordSetAddress,
        Dictionary<int, ulong> cumulativeBytes)
    {
        ArgumentNullException.ThrowIfNull(cumulativeBytes);
        if (recordSetAddress == IntPtr.Zero) return false;

        SRU_STATS_RECORD_SET* recordSet = (SRU_STATS_RECORD_SET*)recordSetAddress;
        if (recordSet->Count > MaximumRecordCount
            || (recordSet->Count > 0 && recordSet->Records == null))
            return false;

        for (uint recordIndex = 0; recordIndex < recordSet->Count; recordIndex++)
        {
            SRU_STATS_RECORD* record = recordSet->Records + recordIndex;
            if (record->UserSID == IntPtr.Zero
                || record->ColumnCount == 0
                || record->ColumnCount > MaximumColumnCount
                || record->Columns == null)
                continue;

            uint processIDValue = uint.MaxValue;
            ulong transferredBytes = 0;
            for (int columnIndex = 0; columnIndex < record->ColumnCount; columnIndex++)
            {
                SRU_STATS_COLUMN* column = record->Columns + columnIndex;
                switch (column->ID)
                {
                    case SentBytesColumnID:
                    case ReceivedBytesColumnID:
                        transferredBytes = SaturatingAdd(transferredBytes, column->UnsignedValue);
                        break;
                    case ProcessIDColumnID:
                        processIDValue = column->UnsignedIntegerValue;
                        break;
                }
            }

            if (processIDValue > int.MaxValue || transferredBytes == 0) continue;

            int processID = (int)processIDValue;
            cumulativeBytes.TryGetValue(processID, out ulong previousBytes);
            cumulativeBytes[processID] = SaturatingAdd(previousBytes, transferredBytes);
        }

        return true;
    }

    /// <summary>Treats a successful registration without records as an empty initial poll.</summary>
    internal static bool AccumulateInitialRecordSet(
        IntPtr recordSetAddress,
        Dictionary<int, ulong> cumulativeBytes)
    {
        ArgumentNullException.ThrowIfNull(cumulativeBytes);
        return recordSetAddress == IntPtr.Zero
               || AccumulateRecordSet(recordSetAddress, cumulativeBytes);
    }

    private static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private void LogFailureOnce(string message)
    {
        if (Interlocked.Exchange(ref _failureLogged, value: 1) != 0) return;
        TADNLog.Log($"ProcessNetworkUsageSampler: {message}");
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        _shutdown.Set();
        if (!_workerThread.Join(ShutdownJoinTimeoutMilliseconds))
        {
            TADNLog.Log("ProcessNetworkUsageSampler did not stop before the shutdown timeout.");
            return;
        }

        _shutdown.Dispose();
        lock (_counterGate)
            _cumulativeBytes.Clear();
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInitialize);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("kernel32.dll")]
    private static extern void GetSystemTime(SYSTEMTIME* systemTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(
        uint count,
        IntPtr* handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(
        out MSG message,
        IntPtr window,
        uint messageFilterMinimum,
        uint messageFilterMaximum,
        uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG message);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public POINT Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SRU_STATS_RECORD_SET
    {
        public uint Count;
        private uint _padding;
        public SRU_STATS_RECORD* Records;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct SRU_STATS_RECORD
    {
        [FieldOffset(40)]
        public IntPtr UserSID;

        [FieldOffset(48)]
        public ushort ColumnCount;

        [FieldOffset(56)]
        public SRU_STATS_COLUMN* Columns;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct SRU_STATS_COLUMN
    {
        [FieldOffset(0)]
        public ushort ID;

        [FieldOffset(8)]
        public ulong UnsignedValue;

        [FieldOffset(8)]
        public uint UnsignedIntegerValue;
    }

    private readonly struct SRUMExports(
        IntPtr module,
        delegate* unmanaged[Stdcall]<
            uint,
            SYSTEMTIME*,
            uint,
            IntPtr,
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>,
            IntPtr*,
            IntPtr*,
            uint> register,
        delegate* unmanaged[Stdcall]<IntPtr, void> unregister,
        delegate* unmanaged[Stdcall]<IntPtr, void> freeRecordSet)
    {
        public IntPtr Module { get; } = module;

        public delegate* unmanaged[Stdcall]<
            uint,
            SYSTEMTIME*,
            uint,
            IntPtr,
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>,
            IntPtr*,
            IntPtr*,
            uint> Register { get; } = register;

        public delegate* unmanaged[Stdcall]<IntPtr, void> Unregister { get; } = unregister;
        public delegate* unmanaged[Stdcall]<IntPtr, void> FreeRecordSet { get; } = freeRecordSet;
    }
}

internal readonly record struct ProcessNetworkUsageSample(
    ulong CumulativeBytes,
    long Generation,
    long Timestamp);
