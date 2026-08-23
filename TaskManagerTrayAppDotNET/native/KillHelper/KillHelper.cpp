#include "KillHelperProtocol.h"

#include <cstdint>
#include <cwchar>

#define KILL_HELPER_WAIT_HANDLE_COUNT 2U
#define KILL_HELPER_MAX_PAYLOAD_READ_ATTEMPTS 1024U
#define KILL_HELPER_MINIMUM_WORKING_SET_BYTES (512U * 1024U)
#define KILL_HELPER_HOT_CODE __declspec(code_seg(".killhot")) __declspec(noinline)

namespace TaskManagerTrayAppDotNET
{
    using CloseHandleFunction = decltype(&::CloseHandle);
    using GetProcessTimesFunction = decltype(&::GetProcessTimes);
    using IsProcessCriticalFunction = decltype(&::IsProcessCritical);
    using OpenProcessFunction = decltype(&::OpenProcess);
    using SetEventFunction = decltype(&::SetEvent);
    using TerminateProcessFunction = decltype(&::TerminateProcess);
    using WaitForMultipleObjectsFunction = decltype(&::WaitForMultipleObjects);

    struct CriticalState
    {
        HANDLE TargetProcessHandle;
        KillHelperTarget Target;
        CloseHandleFunction CloseHandleCall;
        GetProcessTimesFunction GetProcessTimesCall;
        IsProcessCriticalFunction IsProcessCriticalCall;
        OpenProcessFunction OpenProcessCall;
        SetEventFunction SetEventCall;
        TerminateProcessFunction TerminateProcessCall;
        WaitForMultipleObjectsFunction WaitForMultipleObjectsCall;
    };

    struct FireResult
    {
        LONG Result;
        DWORD Error;
    };

    struct HelperArguments
    {
        DWORD ParentProcessID;
        std::uintptr_t MappingHandleValue;
        std::uintptr_t RequestEventHandleValue;
        std::uintptr_t ResponseEventHandleValue;
    };

    alignas(4096) static CriticalState s_criticalState{};

    KILL_HELPER_HOT_CODE static void PublishFireResult(
        KillHelperMailbox* mailbox,
        HANDLE responseEvent,
        LONG64 requestSequence,
        LONG processID,
        const FireResult& result) noexcept;
    KILL_HELPER_HOT_CODE static DWORD RunRequestLoop(
        KillHelperMailbox* mailbox,
        HANDLE requestEvent,
        HANDLE responseEvent,
        HANDLE parentProcess,
        DWORD parentProcessID) noexcept;

    /// Reads a shared 64-bit value with an interlocked acquire barrier.
    KILL_HELPER_HOT_CODE static LONG64 ReadSharedLONG64(volatile LONG64* value) noexcept
    {
        return InterlockedCompareExchange64(value, 0, 0);
    }

    /// Reads a consistent arm payload while the managed process may update it.
    KILL_HELPER_HOT_CODE static bool TryReadArmTarget(
        KillHelperMailbox* mailbox,
        KillHelperTarget* target) noexcept
    {
        for (std::uint32_t attempt = 0; attempt < KILL_HELPER_MAX_PAYLOAD_READ_ATTEMPTS; attempt++)
        {
            LONG64 sequenceBefore = ReadSharedLONG64(&mailbox->ArmPayloadSequence);
            if ((sequenceBefore & 1LL) != 0)
            {
                YieldProcessor();
                continue;
            }

            KillHelperTarget candidate{};
            candidate.ProcessID = mailbox->ArmProcessID;
            candidate.CreationTime = mailbox->ArmCreationTime;
            candidate.Generation = mailbox->ArmGeneration;
            MemoryBarrier();

            LONG64 sequenceAfter = ReadSharedLONG64(&mailbox->ArmPayloadSequence);
            if (sequenceBefore == sequenceAfter)
            {
                *target = candidate;
                return true;
            }
        }

        return false;
    }

