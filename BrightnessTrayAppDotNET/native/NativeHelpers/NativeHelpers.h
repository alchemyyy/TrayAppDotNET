#pragma once

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    int RunDDCHelper(int argumentCount, wchar_t* arguments[]) noexcept;

    // Implemented by the native Night Light migration.
    int RunNightLightHelper(int argumentCount, wchar_t* arguments[]) noexcept;
}
