#include "ParentWatchdog.h"

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    namespace
    {
        DWORD WINAPI WatchParent(void* parameter) noexcept
        {
            HANDLE parentProcess = static_cast<HANDLE>(parameter);
            DWORD waitResult = WaitForSingleObject(parentProcess, INFINITE);
            DWORD exitCode = waitResult == WAIT_OBJECT_0 ? ERROR_SUCCESS : GetLastError();
            CloseHandle(parentProcess);
            ExitProcess(exitCode);
        }
    }

    DWORD StartParentWatchdog(DWORD parentProcessID) noexcept
    {
        if (parentProcessID == 0U || parentProcessID == GetCurrentProcessId())
            return ERROR_INVALID_PARAMETER;

        HANDLE parentProcess = OpenProcess(SYNCHRONIZE, FALSE, parentProcessID);
        if (parentProcess == nullptr)
            return GetLastError();

        HANDLE watchdogThread = CreateThread(nullptr, 0U, WatchParent, parentProcess, 0U, nullptr);
        if (watchdogThread == nullptr)
        {
            DWORD error = GetLastError();
            CloseHandle(parentProcess);
            return error;
        }

        CloseHandle(watchdogThread);
        return ERROR_SUCCESS;
    }
}
