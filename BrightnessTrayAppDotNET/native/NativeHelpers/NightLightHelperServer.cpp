#include "NightLightHelperServer.h"

#include <Windows.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <string_view>

#include "NightLightBackend.h"

#define NIGHT_LIGHT_COMMAND_BUFFER_BYTES 512U
#define NIGHT_LIGHT_PIPE_CONNECT_TIMEOUT_MS 5000U
#define NIGHT_LIGHT_PIPE_READ_BUFFER_BYTES 4096U
#define NIGHT_LIGHT_PIPE_NAME_CHARACTERS 256U

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    namespace
    {
        constexpr wchar_t SERVER_ARGUMENT[] = L"--night-light-helper-server";
        constexpr wchar_t PARENT_PROCESS_ID_ARGUMENT[] = L"--parent-pid";
        constexpr wchar_t PIPE_NAME_ARGUMENT[] = L"--pipe-name";
        constexpr wchar_t PIPE_PATH_PREFIX[] = L"\\\\.\\pipe\\";

        constexpr std::string_view INITIALIZE_COMMAND = "INIT";
        constexpr std::string_view PROTOCOL_VERSION = "1";
        constexpr std::string_view SET_STRENGTH_COMMAND = "SET";
        constexpr std::string_view SET_ACTIVE_COMMAND = "ACTIVE";
        constexpr std::string_view PING_COMMAND = "PING";
        constexpr std::string_view DRAIN_COMMAND = "DRAIN";
        constexpr std::string_view EXIT_COMMAND = "EXIT";

        constexpr std::string_view READY_RESPONSE = "READY";
        constexpr std::string_view IMAGE_MISMATCH_RESPONSE = "IMAGE_MISMATCH";
        constexpr std::string_view UNSUPPORTED_RESPONSE = "UNSUPPORTED\t";
        constexpr std::string_view SUCCESS_RESPONSE = "OK";
        constexpr std::string_view PONG_RESPONSE = "PONG";
        constexpr std::string_view DRAINED_RESPONSE = "DRAINED";
        constexpr std::string_view FAILURE_RESPONSE = "FAIL";

        struct HelperArguments
        {
            DWORD ParentProcessID;
            const wchar_t* PipeName;
        };

        class ScopedHandle final
        {
        public:
            ScopedHandle() noexcept : _handle(nullptr)
            {
            }

            explicit ScopedHandle(HANDLE handle) noexcept : _handle(handle)
            {
            }

            ScopedHandle(const ScopedHandle&) = delete;
            ScopedHandle& operator=(const ScopedHandle&) = delete;

            ~ScopedHandle() noexcept
            {
                Reset();
            }

            HANDLE Get() const noexcept
            {
                return _handle;
            }

            void Reset(HANDLE handle = nullptr) noexcept
            {
                if (_handle != nullptr && _handle != INVALID_HANDLE_VALUE)
                {
                    CloseHandle(_handle);
                }

                _handle = handle;
            }

        private:
            HANDLE _handle;
        };

        class ParentWatchdog final
        {
        public:
            ParentWatchdog() noexcept :
                _parentProcess(),
                _stopEvent(),
                _thread()
            {
            }

            ParentWatchdog(const ParentWatchdog&) = delete;
            ParentWatchdog& operator=(const ParentWatchdog&) = delete;

            ~ParentWatchdog() noexcept
            {
                Stop();
            }

            bool Start(DWORD parentProcessID) noexcept
            {
                if (parentProcessID == 0U || parentProcessID == GetCurrentProcessId())
                {
                    return false;
                }

                HANDLE parentProcess = OpenProcess(
                    SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
                    FALSE,
                    parentProcessID);
                if (parentProcess == nullptr)
                {
                    return false;
                }
                _parentProcess.Reset(parentProcess);

                DWORD parentExitCode = 0U;
                if (GetExitCodeProcess(_parentProcess.Get(), &parentExitCode) == FALSE
                    || parentExitCode != STILL_ACTIVE)
                {
                    return false;
                }

                _stopEvent.Reset(CreateEventW(nullptr, TRUE, FALSE, nullptr));
                if (_stopEvent.Get() == nullptr)
                {
                    return false;
                }

                _thread.Reset(CreateThread(nullptr, 0U, ThreadEntry, this, 0U, nullptr));
                return _thread.Get() != nullptr;
            }

            void Stop() noexcept
            {
                if (_thread.Get() == nullptr)
                {
                    return;
                }

                SetEvent(_stopEvent.Get());
                (void)WaitForSingleObject(_thread.Get(), INFINITE);
                _thread.Reset();
            }

        private:
            static DWORD WINAPI ThreadEntry(void* parameter) noexcept
            {
                if (parameter == nullptr)
                {
                    return ERROR_INVALID_PARAMETER;
                }

                ParentWatchdog* watchdog = static_cast<ParentWatchdog*>(parameter);
                const std::array<HANDLE, 2U> waitHandles =
                    { watchdog->_parentProcess.Get(), watchdog->_stopEvent.Get() };
                DWORD waitResult = WaitForMultipleObjects(
                    static_cast<DWORD>(waitHandles.size()),
                    waitHandles.data(),
                    FALSE,
                    INFINITE);
                if (waitResult == WAIT_OBJECT_0)
                {
                    (void)TerminateProcess(GetCurrentProcess(), ERROR_PROCESS_ABORTED);
                }

                return ERROR_SUCCESS;
            }

            ScopedHandle _parentProcess;
            ScopedHandle _stopEvent;
            ScopedHandle _thread;
        };

        class PipeChannel final
        {
        public:
            explicit PipeChannel(HANDLE pipe) noexcept :
                _pipe(pipe),
                _readBuffer{},
                _readOffset(0U),
                _readLength(0U)
            {
            }

            PipeChannel(const PipeChannel&) = delete;
            PipeChannel& operator=(const PipeChannel&) = delete;
            ~PipeChannel() noexcept = default;

            bool ReadLine(char* line, std::size_t lineCapacity) noexcept
            {
                if (line == nullptr || lineCapacity < 2U)
                {
                    return false;
                }

                std::size_t lineLength = 0U;
                while (true)
                {
                    if (_readOffset == _readLength)
                    {
                        DWORD bytesRead = 0U;
                        BOOL readResult = ReadFile(
                            _pipe,
                            _readBuffer.data(),
                            static_cast<DWORD>(_readBuffer.size()),
                            &bytesRead,
                            nullptr);
                        if (readResult == FALSE || bytesRead == 0U)
                        {
                            return false;
                        }

                        _readOffset = 0U;
                        _readLength = bytesRead;
                    }

                    unsigned char nextByte = _readBuffer[_readOffset++];
                    if (nextByte == static_cast<unsigned char>('\n'))
                    {
                        if (lineLength > 0U && line[lineLength - 1U] == '\r')
                        {
                            lineLength--;
                        }

                        line[lineLength] = '\0';
                        return true;
                    }

                    if (nextByte == 0U || nextByte > 0x7FU || lineLength + 1U >= lineCapacity)
                    {
                        return false;
                    }

                    line[lineLength++] = static_cast<char>(nextByte);
                }
            }

            bool WriteLine(std::string_view response) const noexcept
            {
                return WriteAll(response.data(), response.size()) && WriteAll("\n", 1U);
            }

            bool WriteUnsupported(const char* reason) const noexcept
            {
                if (reason == nullptr)
                {
                    reason = "unknown";
                }

                return WriteAll(UNSUPPORTED_RESPONSE.data(), UNSUPPORTED_RESPONSE.size())
                    && WriteAll(reason, std::strlen(reason))
                    && WriteAll("\n", 1U);
            }

        private:
            bool WriteAll(const char* bytes, std::size_t byteCount) const noexcept
            {
                if (bytes == nullptr)
                {
                    return false;
                }

                std::size_t bytesWrittenTotal = 0U;
                while (bytesWrittenTotal < byteCount)
                {
                    std::size_t remaining = byteCount - bytesWrittenTotal;
                    DWORD writeSize = remaining > MAXDWORD
                        ? MAXDWORD
                        : static_cast<DWORD>(remaining);
                    DWORD bytesWritten = 0U;
                    BOOL writeResult = WriteFile(
                        _pipe,
                        bytes + bytesWrittenTotal,
                        writeSize,
                        &bytesWritten,
                        nullptr);
                    if (writeResult == FALSE || bytesWritten == 0U)
                    {
                        return false;
                    }

                    bytesWrittenTotal += bytesWritten;
                }

                return true;
            }

            HANDLE _pipe;
            std::array<unsigned char, NIGHT_LIGHT_PIPE_READ_BUFFER_BYTES> _readBuffer;
            DWORD _readOffset;
            DWORD _readLength;
        };

        bool TryParseDecimalDWORD(std::wstring_view value, DWORD* result) noexcept
        {
            if (result == nullptr || value.empty())
            {
                return false;
            }

            std::uint64_t parsed = 0U;
            for (wchar_t character : value)
            {
                if (character < L'0' || character > L'9')
                {
                    return false;
                }

                parsed = parsed * 10U + static_cast<std::uint64_t>(character - L'0');
                if (parsed > MAXDWORD)
                {
                    return false;
                }
            }

            *result = static_cast<DWORD>(parsed);
            return true;
        }

        bool TryParseArguments(
            int argumentCount,
            wchar_t* arguments[],
            HelperArguments* result) noexcept
        {
            if (result == nullptr || arguments == nullptr || argumentCount != 6)
            {
                return false;
            }

            result->ParentProcessID = 0U;
            result->PipeName = nullptr;
            bool foundServerArgument = false;
            bool foundParentProcessID = false;
            bool foundPipeName = false;

            for (int argumentIndex = 1; argumentIndex < argumentCount; argumentIndex++)
            {
                const wchar_t* argument = arguments[argumentIndex];
                if (argument == nullptr)
                {
                    return false;
                }

                if (_wcsicmp(argument, SERVER_ARGUMENT) == 0)
                {
                    if (foundServerArgument)
                    {
                        return false;
                    }

                    foundServerArgument = true;
                    continue;
                }

                if (_wcsicmp(argument, PARENT_PROCESS_ID_ARGUMENT) == 0)
                {
                    if (foundParentProcessID || argumentIndex + 1 >= argumentCount)
                    {
                        return false;
                    }

                    const wchar_t* value = arguments[++argumentIndex];
                    if (value == nullptr
                        || !TryParseDecimalDWORD(value, &result->ParentProcessID)
                        || result->ParentProcessID == 0U)
                    {
                        return false;
                    }

                    foundParentProcessID = true;
                    continue;
                }

                if (_wcsicmp(argument, PIPE_NAME_ARGUMENT) == 0)
                {
                    if (foundPipeName || argumentIndex + 1 >= argumentCount)
                    {
                        return false;
                    }

                    const wchar_t* value = arguments[++argumentIndex];
                    if (value == nullptr)
                    {
                        return false;
                    }

                    std::size_t pipeNameLength = std::wcslen(value);
                    if (pipeNameLength == 0U || pipeNameLength >= NIGHT_LIGHT_PIPE_NAME_CHARACTERS
                        || std::wcschr(value, L'\r') != nullptr
                        || std::wcschr(value, L'\n') != nullptr)
                    {
                        return false;
                    }

                    result->PipeName = value;
                    foundPipeName = true;
                    continue;
                }

                return false;
            }

            return foundServerArgument && foundParentProcessID && foundPipeName;
        }

        HANDLE ConnectToParentPipe(const HelperArguments& arguments) noexcept
        {
            std::size_t prefixLength = std::wcslen(PIPE_PATH_PREFIX);
            std::size_t pipeNameLength = std::wcslen(arguments.PipeName);
            std::array<wchar_t, NIGHT_LIGHT_PIPE_NAME_CHARACTERS + 16U> pipePath{};
            if (prefixLength + pipeNameLength + 1U > pipePath.size())
            {
                return INVALID_HANDLE_VALUE;
            }

            std::memcpy(pipePath.data(), PIPE_PATH_PREFIX, prefixLength * sizeof(wchar_t));
            std::memcpy(
                pipePath.data() + prefixLength,
                arguments.PipeName,
                (pipeNameLength + 1U) * sizeof(wchar_t));

            ULONGLONG startedAtTick = GetTickCount64();
            while (GetTickCount64() - startedAtTick < NIGHT_LIGHT_PIPE_CONNECT_TIMEOUT_MS)
            {
                HANDLE pipe = CreateFileW(
                    pipePath.data(),
                    GENERIC_READ | GENERIC_WRITE,
                    0U,
                    nullptr,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    nullptr);
                if (pipe != INVALID_HANDLE_VALUE)
                {
                    ULONG serverProcessID = 0U;
                    if (GetNamedPipeServerProcessId(pipe, &serverProcessID) == FALSE
                        || serverProcessID != arguments.ParentProcessID)
                    {
                        CloseHandle(pipe);
                        return INVALID_HANDLE_VALUE;
                    }

                    DWORD readMode = PIPE_READMODE_BYTE;
                    if (SetNamedPipeHandleState(pipe, &readMode, nullptr, nullptr) == FALSE)
                    {
                        CloseHandle(pipe);
                        return INVALID_HANDLE_VALUE;
                    }

                    return pipe;
                }

                DWORD error = GetLastError();
                if (error != ERROR_PIPE_BUSY && error != ERROR_FILE_NOT_FOUND)
                {
                    return INVALID_HANDLE_VALUE;
                }

                (void)WaitNamedPipeW(pipePath.data(), 50U);
                Sleep(10U);
            }

            return INVALID_HANDLE_VALUE;
        }

        int HexDigitValue(char character) noexcept
        {
            if (character >= '0' && character <= '9')
            {
                return character - '0';
            }

            if (character >= 'a' && character <= 'f')
            {
                return character - 'a' + 10;
            }

            if (character >= 'A' && character <= 'F')
            {
                return character - 'A' + 10;
            }

            return -1;
        }

        bool TryParseHexDWORD(std::string_view value, std::uint32_t* result) noexcept
        {
            if (result == nullptr || value.empty() || value.size() > 8U)
            {
                return false;
            }

            std::uint32_t parsed = 0U;
            for (char character : value)
            {
                int digit = HexDigitValue(character);
                if (digit < 0)
                {
                    return false;
                }

                parsed = (parsed << 4U) | static_cast<std::uint32_t>(digit);
            }

            *result = parsed;
            return true;
        }

        bool TryParseDecimalDWORD(std::string_view value, std::uint32_t* result) noexcept
        {
            if (result == nullptr || value.empty())
            {
                return false;
            }

            std::uint64_t parsed = 0U;
            for (char character : value)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }

                parsed = parsed * 10U + static_cast<std::uint64_t>(character - '0');
                if (parsed > MAXDWORD)
                {
                    return false;
                }
            }

            *result = static_cast<std::uint32_t>(parsed);
            return true;
        }

        bool TryParseGUID(std::string_view value, GUID* result) noexcept
        {
            if (result == nullptr || value.size() != 32U)
            {
                return false;
            }

            std::uint32_t data1 = 0U;
            std::uint32_t data2 = 0U;
            std::uint32_t data3 = 0U;
            if (!TryParseHexDWORD(value.substr(0U, 8U), &data1)
                || !TryParseHexDWORD(value.substr(8U, 4U), &data2)
                || !TryParseHexDWORD(value.substr(12U, 4U), &data3))
            {
                return false;
            }

            GUID parsed{};
            parsed.Data1 = data1;
            parsed.Data2 = static_cast<unsigned short>(data2);
            parsed.Data3 = static_cast<unsigned short>(data3);
            bool hasNonzeroByte = data1 != 0U || data2 != 0U || data3 != 0U;
            for (std::size_t byteIndex = 0U; byteIndex < 8U; byteIndex++)
            {
                int upper = HexDigitValue(value[16U + byteIndex * 2U]);
                int lower = HexDigitValue(value[17U + byteIndex * 2U]);
                if (upper < 0 || lower < 0)
                {
                    return false;
                }

                parsed.Data4[byteIndex] = static_cast<unsigned char>((upper << 4) | lower);
                hasNonzeroByte = hasNonzeroByte || parsed.Data4[byteIndex] != 0U;
            }

            if (!hasNonzeroByte)
            {
                return false;
            }

            *result = parsed;
            return true;
        }

        template <std::size_t FieldCapacity>
        bool TrySplitFields(
            std::string_view line,
            std::array<std::string_view, FieldCapacity>* fields,
            std::size_t* fieldCount) noexcept
        {
            if (fields == nullptr || fieldCount == nullptr || line.empty())
            {
                return false;
            }

            *fieldCount = 0U;
            std::size_t fieldStart = 0U;
            while (fieldStart <= line.size())
            {
                if (*fieldCount >= fields->size())
                {
                    return false;
                }

                std::size_t separator = line.find('\t', fieldStart);
                std::size_t fieldEnd = separator == std::string_view::npos
                    ? line.size()
                    : separator;
                if (fieldEnd == fieldStart)
                {
                    return false;
                }

                std::size_t destinationIndex = *fieldCount;
                (*fields)[destinationIndex] = line.substr(fieldStart, fieldEnd - fieldStart);
                *fieldCount = destinationIndex + 1U;
                if (separator == std::string_view::npos)
                {
                    return true;
                }

                fieldStart = separator + 1U;
            }

            return false;
        }

        bool TryParseBootstrap(
            std::string_view line,
            NightLightBootstrapDescriptor* descriptor) noexcept
        {
            if (descriptor == nullptr)
            {
                return false;
            }

            std::array<std::string_view, 10U> fields{};
            std::size_t fieldCount = 0U;
            if (!TrySplitFields(line, &fields, &fieldCount)
                || fieldCount != fields.size()
                || fields[0] != INITIALIZE_COMMAND
                || fields[1] != PROTOCOL_VERSION
                || !TryParseGUID(fields[2], &descriptor->PDBGuid)
                || !TryParseDecimalDWORD(fields[3], &descriptor->PDBAge)
                || !TryParseHexDWORD(fields[4], &descriptor->ImageSize)
                || !TryParseHexDWORD(fields[5], &descriptor->InitializeRVA)
                || !TryParseHexDWORD(fields[6], &descriptor->SInstanceRVA)
                || !TryParseHexDWORD(fields[7], &descriptor->SetTemperatureRVA)
                || !TryParseHexDWORD(fields[8], &descriptor->SetPreviewRVA)
                || !TryParseHexDWORD(fields[9], &descriptor->SetActiveRVA))
            {
                return false;
            }

            return descriptor->PDBAge != 0U
                && descriptor->ImageSize != 0U
                && descriptor->InitializeRVA != 0U
                && descriptor->SInstanceRVA != 0U
                && descriptor->SetTemperatureRVA != 0U
                && descriptor->SetPreviewRVA != 0U
                && descriptor->SetActiveRVA != 0U;
        }

        bool TryParsePercent(std::string_view value, int* result) noexcept
        {
            std::uint32_t parsed = 0U;
            if (result == nullptr || !TryParseDecimalDWORD(value, &parsed) || parsed > 100U)
            {
                return false;
            }

            *result = static_cast<int>(parsed);
            return true;
        }

        bool ProcessCommand(
            std::string_view line,
            NightLightBackend* backend,
            std::string_view* response,
            bool* shouldExit) noexcept
        {
            if (backend == nullptr || response == nullptr || shouldExit == nullptr)
            {
                return false;
            }

            *shouldExit = false;
            if (line == PING_COMMAND)
            {
                *response = PONG_RESPONSE;
                return true;
            }

            if (line == DRAIN_COMMAND)
            {
                *response = backend->Drain() ? DRAINED_RESPONSE : FAILURE_RESPONSE;
                return true;
            }

            if (line == EXIT_COMMAND)
            {
                (void)backend->Drain();
                *shouldExit = true;
                return true;
            }

            std::array<std::string_view, 3U> fields{};
            std::size_t fieldCount = 0U;
            if (!TrySplitFields(line, &fields, &fieldCount))
            {
                *response = FAILURE_RESPONSE;
                return true;
            }

            if (fieldCount == 2U && fields[0] == SET_STRENGTH_COMMAND)
            {
                int percent = 0;
                *response = TryParsePercent(fields[1], &percent)
                    && backend->QueueStrengthPercent(percent)
                    ? SUCCESS_RESPONSE
                    : FAILURE_RESPONSE;
                return true;
            }

            if ((fieldCount == 2U || fieldCount == 3U) && fields[0] == SET_ACTIVE_COMMAND)
            {
                bool enabled = false;
                if (fields[1] == "1")
                {
                    enabled = true;
                }
                else if (fields[1] != "0")
                {
                    *response = FAILURE_RESPONSE;
                    return true;
                }

                bool hasEnableStrength = fieldCount == 3U;
                int enableStrength = 0;
                if ((hasEnableStrength && !enabled)
                    || (hasEnableStrength && !TryParsePercent(fields[2], &enableStrength)))
                {
                    *response = FAILURE_RESPONSE;
                    return true;
                }

                *response = backend->SetActive(enabled, hasEnableStrength, enableStrength)
                    ? SUCCESS_RESPONSE
                    : FAILURE_RESPONSE;
                return true;
            }

            *response = FAILURE_RESPONSE;
            return true;
        }

        int RunNightLightHelperCore(int argumentCount, wchar_t* arguments[]) noexcept
        {
            HelperArguments helperArguments{};
            if (!TryParseArguments(argumentCount, arguments, &helperArguments))
            {
                return ERROR_INVALID_PARAMETER;
            }

            ParentWatchdog parentWatchdog;
            if (!parentWatchdog.Start(helperArguments.ParentProcessID))
            {
                return ERROR_INVALID_HANDLE;
            }

            ScopedHandle pipe(ConnectToParentPipe(helperArguments));
            if (pipe.Get() == INVALID_HANDLE_VALUE)
            {
                pipe.Reset();
                return ERROR_PIPE_NOT_CONNECTED;
            }

            PipeChannel channel(pipe.Get());
            std::array<char, NIGHT_LIGHT_COMMAND_BUFFER_BYTES> lineBuffer{};
            if (!channel.ReadLine(lineBuffer.data(), lineBuffer.size()))
            {
                return ERROR_BROKEN_PIPE;
            }

            NightLightBootstrapDescriptor descriptor{};
            if (!TryParseBootstrap(lineBuffer.data(), &descriptor))
            {
                (void)channel.WriteUnsupported("invalid-init");
                return ERROR_INVALID_DATA;
            }

            NightLightBackend backend;
            if (!backend.Start(descriptor))
            {
                NightLightBackendStartError startError = backend.GetStartError();
                if (startError == NightLightBackendStartError::ImageIdentity)
                {
                    (void)channel.WriteLine(IMAGE_MISMATCH_RESPONSE);
                    return ERROR_REVISION_MISMATCH;
                }

                const char* failureReason = GetNightLightBackendStartErrorToken(startError);
                (void)channel.WriteUnsupported(failureReason);
                return ERROR_NOT_SUPPORTED;
            }

            if (!channel.WriteLine(READY_RESPONSE))
            {
                return ERROR_BROKEN_PIPE;
            }

            while (channel.ReadLine(lineBuffer.data(), lineBuffer.size()))
            {
                std::string_view response{};
                bool shouldExit = false;
                if (!ProcessCommand(lineBuffer.data(), &backend, &response, &shouldExit))
                {
                    return ERROR_INVALID_DATA;
                }

                if (shouldExit)
                {
                    return ERROR_SUCCESS;
                }

                if (!channel.WriteLine(response))
                {
                    return ERROR_BROKEN_PIPE;
                }
            }

            return ERROR_SUCCESS;
        }
    }

    int RunNightLightHelper(int argumentCount, wchar_t* arguments[]) noexcept
    {
        try
        {
            return RunNightLightHelperCore(argumentCount, arguments);
        }
        catch (...)
        {
            return ERROR_UNHANDLED_EXCEPTION;
        }
    }
}