    /// Reads a consistent fire payload while the managed process may update it.
    KILL_HELPER_HOT_CODE static bool TryReadFireTarget(
        KillHelperMailbox* mailbox,
        KillHelperTarget* target,
        DWORD* exitCode) noexcept
    {
        for (std::uint32_t attempt = 0; attempt < KILL_HELPER_MAX_PAYLOAD_READ_ATTEMPTS; attempt++)
        {
            LONG64 sequenceBefore = ReadSharedLONG64(&mailbox->FirePayloadSequence);
            if ((sequenceBefore & 1LL) != 0)
            {
                YieldProcessor();
                continue;
            }

            KillHelperTarget candidate{};
            candidate.ProcessID = mailbox->FireProcessID;
            candidate.CreationTime = mailbox->FireCreationTime;
            candidate.Generation = mailbox->FireGeneration;
            DWORD candidateExitCode = mailbox->FireExitCode;
            MemoryBarrier();

            LONG64 sequenceAfter = ReadSharedLONG64(&mailbox->FirePayloadSequence);
            if (sequenceBefore == sequenceAfter)
            {
                *target = candidate;
                *exitCode = candidateExitCode;
                return true;
            }
        }

        return false;
    }

    /// Combines a Win32 FILETIME into its protocol representation.
    KILL_HELPER_HOT_CODE static LONG64 FileTimeToLONG64(const FILETIME& fileTime) noexcept
    {
        ULARGE_INTEGER value{};
        value.LowPart = fileTime.dwLowDateTime;
        value.HighPart = fileTime.dwHighDateTime;
        return static_cast<LONG64>(value.QuadPart);
    }

    /// Resolves hot-path kernel calls into the locked critical-state page.
    static bool ResolveCriticalFunctions() noexcept
    {
        HMODULE kernelModule = GetModuleHandleW(L"kernel32.dll");
        if (kernelModule == nullptr)
            return false;

        s_criticalState.CloseHandleCall = reinterpret_cast<CloseHandleFunction>(
            GetProcAddress(kernelModule, "CloseHandle"));
        s_criticalState.GetProcessTimesCall = reinterpret_cast<GetProcessTimesFunction>(
            GetProcAddress(kernelModule, "GetProcessTimes"));
        s_criticalState.IsProcessCriticalCall = reinterpret_cast<IsProcessCriticalFunction>(
            GetProcAddress(kernelModule, "IsProcessCritical"));
        s_criticalState.OpenProcessCall = reinterpret_cast<OpenProcessFunction>(
            GetProcAddress(kernelModule, "OpenProcess"));
        s_criticalState.SetEventCall = reinterpret_cast<SetEventFunction>(
            GetProcAddress(kernelModule, "SetEvent"));
        s_criticalState.TerminateProcessCall = reinterpret_cast<TerminateProcessFunction>(
            GetProcAddress(kernelModule, "TerminateProcess"));
        s_criticalState.WaitForMultipleObjectsCall = reinterpret_cast<WaitForMultipleObjectsFunction>(
            GetProcAddress(kernelModule, "WaitForMultipleObjects"));
        return s_criticalState.CloseHandleCall != nullptr &&
            s_criticalState.GetProcessTimesCall != nullptr &&
            s_criticalState.IsProcessCriticalCall != nullptr &&
            s_criticalState.OpenProcessCall != nullptr &&
            s_criticalState.SetEventCall != nullptr &&
            s_criticalState.TerminateProcessCall != nullptr &&
            s_criticalState.WaitForMultipleObjectsCall != nullptr;
    }

    /// Closes the currently armed process handle and resets its identity.
    KILL_HELPER_HOT_CODE static void CloseArmedTarget() noexcept
    {
        if (s_criticalState.TargetProcessHandle != nullptr)
            s_criticalState.CloseHandleCall(s_criticalState.TargetProcessHandle);

        s_criticalState.TargetProcessHandle = nullptr;
        s_criticalState.Target = {};
    }

