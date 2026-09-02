#pragma once

#include <windows.h>

#include <array>
#include <string>

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    enum class PipeLineReadResult
    {
        Line,
        End,
        TooLong,
        Failed
    };

    class PipeChannel final
    {
    public:
        PipeChannel() noexcept;
        explicit PipeChannel(HANDLE pipeHandle) noexcept;
        PipeChannel(const PipeChannel&) = delete;
        PipeChannel& operator=(const PipeChannel&) = delete;
        ~PipeChannel() noexcept;

        static PipeChannel Connect(
            const std::wstring& pipeName,
            DWORD timeoutMilliseconds,
            DWORD* error) noexcept;

        [[nodiscard]] bool IsOpen() const noexcept;
        PipeLineReadResult ReadLine(std::string* line, DWORD* error);
        bool WriteLine(const std::string& line, DWORD* error) const noexcept;

    private:
        static constexpr DWORD READ_BUFFER_SIZE = 4096U;

        HANDLE _pipeHandle;
        std::array<char, READ_BUFFER_SIZE> _readBuffer;
        DWORD _readOffset;
        DWORD _readLength;
    };
}
