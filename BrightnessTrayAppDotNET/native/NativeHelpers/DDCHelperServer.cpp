#include "DDCHelperServer.h"

#include "DDCHelperProtocol.h"
#include "ParentWatchdog.h"
#include "PipeChannel.h"

#include <windows.h>
#include <highlevelmonitorconfigurationapi.h>
#include <lowlevelmonitorconfigurationapi.h>
#include <physicalmonitorenumerationapi.h>

#include <array>
#include <charconv>
#include <cstdint>
#include <cstdio>
#include <cwchar>
#include <new>
#include <string>
#include <string_view>
#include <vector>

#pragma comment(lib, "Advapi32.lib")
#pragma comment(lib, "Dxva2.lib")
#pragma comment(lib, "User32.lib")

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    namespace
    {
        constexpr size_t EDID_BLOCK_LENGTH = 128U;
        constexpr size_t EDID_DESCRIPTOR_BASE = 54U;
        constexpr size_t EDID_DESCRIPTOR_COUNT = 4U;
        constexpr size_t EDID_DESCRIPTOR_LENGTH = 18U;
        constexpr BYTE EDID_SERIAL_STRING_TAG = 0xFFU;

        struct HelperArguments
        {
            DWORD ParentProcessID;
            std::wstring PipeName;
        };

        struct MonitorCandidate
        {
            HMONITOR MonitorHandle;
            std::wstring DeviceID;
            std::wstring DisplayInstancePath;
            std::wstring EDIDSerial;
            std::wstring Name;
        };

        struct MonitorEnumerationState
        {
            std::vector<MonitorCandidate>* Candidates;
            bool AllocationFailed;
        };

        class PhysicalMonitorHandles final
        {
        public:
            PhysicalMonitorHandles() = default;
            PhysicalMonitorHandles(const PhysicalMonitorHandles&) = delete;
            PhysicalMonitorHandles& operator=(const PhysicalMonitorHandles&) = delete;

            ~PhysicalMonitorHandles() noexcept
            {
                if (!_monitors.empty())
                    DestroyPhysicalMonitors(static_cast<DWORD>(_monitors.size()), _monitors.data());
            }

            bool Open(HMONITOR monitorHandle, DWORD* error)
            {
                DWORD monitorCount = 0U;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitorHandle, &monitorCount))
                {
                    *error = GetLastError();
                    return false;
                }

                if (monitorCount == 0U)
                {
                    *error = ERROR_NOT_FOUND;
                    return false;
                }

                _monitors.resize(monitorCount);
                if (!GetPhysicalMonitorsFromHMONITOR(monitorHandle, monitorCount, _monitors.data()))
                {
                    *error = GetLastError();
                    _monitors.clear();
                    return false;
                }

                return true;
            }

            [[nodiscard]] HANDLE First() const noexcept
            {
                return _monitors.front().hPhysicalMonitor;
            }

        private:
            std::vector<PHYSICAL_MONITOR> _monitors;
        };

        bool EqualsOrdinalIgnoreCase(const std::wstring& left, const std::wstring& right) noexcept
        {
            if (left.size() != right.size())
                return false;

            return CompareStringOrdinal(
                       left.data(),
                       static_cast<int>(left.size()),
                       right.data(),
                       static_cast<int>(right.size()),
                       TRUE) == CSTR_EQUAL;
        }

        bool TryParseDWORD(const wchar_t* value, DWORD* parsedValue) noexcept
        {
            if (value == nullptr || *value == L'\0' || parsedValue == nullptr)
                return false;

            wchar_t* end = nullptr;
            unsigned long parsed = wcstoul(value, &end, 10);
            if (end == value || *end != L'\0')
                return false;

            *parsedValue = static_cast<DWORD>(parsed);
            return true;
        }

        const wchar_t* FindArgumentValue(
            int argumentCount,
            wchar_t* arguments[],
            const wchar_t* argumentName) noexcept
        {
            if (arguments == nullptr)
                return nullptr;

            for (int argumentIndex = 2; argumentIndex < argumentCount - 1; ++argumentIndex)
            {
                if (arguments[argumentIndex] != nullptr &&
                    wcscmp(arguments[argumentIndex], argumentName) == 0)
                {
                    return arguments[argumentIndex + 1];
                }
            }

            return nullptr;
        }

        bool TryParseArguments(
            int argumentCount,
            wchar_t* arguments[],
            HelperArguments* helperArguments)
        {
            if (argumentCount < 6 ||
                arguments == nullptr ||
                arguments[1] == nullptr ||
                wcscmp(arguments[1], DDC_HELPER_MODE_ARGUMENT) != 0 ||
                helperArguments == nullptr)
            {
                return false;
            }

            const wchar_t* parentProcessIDValue = FindArgumentValue(
                argumentCount,
                arguments,
                DDC_HELPER_PARENT_PROCESS_ID_ARGUMENT);
            const wchar_t* pipeNameValue = FindArgumentValue(
                argumentCount,
                arguments,
                DDC_HELPER_PIPE_NAME_ARGUMENT);
            if (!TryParseDWORD(parentProcessIDValue, &helperArguments->ParentProcessID) ||
                helperArguments->ParentProcessID == 0U ||
                pipeNameValue == nullptr ||
                *pipeNameValue == L'\0' ||
                wcslen(pipeNameValue) > 240U)
            {
                return false;
            }

            helperArguments->PipeName = pipeNameValue;
            return true;
        }

        int DecodeBase64Character(unsigned char character) noexcept
        {
            if (character >= 'A' && character <= 'Z')
                return character - 'A';
            if (character >= 'a' && character <= 'z')
                return character - 'a' + 26;
            if (character >= '0' && character <= '9')
                return character - '0' + 52;
            if (character == '+')
                return 62;
            if (character == '/')
                return 63;
            return -1;
        }

        bool TryDecodeBase64(std::string_view encoded, std::string* decoded)
        {
            decoded->clear();
            if (encoded.empty())
                return true;
            if (encoded.size() % 4U != 0U)
                return false;

            decoded->reserve(encoded.size() / 4U * 3U);
            for (size_t quartetOffset = 0U; quartetOffset < encoded.size(); quartetOffset += 4U)
            {
                int first = DecodeBase64Character(static_cast<unsigned char>(encoded[quartetOffset]));
                int second = DecodeBase64Character(static_cast<unsigned char>(encoded[quartetOffset + 1U]));
                bool thirdIsPadding = encoded[quartetOffset + 2U] == '=';
                bool fourthIsPadding = encoded[quartetOffset + 3U] == '=';
                int third = thirdIsPadding
                    ? 0
                    : DecodeBase64Character(static_cast<unsigned char>(encoded[quartetOffset + 2U]));
                int fourth = fourthIsPadding
                    ? 0
                    : DecodeBase64Character(static_cast<unsigned char>(encoded[quartetOffset + 3U]));
                bool isLastQuartet = quartetOffset + 4U == encoded.size();
                if (first < 0 || second < 0 || third < 0 || fourth < 0 ||
                    thirdIsPadding && !fourthIsPadding ||
                    (thirdIsPadding || fourthIsPadding) && !isLastQuartet)
                {
                    decoded->clear();
                    return false;
                }

                uint32_t packed =
                    static_cast<uint32_t>(first) << 18U |
                    static_cast<uint32_t>(second) << 12U |
                    static_cast<uint32_t>(third) << 6U |
                    static_cast<uint32_t>(fourth);
                decoded->push_back(static_cast<char>((packed >> 16U) & 0xFFU));
                if (!thirdIsPadding)
                    decoded->push_back(static_cast<char>((packed >> 8U) & 0xFFU));
                if (!fourthIsPadding)
                    decoded->push_back(static_cast<char>(packed & 0xFFU));
            }

            return true;
        }

        std::string EncodeBase64(std::string_view value)
        {
            constexpr char BASE64_ALPHABET[] =
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            if (value.empty())
                return std::string();

            std::string encoded;
            encoded.reserve((value.size() + 2U) / 3U * 4U);
            for (size_t byteOffset = 0U; byteOffset < value.size(); byteOffset += 3U)
            {
                size_t remaining = value.size() - byteOffset;
                uint32_t packed = static_cast<unsigned char>(value[byteOffset]) << 16U;
                if (remaining > 1U)
                    packed |= static_cast<unsigned char>(value[byteOffset + 1U]) << 8U;
                if (remaining > 2U)
                    packed |= static_cast<unsigned char>(value[byteOffset + 2U]);

                encoded.push_back(BASE64_ALPHABET[(packed >> 18U) & 0x3FU]);
                encoded.push_back(BASE64_ALPHABET[(packed >> 12U) & 0x3FU]);
                encoded.push_back(remaining > 1U ? BASE64_ALPHABET[(packed >> 6U) & 0x3FU] : '=');
                encoded.push_back(remaining > 2U ? BASE64_ALPHABET[packed & 0x3FU] : '=');
            }

            return encoded;
        }

        bool TryConvertUTF8ToWide(std::string_view value, std::wstring* converted)
        {
            converted->clear();
            if (value.empty())
                return true;

            int characterCount = MultiByteToWideChar(
                CP_UTF8,
                MB_ERR_INVALID_CHARS,
                value.data(),
                static_cast<int>(value.size()),
                nullptr,
                0);
            if (characterCount <= 0)
                return false;

            converted->resize(static_cast<size_t>(characterCount));
            return MultiByteToWideChar(
                       CP_UTF8,
                       MB_ERR_INVALID_CHARS,
                       value.data(),
                       static_cast<int>(value.size()),
                       converted->data(),
                       characterCount) == characterCount;
        }

        bool TryDecodeIdentity(std::string_view encoded, std::wstring* decoded)
        {
            std::string utf8Value;
            return TryDecodeBase64(encoded, &utf8Value) && TryConvertUTF8ToWide(utf8Value, decoded);
        }

        std::vector<std::string_view> SplitFields(const std::string& line)
        {
            std::vector<std::string_view> fields;
            fields.reserve(8U);
            size_t fieldStart = 0U;
            while (true)
            {
                size_t separator = line.find('\t', fieldStart);
                if (separator == std::string::npos)
                {
                    fields.emplace_back(line.data() + fieldStart, line.size() - fieldStart);
                    break;
                }

                fields.emplace_back(line.data() + fieldStart, separator - fieldStart);
                fieldStart = separator + 1U;
            }

            return fields;
        }

        std::string FormatWindowsError(std::string_view operation, DWORD error)
        {
            char hexadecimal[11]{};
            int printResult = _snprintf_s(
                hexadecimal,
                sizeof(hexadecimal),
                _TRUNCATE,
                "0x%08lX",
                error);
            if (printResult < 0)
                hexadecimal[0] = '\0';

            std::string message(operation);
            message += " failed (Win32: ";
            message += std::to_string(static_cast<int32_t>(error));
            message += ", ";
            message += hexadecimal;
            message += ')';
            return message;
        }

        std::string BuildFailure(std::string_view error)
        {
            return "FAIL\t" + EncodeBase64(error);
        }

        std::wstring ResolveDeviceID(const std::wstring& adapterName)
        {
            DISPLAY_DEVICEW displayDevice{};
            displayDevice.cb = sizeof(displayDevice);
            if (EnumDisplayDevicesW(adapterName.c_str(), 0U, &displayDevice, 0U) &&
                displayDevice.DeviceID[0] != L'\0')
            {
                return displayDevice.DeviceID;
            }

            return adapterName;
        }

        std::wstring ResolveDisplayInstancePath(const std::wstring& adapterName)
        {
            DISPLAY_DEVICEW displayDevice{};
            displayDevice.cb = sizeof(displayDevice);
            if (!EnumDisplayDevicesW(
                    adapterName.c_str(),
                    0U,
                    &displayDevice,
                    EDD_GET_DEVICE_INTERFACE_NAME) ||
                displayDevice.DeviceID[0] == L'\0')
            {
                return std::wstring();
            }

            std::wstring interfacePath(displayDevice.DeviceID);
            constexpr std::wstring_view PREFIX = L"\\\\?\\";
            if (!interfacePath.starts_with(PREFIX))
                return std::wstring();

            std::wstring body = interfacePath.substr(PREFIX.size());
            size_t lastHash = body.find_last_of(L'#');
            if (lastHash == std::wstring::npos || lastHash == 0U)
                return std::wstring();

            body.resize(lastHash);
            for (wchar_t& character : body)
            {
                if (character == L'#')
                    character = L'\\';
            }

            return body;
        }

        bool HasValidEDIDHeader(const std::vector<BYTE>& edid) noexcept
        {
            return edid.size() >= EDID_BLOCK_LENGTH &&
                   edid[0] == 0x00U &&
                   edid[1] == 0xFFU &&
                   edid[2] == 0xFFU &&
                   edid[3] == 0xFFU &&
                   edid[4] == 0xFFU &&
                   edid[5] == 0xFFU &&
                   edid[6] == 0xFFU &&
                   edid[7] == 0x00U;
        }

        std::wstring ExtractEDIDSerial(const std::vector<BYTE>& edid)
        {
            if (!HasValidEDIDHeader(edid))
                return std::wstring();

            for (size_t descriptorIndex = 0U;
                 descriptorIndex < EDID_DESCRIPTOR_COUNT;
                 ++descriptorIndex)
            {
                size_t descriptorOffset =
                    EDID_DESCRIPTOR_BASE + descriptorIndex * EDID_DESCRIPTOR_LENGTH;
                if (edid[descriptorOffset] != 0x00U ||
                    edid[descriptorOffset + 1U] != 0x00U ||
                    edid[descriptorOffset + 2U] != 0x00U ||
                    edid[descriptorOffset + 3U] != EDID_SERIAL_STRING_TAG)
                {
                    continue;
                }

                std::wstring serial;
                serial.reserve(13U);
                for (size_t characterIndex = 0U; characterIndex < 13U; ++characterIndex)
                {
                    BYTE character = edid[descriptorOffset + 5U + characterIndex];
                    if (character == 0x0AU)
                        break;
                    if (character >= 0x20U && character <= 0x7EU)
                        serial.push_back(static_cast<wchar_t>(character));
                }

                size_t firstContent = serial.find_first_not_of(L" \t\r\n");
                if (firstContent == std::wstring::npos)
                    break;
                size_t lastContent = serial.find_last_not_of(L" \t\r\n");
                return serial.substr(firstContent, lastContent - firstContent + 1U);
            }

            uint32_t numericSerial =
                static_cast<uint32_t>(edid[12]) |
                static_cast<uint32_t>(edid[13]) << 8U |
                static_cast<uint32_t>(edid[14]) << 16U |
                static_cast<uint32_t>(edid[15]) << 24U;
            return numericSerial == 0U ? std::wstring() : std::to_wstring(numericSerial);
        }

        std::wstring ReadEDIDSerial(const std::wstring& displayInstancePath)
        {
            if (displayInstancePath.empty())
                return std::wstring();

            std::wstring registryPath =
                L"SYSTEM\\CurrentControlSet\\Enum\\" +
                displayInstancePath +
                L"\\Device Parameters";
            HKEY registryKey = nullptr;
            LSTATUS openStatus = RegOpenKeyExW(
                HKEY_LOCAL_MACHINE,
                registryPath.c_str(),
                0U,
                KEY_QUERY_VALUE,
                &registryKey);
            if (openStatus != ERROR_SUCCESS)
                return std::wstring();

            DWORD valueType = 0U;
            DWORD byteCount = 0U;
            LSTATUS queryStatus = RegQueryValueExW(
                registryKey,
                L"EDID",
                nullptr,
                &valueType,
                nullptr,
                &byteCount);
            if (queryStatus != ERROR_SUCCESS ||
                valueType != REG_BINARY ||
                byteCount < EDID_BLOCK_LENGTH ||
                byteCount > 65536U)
            {
                RegCloseKey(registryKey);
                return std::wstring();
            }

            std::vector<BYTE> edid(byteCount);
            queryStatus = RegQueryValueExW(
                registryKey,
                L"EDID",
                nullptr,
                &valueType,
                edid.data(),
                &byteCount);
            RegCloseKey(registryKey);
            if (queryStatus != ERROR_SUCCESS || valueType != REG_BINARY)
                return std::wstring();

            edid.resize(byteCount);
            return ExtractEDIDSerial(edid);
        }

        BOOL CALLBACK EnumerateMonitor(
            HMONITOR monitorHandle,
            HDC,
            LPRECT,
            LPARAM parameter) noexcept
        {
            MonitorEnumerationState* state = reinterpret_cast<MonitorEnumerationState*>(parameter);
            try
            {
                MONITORINFOEXW monitorInfo{};
                monitorInfo.cbSize = sizeof(monitorInfo);
                if (!GetMonitorInfoW(monitorHandle, &monitorInfo))
                    return TRUE;

                MonitorCandidate candidate{};
                candidate.MonitorHandle = monitorHandle;
                candidate.Name = monitorInfo.szDevice;
                candidate.DeviceID = ResolveDeviceID(candidate.Name);
                candidate.DisplayInstancePath = ResolveDisplayInstancePath(candidate.Name);
                candidate.EDIDSerial = ReadEDIDSerial(candidate.DisplayInstancePath);
                state->Candidates->emplace_back(std::move(candidate));
                return TRUE;
            }
            catch (...)
            {
                state->AllocationFailed = true;
                return FALSE;
            }
        }

        bool TryEnumerateMonitors(std::vector<MonitorCandidate>* candidates, DWORD* error)
        {
            candidates->clear();
            candidates->reserve(16U);
            MonitorEnumerationState state{candidates, false};
            BOOL enumerationSucceeded = EnumDisplayMonitors(
                nullptr,
                nullptr,
                EnumerateMonitor,
                reinterpret_cast<LPARAM>(&state));
            if (state.AllocationFailed)
            {
                *error = ERROR_OUTOFMEMORY;
                return false;
            }

            if (!enumerationSucceeded)
            {
                *error = GetLastError();
                return false;
            }

            return true;
        }

        const MonitorCandidate* FindMonitor(
            const std::vector<MonitorCandidate>& candidates,
            const MonitorCandidate& identity) noexcept
        {
            if (!identity.DeviceID.empty())
            {
                for (const MonitorCandidate& candidate : candidates)
                {
                    if (EqualsOrdinalIgnoreCase(candidate.DeviceID, identity.DeviceID))
                        return &candidate;
                }
            }

            if (!identity.DisplayInstancePath.empty())
            {
                for (const MonitorCandidate& candidate : candidates)
                {
                    if (EqualsOrdinalIgnoreCase(
                            candidate.DisplayInstancePath,
                            identity.DisplayInstancePath))
                    {
                        return &candidate;
                    }
                }
            }

            if (!identity.EDIDSerial.empty())
            {
                for (const MonitorCandidate& candidate : candidates)
                {
                    if (candidate.EDIDSerial == identity.EDIDSerial)
                        return &candidate;
                }
            }

            if (!identity.Name.empty())
            {
                for (const MonitorCandidate& candidate : candidates)
                {
                    if (EqualsOrdinalIgnoreCase(candidate.Name, identity.Name))
                        return &candidate;
                }
            }

            return nullptr;
        }

        template <typename Integer>
        bool TryParseInteger(std::string_view value, int radix, Integer* parsedValue) noexcept
        {
            Integer parsed{};
            std::from_chars_result result = std::from_chars(
                value.data(),
                value.data() + value.size(),
                parsed,
                radix);
            if (result.ec != std::errc() || result.ptr != value.data() + value.size())
                return false;

            *parsedValue = parsed;
            return true;
        }

        std::string HandleCapabilities(HANDLE physicalMonitor)
        {
            DWORD length = 0U;
            if (!GetCapabilitiesStringLength(physicalMonitor, &length))
                return BuildFailure(FormatWindowsError("GetCapabilitiesStringLength", GetLastError()));
            if (length == 0U || length > DDC_HELPER_MAX_CAPABILITIES_BYTES)
                return BuildFailure("GetCapabilitiesStringLength returned an invalid length.");

            std::vector<char> capabilities(length, '\0');
            if (!CapabilitiesRequestAndCapabilitiesReply(physicalMonitor, capabilities.data(), length))
            {
                return BuildFailure(
                    FormatWindowsError("CapabilitiesRequestAndCapabilitiesReply", GetLastError()));
            }

            size_t capabilityLength = 0U;
            while (capabilityLength < capabilities.size() && capabilities[capabilityLength] != '\0')
                ++capabilityLength;
            std::string_view capabilityValue(capabilities.data(), capabilityLength);
            return "OK\t" + EncodeBase64(capabilityValue);
        }

        std::string HandleGetVCP(
            HANDLE physicalMonitor,
            const std::vector<std::string_view>& fields)
        {
            if (fields.size() < 6U)
                return BuildFailure("Malformed GETVCP command.");

            unsigned int parsedCode = 0U;
            if (!TryParseInteger(fields[5], 16, &parsedCode) || parsedCode > 0xFFU)
                return BuildFailure("Malformed GETVCP code.");

            DWORD currentValue = 0U;
            DWORD maximumValue = 0U;
            if (!GetVCPFeatureAndVCPFeatureReply(
                    physicalMonitor,
                    static_cast<BYTE>(parsedCode),
                    nullptr,
                    &currentValue,
                    &maximumValue))
            {
                return BuildFailure(
                    FormatWindowsError("GetVCPFeatureAndVCPFeatureReply", GetLastError()));
            }

            return "OK\t" +
                   std::to_string(currentValue) +
                   "\t" +
                   std::to_string(maximumValue);
        }

        std::string HandleSetVCP(
            HANDLE physicalMonitor,
            const std::vector<std::string_view>& fields)
        {
            if (fields.size() < 7U)
                return BuildFailure("Malformed SETVCP command.");

            unsigned int parsedCode = 0U;
            DWORD value = 0U;
            if (!TryParseInteger(fields[5], 16, &parsedCode) || parsedCode > 0xFFU)
                return BuildFailure("Malformed SETVCP code.");
            if (!TryParseInteger(fields[6], 10, &value))
                return BuildFailure("Malformed SETVCP value.");

            if (!SetVCPFeature(physicalMonitor, static_cast<BYTE>(parsedCode), value))
                return BuildFailure(FormatWindowsError("SetVCPFeature", GetLastError()));

            return "OK";
        }

        std::string HandleCommand(const std::string& command)
        {
            std::vector<std::string_view> fields = SplitFields(command);
            if (fields.size() < 5U)
                return BuildFailure("Malformed DDC helper command.");

            MonitorCandidate identity{};
            if (!TryDecodeIdentity(fields[1], &identity.DeviceID) ||
                !TryDecodeIdentity(fields[2], &identity.EDIDSerial) ||
                !TryDecodeIdentity(fields[3], &identity.Name) ||
                !TryDecodeIdentity(fields[4], &identity.DisplayInstancePath))
            {
                return BuildFailure("Malformed DDC helper identity.");
            }

            DWORD error = ERROR_SUCCESS;
            std::vector<MonitorCandidate> candidates;
            if (!TryEnumerateMonitors(&candidates, &error))
                return BuildFailure(FormatWindowsError("EnumDisplayMonitors", error));

            const MonitorCandidate* monitor = FindMonitor(candidates, identity);
            if (monitor == nullptr)
                return BuildFailure("No matching monitor was found in the DDC helper process.");

            PhysicalMonitorHandles physicalMonitors;
            if (!physicalMonitors.Open(monitor->MonitorHandle, &error))
            {
                return BuildFailure(
                    FormatWindowsError("GetPhysicalMonitorsFromHMONITOR", error));
            }

            if (fields[0] == "CAPS")
                return HandleCapabilities(physicalMonitors.First());
            if (fields[0] == "GETVCP")
                return HandleGetVCP(physicalMonitors.First(), fields);
            if (fields[0] == "SETVCP")
                return HandleSetVCP(physicalMonitors.First(), fields);

            return BuildFailure("Unknown DDC helper command.");
        }

        DWORD RunRequestLoop(PipeChannel* channel)
        {
            DWORD error = ERROR_SUCCESS;
            if (!channel->WriteLine(DDC_HELPER_READY_RESPONSE, &error))
                return error;

            while (true)
            {
                std::string command;
                PipeLineReadResult readResult = channel->ReadLine(&command, &error);
                switch (readResult)
                {
                    case PipeLineReadResult::End:
                        return ERROR_SUCCESS;
                    case PipeLineReadResult::Failed:
                        return error;
                    case PipeLineReadResult::TooLong:
                        if (!channel->WriteLine(BuildFailure("DDC helper command exceeds the size limit."), &error))
                            return error;
                        continue;
                    case PipeLineReadResult::Line:
                        break;
                }

                if (command == DDC_HELPER_EXIT_COMMAND)
                    return ERROR_SUCCESS;

                std::string response;
                if (command == DDC_HELPER_PING_COMMAND)
                    response = DDC_HELPER_PING_RESPONSE;
                else
                    response = HandleCommand(command);

                if (!channel->WriteLine(response, &error))
                    return error;
            }
        }
    }

    int RunDDCHelper(int argumentCount, wchar_t* arguments[]) noexcept
    {
        try
        {
            HelperArguments helperArguments{};
            if (!TryParseArguments(argumentCount, arguments, &helperArguments))
                return ERROR_INVALID_PARAMETER;

            DWORD watchdogError = StartParentWatchdog(helperArguments.ParentProcessID);
            if (watchdogError != ERROR_SUCCESS)
                return static_cast<int>(watchdogError);

            DWORD connectError = ERROR_SUCCESS;
            PipeChannel channel = PipeChannel::Connect(
                helperArguments.PipeName,
                DDC_HELPER_CONNECT_TIMEOUT_MS,
                &connectError);
            if (!channel.IsOpen())
                return static_cast<int>(connectError);

            return static_cast<int>(RunRequestLoop(&channel));
        }
        catch (const std::bad_alloc&)
        {
            return ERROR_OUTOFMEMORY;
        }
        catch (...)
        {
            return ERROR_UNHANDLED_EXCEPTION;
        }
    }
}