    /// Opens and validates a process handle before it enters the emergency path.
    KILL_HELPER_HOT_CODE static FireResult ArmTarget(
        const KillHelperTarget& target,
        DWORD parentProcessID) noexcept
    {
        if (target.ProcessID <= 0 ||
            static_cast<DWORD>(target.ProcessID) == parentProcessID ||
            static_cast<DWORD>(target.ProcessID) == GetCurrentProcessId())
        {
            return { KILL_HELPER_RESULT_INVALID_TARGET, ERROR_INVALID_PARAMETER };
        }

        if (s_criticalState.TargetProcessHandle != nullptr &&
            s_criticalState.Target.ProcessID == target.ProcessID &&
            s_criticalState.Target.CreationTime == target.CreationTime &&
            s_criticalState.Target.Generation == target.Generation)
        {
            return { KILL_HELPER_RESULT_SUCCESS, ERROR_SUCCESS };
        }

        CloseArmedTarget();
        DWORD processAccess = PROCESS_TERMINATE | SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION;
        HANDLE processHandle = s_criticalState.OpenProcessCall(
            processAccess,
            FALSE,
            static_cast<DWORD>(target.ProcessID));
        if (processHandle == nullptr)
            return { KILL_HELPER_RESULT_OPEN_FAILED, GetLastError() };

        FILETIME creationTime{};
        FILETIME exitTime{};
        FILETIME kernelTime{};
        FILETIME userTime{};
        if (target.CreationTime != 0 &&
            !s_criticalState.GetProcessTimesCall(
                processHandle,
                &creationTime,
                &exitTime,
                &kernelTime,
                &userTime))
        {
            DWORD error = GetLastError();
            s_criticalState.CloseHandleCall(processHandle);
            return { KILL_HELPER_RESULT_OPEN_FAILED, error };
        }

        if (target.CreationTime != 0 && FileTimeToLONG64(creationTime) != target.CreationTime)
        {
            s_criticalState.CloseHandleCall(processHandle);
            return { KILL_HELPER_RESULT_IDENTITY_MISMATCH, ERROR_NOT_FOUND };
        }

        BOOL isCritical = FALSE;
        if (!s_criticalState.IsProcessCriticalCall(processHandle, &isCritical))
        {
            DWORD error = GetLastError();
            s_criticalState.CloseHandleCall(processHandle);
            return { KILL_HELPER_RESULT_OPEN_FAILED, error };
        }
        if (isCritical)
        {
            s_criticalState.CloseHandleCall(processHandle);
            return { KILL_HELPER_RESULT_CRITICAL_PROCESS, ERROR_ACCESS_DENIED };
        }

        s_criticalState.TargetProcessHandle = processHandle;
        s_criticalState.Target = target;
        return { KILL_HELPER_RESULT_SUCCESS, ERROR_SUCCESS };
    }

    /// Sends the native termination request through an already-open process handle.
    KILL_HELPER_HOT_CODE static FireResult ExecuteTerminationRequest(
        const KillHelperTarget& target,
        DWORD exitCode,
        DWORD parentProcessID) noexcept
    {
        FireResult armResult = ArmTarget(target, parentProcessID);
        if (armResult.Result != KILL_HELPER_RESULT_SUCCESS)
            return armResult;

        if (!s_criticalState.TerminateProcessCall(s_criticalState.TargetProcessHandle, exitCode))
            return { KILL_HELPER_RESULT_TERMINATE_FAILED, GetLastError() };

        return { KILL_HELPER_RESULT_SUCCESS, ERROR_SUCCESS };
    }

    /// Locks the committed page containing an address into the helper working set.
    static bool LockAddressPage(const void* address) noexcept
    {
        SYSTEM_INFO systemInfo{};
        GetSystemInfo(&systemInfo);
        std::uintptr_t pageMask = static_cast<std::uintptr_t>(systemInfo.dwPageSize) - 1U;
        std::uintptr_t addressValue = reinterpret_cast<std::uintptr_t>(address);
        void* pageAddress = reinterpret_cast<void*>(addressValue & ~pageMask);
        return VirtualLock(pageAddress, systemInfo.dwPageSize) != FALSE;
    }

