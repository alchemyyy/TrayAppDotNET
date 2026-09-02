#include "PipeChannel.h"

#include "DDCHelperProtocol.h"

#include <algorithm>
#include <limits>
#include <string>

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    PipeChannel::PipeChannel() noexcept
        : _pipeHandle(INVALID_HANDLE_VALUE),
          _readBuffer{},
          _readOffset(0U),
          _readLength(0U)
    {
    }

    PipeChannel::PipeChannel(HANDLE pipeHandle) noexcept
        : _pipeHandle(pipeHandle),
          _readBuffer{},
          _readOffset(0U),
          _readLength(0U)
    {
    }

    PipeChannel::~PipeChannel() noexcept
    {
        if (_pipeHandle != INVALID_HANDLE_VALUE)
            CloseHandle(_pipeHandle);
    }

    PipeChannel PipeChannel::Connect(
        const std::wstring& pipeName,
        DWORD timeoutMilliseconds,
        DWORD* error) noexcept
    {
        if (error != nullptr)
            *error = ERROR_SUCCESS;

        if (pipeName.empty())
        {
            if (error != nullptr)
                *error = ERROR_INVALID_PARAMETER;
            return PipeChannel();
        }

        std::wstring fullPipeName;
        try
        {
            fullPipeName = L"\\\\.\\pipe\\" + pipeName;
        }
        catch (...)
        {
            if (error != nullptr)
                *error = ERROR_OUTOFMEMORY;
            return PipeChannel();
        }

        ULONGLONG deadline = GetTickCount64() + timeoutMilliseconds;
        while (true)
        {
            HANDLE pipeHandle = CreateFileW(
                fullPipeName.c_str(),
                GENERIC_READ | GENERIC_WRITE,
                0U,
                nullptr,
                OPEN_EXISTING,
                0U,
                nullptr);
            if (pipeHandle != INVALID_HANDLE_VALUE)
                return PipeChannel(pipeHandle);

            DWORD createError = GetLastError();
            if (createError != ERROR_PIPE_BUSY && createError != ERROR_FILE_NOT_FOUND)
            {
                if (error != nullptr)
                    *error = createError;
                return PipeChannel();
            }

            ULONGLONG currentTick = GetTickCount64();
            if (currentTick >= deadline)
            {
                if (error != nullptr)
                    *error = ERROR_SEM_TIMEOUT;
                return PipeChannel();
            }

            ULONGLONG remaining = deadline - currentTick;
            DWORD waitMilliseconds = static_cast<DWORD>(std::min<ULONGLONG>(remaining, 50U));
            if (createError == ERROR_PIPE_BUSY)
                WaitNamedPipeW(fullPipeName.c_str(), waitMilliseconds);
            else
                Sleep(std::min<DWORD>(waitMilliseconds, 10U));
        }
    }

    bool PipeChannel::IsOpen() const noexcept
    {
        return _pipeHandle != INVALID_HANDLE_VALUE;
    }

    PipeLineReadResult PipeChannel::ReadLine(std::string* line, DWORD* error)
    {
        if (line == nullptr)
        {
            if (error != nullptr)
                *error = ERROR_INVALID_PARAMETER;
            return PipeLineReadResult::Failed;
        }

        line->clear();
        bool isTooLong = false;
        while (true)
        {
            if (_readOffset == _readLength)
            {
                DWORD bytesRead = 0U;
                BOOL readSucceeded = ReadFile(
                    _pipeHandle,
                    _readBuffer.data(),
                    static_cast<DWORD>(_readBuffer.size()),
                    &bytesRead,
                    nullptr);
                if (!readSucceeded)
                {
                    DWORD readError = GetLastError();
                    if (readError == ERROR_BROKEN_PIPE)
                        return PipeLineReadResult::End;

                    if (error != nullptr)
                        *error = readError;
                    return PipeLineReadResult::Failed;
                }

                if (bytesRead == 0U)
                    return PipeLineReadResult::End;

                _readOffset = 0U;
                _readLength = bytesRead;
            }

            char character = _readBuffer[_readOffset++];
            if (character == '\n')
            {
                if (!line->empty() && line->back() == '\r')
                    line->pop_back();
                return isTooLong ? PipeLineReadResult::TooLong : PipeLineReadResult::Line;
            }

            if (isTooLong)
                continue;

            if (line->size() >= DDC_HELPER_MAX_COMMAND_BYTES)
            {
                isTooLong = true;
                line->clear();
                continue;
            }

            line->push_back(character);
        }
    }

    bool PipeChannel::WriteLine(const std::string& line, DWORD* error) const noexcept
    {
        if (line.size() > static_cast<size_t>(std::numeric_limits<DWORD>::max()) - 1U)
        {
            if (error != nullptr)
                *error = ERROR_BUFFER_OVERFLOW;
            return false;
        }

        DWORD bytesWritten = 0U;
        if (!line.empty())
        {
            BOOL lineWritten = WriteFile(
                _pipeHandle,
                line.data(),
                static_cast<DWORD>(line.size()),
                &bytesWritten,
                nullptr);
            if (!lineWritten || bytesWritten != line.size())
            {
                if (error != nullptr)
                    *error = lineWritten ? ERROR_WRITE_FAULT : GetLastError();
                return false;
            }
        }

        constexpr char NEWLINE = '\n';
        BOOL newlineWritten = WriteFile(_pipeHandle, &NEWLINE, 1U, &bytesWritten, nullptr);
        if (!newlineWritten || bytesWritten != 1U)
        {
            if (error != nullptr)
                *error = newlineWritten ? ERROR_WRITE_FAULT : GetLastError();
            return false;
        }

        return true;
    }
}
