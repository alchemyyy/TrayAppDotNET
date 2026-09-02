#include "DDCHelperProtocol.h"
#include "NativeHelpers.h"

#include <windows.h>
#include <shellapi.h>

#include <cwchar>

#pragma comment(lib, "Shell32.lib")

#if defined(BRIGHTNESS_NATIVE_AOT_ENTRY)
extern "C" int __managed__Main(int argumentCount, wchar_t* arguments[]);
#endif

namespace
{
    constexpr wchar_t NIGHT_LIGHT_HELPER_MODE_ARGUMENT[] = L"--night-light-helper-server";

    bool IsDDCHelperMode(int argumentCount, wchar_t* arguments[]) noexcept
    {
        return argumentCount > 1 &&
               arguments != nullptr &&
               arguments[1] != nullptr &&
               wcscmp(arguments[1], DDC_HELPER_MODE_ARGUMENT) == 0;
    }

    bool IsNightLightHelperMode(int argumentCount, wchar_t* arguments[]) noexcept
    {
        return argumentCount > 1 &&
               arguments != nullptr &&
               arguments[1] != nullptr &&
               wcscmp(arguments[1], NIGHT_LIGHT_HELPER_MODE_ARGUMENT) == 0;
    }
}

#if defined(BRIGHTNESS_NATIVE_AOT_ENTRY)
int __cdecl wmain(int argumentCount, wchar_t* arguments[])
{
    if (IsDDCHelperMode(argumentCount, arguments))
        return BrightnessTrayAppDotNET::NativeHelpers::RunDDCHelper(argumentCount, arguments);
    if (IsNightLightHelperMode(argumentCount, arguments))
        return BrightnessTrayAppDotNET::NativeHelpers::RunNightLightHelper(argumentCount, arguments);

    return __managed__Main(argumentCount, arguments);
}
#else
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    int argumentCount = 0;
    wchar_t** arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    if (arguments == nullptr)
        return static_cast<int>(GetLastError());

    int exitCode = ERROR_INVALID_PARAMETER;
    if (IsDDCHelperMode(argumentCount, arguments))
        exitCode = BrightnessTrayAppDotNET::NativeHelpers::RunDDCHelper(argumentCount, arguments);
    else if (IsNightLightHelperMode(argumentCount, arguments))
        exitCode = BrightnessTrayAppDotNET::NativeHelpers::RunNightLightHelper(argumentCount, arguments);

    LocalFree(arguments);
    return exitCode;
}
#endif