    /// Locks the complete linker section containing every emergency-path function.
    static bool LockHelperHotCodeSection() noexcept
    {
        HMODULE module = GetModuleHandleW(nullptr);
        if (module == nullptr)
            return false;

        BYTE* imageBase = reinterpret_cast<BYTE*>(module);
        IMAGE_DOS_HEADER* DOSHeader = reinterpret_cast<IMAGE_DOS_HEADER*>(imageBase);
        if (DOSHeader->e_magic != IMAGE_DOS_SIGNATURE)
            return false;

        IMAGE_NT_HEADERS* NTHeaders = reinterpret_cast<IMAGE_NT_HEADERS*>(imageBase + DOSHeader->e_lfanew);
        if (NTHeaders->Signature != IMAGE_NT_SIGNATURE)
            return false;

        const BYTE hotSectionName[IMAGE_SIZEOF_SHORT_NAME] =
        {
            '.', 'k', 'i', 'l', 'l', 'h', 'o', 't'
        };
        IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(NTHeaders);
        for (WORD sectionIndex = 0; sectionIndex < NTHeaders->FileHeader.NumberOfSections; sectionIndex++)
        {
            bool isHotSection = true;
            for (std::uint32_t nameIndex = 0; nameIndex < IMAGE_SIZEOF_SHORT_NAME; nameIndex++)
            {
                if (section[sectionIndex].Name[nameIndex] == hotSectionName[nameIndex])
                    continue;
                isHotSection = false;
                break;
            }
            if (!isHotSection)
                continue;

            SIZE_T sectionSize = section[sectionIndex].Misc.VirtualSize;
            if (sectionSize == 0)
                return false;
            void* sectionAddress = imageBase + section[sectionIndex].VirtualAddress;
            return VirtualLock(sectionAddress, sectionSize) != FALSE;
        }

        return false;
    }

    /// Locks every committed, accessible region of the fully committed helper stack.
    static bool LockCurrentThreadStack() noexcept
    {
        ULONG_PTR stackLow = 0;
        ULONG_PTR stackHigh = 0;
        GetCurrentThreadStackLimits(&stackLow, &stackHigh);
        if (stackLow == 0 || stackHigh <= stackLow)
            return false;

        bool lockedAnyRegion = false;
        std::uintptr_t cursor = static_cast<std::uintptr_t>(stackLow);
        std::uintptr_t limit = static_cast<std::uintptr_t>(stackHigh);
        while (cursor < limit)
        {
            MEMORY_BASIC_INFORMATION memoryInformation{};
            if (VirtualQuery(
                    reinterpret_cast<const void*>(cursor),
                    &memoryInformation,
                    sizeof(memoryInformation)) == 0)
            {
                return false;
            }

            std::uintptr_t regionStart = reinterpret_cast<std::uintptr_t>(memoryInformation.BaseAddress);
            std::uintptr_t regionEnd = regionStart + memoryInformation.RegionSize;
            std::uintptr_t lockStart = regionStart < cursor ? cursor : regionStart;
            std::uintptr_t lockEnd = regionEnd > limit ? limit : regionEnd;
            bool isAccessibleCommit = memoryInformation.State == MEM_COMMIT &&
                (memoryInformation.Protect & (PAGE_GUARD | PAGE_NOACCESS)) == 0;
            if (isAccessibleCommit && lockEnd > lockStart)
            {
                SIZE_T lockSize = static_cast<SIZE_T>(lockEnd - lockStart);
                if (!VirtualLock(reinterpret_cast<void*>(lockStart), lockSize))
                    return false;
                lockedAnyRegion = true;
            }

            if (regionEnd <= cursor)
                return false;
            cursor = regionEnd;
        }

        return lockedAnyRegion;
    }

