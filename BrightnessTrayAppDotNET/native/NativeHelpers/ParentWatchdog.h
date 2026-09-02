#pragma once

#include <windows.h>

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    DWORD StartParentWatchdog(DWORD parentProcessID) noexcept;
}
