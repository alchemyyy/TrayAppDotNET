#pragma once

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    /// Runs the native Night Light named-pipe helper mode.
    int RunNightLightHelper(int argumentCount, wchar_t* arguments[]) noexcept;
}