    /// Reserves enough working-set quota for all emergency pages before locking them.
    static bool ReserveVirtualLockCapacity() noexcept
    {
        SIZE_T currentMinimum = 0;
        SIZE_T currentMaximum = 0;
        HANDLE currentProcess = GetCurrentProcess();
        if (!GetProcessWorkingSetSize(currentProcess, &currentMinimum, &currentMaximum))
            return false;

        SIZE_T requiredMinimum = KILL_HELPER_MINIMUM_WORKING_SET_BYTES;
        if (currentMinimum >= requiredMinimum)
            return true;

        SIZE_T requiredMaximum = currentMaximum < requiredMinimum
            ? requiredMinimum
            : currentMaximum;
        return SetProcessWorkingSetSize(
                   currentProcess,
                   requiredMinimum,
                   requiredMaximum) != FALSE;
    }

    /// Pins the mailbox, helper state, hot function, and imported kernel entry points.
    static LONG LockCriticalPages(KillHelperMailbox* mailbox) noexcept
    {
        LONG flags = 0;
        if (ReserveVirtualLockCapacity())
            flags |= KILL_HELPER_FLAG_LOCK_CAPACITY_RESERVED;
        if (VirtualLock(mailbox, KILL_HELPER_MAILBOX_SIZE))
            flags |= KILL_HELPER_FLAG_MAILBOX_LOCKED;
        if (VirtualLock(&s_criticalState, sizeof(s_criticalState)))
            flags |= KILL_HELPER_FLAG_STATE_LOCKED;

        if (LockHelperHotCodeSection())
            flags |= KILL_HELPER_FLAG_HOT_CODE_LOCKED;

        std::uintptr_t kernelFunctionAddresses[] =
        {
            reinterpret_cast<std::uintptr_t>(s_criticalState.CloseHandleCall),
            reinterpret_cast<std::uintptr_t>(s_criticalState.GetProcessTimesCall),
            reinterpret_cast<std::uintptr_t>(s_criticalState.IsProcessCriticalCall),
            reinterpret_cast<std::uintptr_t>(s_criticalState.OpenProcessCall),
            reinterpret_cast<std::uintptr_t>(s_criticalState.SetEventCall),
            reinterpret_cast<std::uintptr_t>(s_criticalState.TerminateProcessCall),
            reinterpret_cast<std::uintptr_t>(s_criticalState.WaitForMultipleObjectsCall)
        };
        bool kernelCodeLocked = true;
        for (std::uint32_t index = 0;
             index < static_cast<std::uint32_t>(sizeof(kernelFunctionAddresses) / sizeof(kernelFunctionAddresses[0]));
             index++)
        {
            bool functionLocked = LockAddressPage(
                reinterpret_cast<const void*>(kernelFunctionAddresses[index]));
            kernelCodeLocked = functionLocked && kernelCodeLocked;
        }
        if (kernelCodeLocked)
            flags |= KILL_HELPER_FLAG_KERNEL_CODE_LOCKED;
        if (LockCurrentThreadStack())
            flags |= KILL_HELPER_FLAG_STACK_LOCKED;

        return flags;
    }

    /// Enables SeDebugPrivilege in the elevated process token.
    static bool EnableDebugPrivilege() noexcept
    {
        HANDLE tokenHandle = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &tokenHandle))
            return false;

        LUID privilegeLUID{};
        if (!LookupPrivilegeValueW(nullptr, SE_DEBUG_NAME, &privilegeLUID))
        {
            CloseHandle(tokenHandle);
            return false;
        }

        TOKEN_PRIVILEGES privileges{};
        privileges.PrivilegeCount = 1;
        privileges.Privileges[0].Luid = privilegeLUID;
        privileges.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
        SetLastError(ERROR_SUCCESS);
        BOOL adjusted = AdjustTokenPrivileges(tokenHandle, FALSE, &privileges, sizeof(privileges), nullptr, nullptr);
        DWORD adjustmentError = GetLastError();
        CloseHandle(tokenHandle);
        return adjusted && adjustmentError == ERROR_SUCCESS;
    }

    /// Disables execution-speed throttling for the normally dormant helper.
    static bool DisablePowerThrottling() noexcept
    {
        PROCESS_POWER_THROTTLING_STATE throttlingState{};
        throttlingState.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
        throttlingState.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
        throttlingState.StateMask = 0;
        return SetProcessInformation(
                   GetCurrentProcess(),
                   ProcessPowerThrottling,
                   &throttlingState,
                   sizeof(throttlingState)) != FALSE;
    }

    /// Publishes a completed fire request and wakes the managed response waiter.
    KILL_HELPER_HOT_CODE static void PublishFireResult(
        KillHelperMailbox* mailbox,
        HANDLE responseEvent,
        LONG64 requestSequence,
        LONG processID,
        const FireResult& result) noexcept
    {
        InterlockedExchange(&mailbox->FireResult, result.Result);
        InterlockedExchange(&mailbox->FireError, static_cast<LONG>(result.Error));
        InterlockedExchange(&mailbox->FireResponseProcessID, processID);
        InterlockedExchange64(&mailbox->FireResponseSequence, requestSequence);
        s_criticalState.SetEventCall(responseEvent);
    }

    /// Runs the allocation-free request loop after startup has opened all resources.
    KILL_HELPER_HOT_CODE static DWORD RunRequestLoop(
        KillHelperMailbox* mailbox,
        HANDLE requestEvent,
        HANDLE responseEvent,
        HANDLE parentProcess,
        DWORD parentProcessID) noexcept
    {
        HANDLE waitHandles[KILL_HELPER_WAIT_HANDLE_COUNT] = { requestEvent, parentProcess };
        LONG64 processedArmSequence = 0;
        LONG64 processedFireSequence = 0;

        for (;;)
        {
            DWORD waitResult = s_criticalState.WaitForMultipleObjectsCall(
                KILL_HELPER_WAIT_HANDLE_COUNT,
                waitHandles,
                FALSE,
                INFINITE);
            if (waitResult == WAIT_OBJECT_0 + 1U)
                return ERROR_SUCCESS;
            if (waitResult != WAIT_OBJECT_0)
                return GetLastError();
            if ((InterlockedCompareExchange(&mailbox->ControlFlags, 0, 0) & KILL_HELPER_CONTROL_SHUTDOWN) != 0)
                return ERROR_SUCCESS;

            LONG64 fireRequestSequence = ReadSharedLONG64(&mailbox->FireRequestSequence);
            if (fireRequestSequence != processedFireSequence)
            {
                KillHelperTarget fireTarget{};
                DWORD exitCode = 1U;
                FireResult fireResult = { KILL_HELPER_RESULT_INVALID_TARGET, ERROR_INVALID_DATA };
                if (TryReadFireTarget(mailbox, &fireTarget, &exitCode))
                    fireResult = ExecuteTerminationRequest(fireTarget, exitCode, parentProcessID);

                PublishFireResult(
                    mailbox,
                    responseEvent,
                    fireRequestSequence,
                    fireTarget.ProcessID,
                    fireResult);
                processedFireSequence = fireRequestSequence;
            }

            LONG64 armRequestSequence = ReadSharedLONG64(&mailbox->ArmRequestSequence);
            if (armRequestSequence == processedArmSequence)
                continue;

            KillHelperTarget armTarget{};
            if (TryReadArmTarget(mailbox, &armTarget))
            {
                if (armTarget.ProcessID <= 0)
                    CloseArmedTarget();
                else if (armTarget.Generation != s_criticalState.Target.Generation)
                    static_cast<void>(ArmTarget(armTarget, parentProcessID));
            }
            processedArmSequence = armRequestSequence;
        }
    }

    /// Parses one unsigned pointer-sized value in hexadecimal form.
    static bool TryParseHandleValue(
        const WCHAR* text,
        std::uintptr_t* value,
        const WCHAR** next) noexcept
    {
        if (text == nullptr || value == nullptr || next == nullptr)
            return false;

        WCHAR* endPointer = nullptr;
        unsigned long long parsedValue = wcstoull(text, &endPointer, 16);
        if (endPointer == text ||
            (*endPointer != L'\0' && *endPointer != L' ' && *endPointer != L'\t') ||
            parsedValue == 0 ||
            parsedValue > UINTPTR_MAX)
        {
            return false;
        }

        *value = static_cast<std::uintptr_t>(parsedValue);
        *next = endPointer;
        return true;
    }

    /// Advances past the fixed command protocol's ASCII whitespace.
    static const WCHAR* SkipWhitespace(const WCHAR* text) noexcept
    {
        while (*text == L' ' || *text == L'\t')
            text++;
        return text;
    }

    /// Parses the four numeric command-line fields without loading Shell32 or allocating.
    static bool TryParseArguments(const WCHAR* commandLine, HelperArguments* arguments) noexcept
    {
        if (commandLine == nullptr || arguments == nullptr)
            return false;

        const WCHAR* cursor = SkipWhitespace(commandLine);
        WCHAR* endPointer = nullptr;
        unsigned long parentProcessID = wcstoul(cursor, &endPointer, 10);
        if (endPointer == cursor || parentProcessID == 0 || parentProcessID > MAXDWORD)
            return false;
        arguments->ParentProcessID = static_cast<DWORD>(parentProcessID);

        cursor = SkipWhitespace(endPointer);
        const WCHAR* next = nullptr;
        if (!TryParseHandleValue(cursor, &arguments->MappingHandleValue, &next))
            return false;

        cursor = SkipWhitespace(next);
        if (!TryParseHandleValue(cursor, &arguments->RequestEventHandleValue, &next))
            return false;

        cursor = SkipWhitespace(next);
        if (!TryParseHandleValue(cursor, &arguments->ResponseEventHandleValue, &next))
            return false;
        return *SkipWhitespace(next) == L'\0';
    }

    /// Opens the pre-created objects and services requests until shutdown.
    static int RunHelper(const HelperArguments& arguments, bool isDebugPrivilegeEnabled) noexcept
    {
        HANDLE parentProcess = OpenProcess(
            PROCESS_DUP_HANDLE | SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
            FALSE,
            arguments.ParentProcessID);
        if (parentProcess == nullptr)
            return static_cast<int>(GetLastError());

        HANDLE mappingHandle = nullptr;
        HANDLE requestEvent = nullptr;
        HANDLE responseEvent = nullptr;
        DWORD duplicateError = ERROR_SUCCESS;
        BOOL responseEventDuplicated = DuplicateHandle(
            parentProcess,
            reinterpret_cast<HANDLE>(arguments.ResponseEventHandleValue),
            GetCurrentProcess(),
            &responseEvent,
            0,
            FALSE,
            DUPLICATE_SAME_ACCESS);
        if (!responseEventDuplicated)
            duplicateError = GetLastError();

        BOOL mappingDuplicated = FALSE;
        if (responseEventDuplicated)
        {
            mappingDuplicated = DuplicateHandle(
                parentProcess,
                reinterpret_cast<HANDLE>(arguments.MappingHandleValue),
                GetCurrentProcess(),
                &mappingHandle,
                0,
                FALSE,
                DUPLICATE_SAME_ACCESS);
            if (!mappingDuplicated)
                duplicateError = GetLastError();
        }

        BOOL requestEventDuplicated = FALSE;
        if (mappingDuplicated)
        {
            requestEventDuplicated = DuplicateHandle(
                parentProcess,
                reinterpret_cast<HANDLE>(arguments.RequestEventHandleValue),
                GetCurrentProcess(),
                &requestEvent,
                0,
                FALSE,
                DUPLICATE_SAME_ACCESS);
            if (!requestEventDuplicated)
                duplicateError = GetLastError();
        }
        if (!mappingDuplicated || !requestEventDuplicated || !responseEventDuplicated)
        {
            if (responseEvent != nullptr)
                SetEvent(responseEvent);
            if (responseEvent != nullptr)
                CloseHandle(responseEvent);
            if (requestEvent != nullptr)
                CloseHandle(requestEvent);
            if (mappingHandle != nullptr)
                CloseHandle(mappingHandle);
            CloseHandle(parentProcess);
            return static_cast<int>(duplicateError);
        }

        KillHelperMailbox* mailbox = static_cast<KillHelperMailbox*>(MapViewOfFile(
            mappingHandle,
            FILE_MAP_READ | FILE_MAP_WRITE,
            0,
            0,
            KILL_HELPER_MAILBOX_SIZE));
        if (mailbox == nullptr)
        {
            DWORD error = GetLastError();
            CloseHandle(responseEvent);
            CloseHandle(requestEvent);
            CloseHandle(mappingHandle);
            CloseHandle(parentProcess);
            return static_cast<int>(error);
        }

        if (mailbox->Magic != KILL_HELPER_MAILBOX_MAGIC ||
            mailbox->Version != KILL_HELPER_PROTOCOL_VERSION ||
            mailbox->ParentProcessID != arguments.ParentProcessID)
        {
            InterlockedExchange(&mailbox->HelperStartupError, ERROR_INVALID_DATA);
            InterlockedExchange(&mailbox->HelperState, KILL_HELPER_STATE_FAILED);
            SetEvent(responseEvent);
            CloseHandle(parentProcess);
            CloseHandle(responseEvent);
            CloseHandle(requestEvent);
            UnmapViewOfFile(mailbox);
            CloseHandle(mappingHandle);
            return ERROR_INVALID_DATA;
        }

        if (!ResolveCriticalFunctions())
        {
            InterlockedExchange(&mailbox->HelperStartupError, ERROR_PROC_NOT_FOUND);
            InterlockedExchange(&mailbox->HelperState, KILL_HELPER_STATE_FAILED);
            SetEvent(responseEvent);
            CloseHandle(parentProcess);
            CloseHandle(responseEvent);
            CloseHandle(requestEvent);
            UnmapViewOfFile(mailbox);
            CloseHandle(mappingHandle);
            return ERROR_PROC_NOT_FOUND;
        }

        LONG helperFlags = LockCriticalPages(mailbox);
        if (SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS) &&
            SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST))
        {
            helperFlags |= KILL_HELPER_FLAG_HIGH_PRIORITY;
        }
        if (isDebugPrivilegeEnabled)
            helperFlags |= KILL_HELPER_FLAG_DEBUG_PRIVILEGE;
        if (DisablePowerThrottling())
            helperFlags |= KILL_HELPER_FLAG_POWER_THROTTLING_DISABLED;

        mailbox->HelperProcessID = GetCurrentProcessId();
        InterlockedExchange(&mailbox->HelperFlags, helperFlags);
        InterlockedExchange(&mailbox->HelperStartupError, ERROR_SUCCESS);
        InterlockedExchange(&mailbox->HelperState, KILL_HELPER_STATE_READY);
        SetEvent(responseEvent);

        DWORD runError = RunRequestLoop(
            mailbox,
            requestEvent,
            responseEvent,
            parentProcess,
            arguments.ParentProcessID);

        InterlockedExchange(&mailbox->HelperState, KILL_HELPER_STATE_STOPPING);
        CloseArmedTarget();
        CloseHandle(parentProcess);
        CloseHandle(responseEvent);
        CloseHandle(requestEvent);
        VirtualUnlock(mailbox, KILL_HELPER_MAILBOX_SIZE);
        UnmapViewOfFile(mailbox);
        CloseHandle(mappingHandle);
        return static_cast<int>(runError);
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR commandLine, int)
{
    TaskManagerTrayAppDotNET::HelperArguments arguments{};
    if (!TaskManagerTrayAppDotNET::TryParseArguments(commandLine, &arguments))
        return ERROR_INVALID_PARAMETER;

    bool isDebugPrivilegeEnabled = TaskManagerTrayAppDotNET::EnableDebugPrivilege();
    return TaskManagerTrayAppDotNET::RunHelper(arguments, isDebugPrivilegeEnabled);
}
