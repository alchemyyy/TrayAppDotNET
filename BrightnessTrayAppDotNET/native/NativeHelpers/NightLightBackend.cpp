#include "NightLightBackend.h"

#include <roapi.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "runtimeobject.lib")

#define NIGHT_LIGHT_BACKEND_START_TIMEOUT_MS 60000U
#define NIGHT_LIGHT_BACKEND_SHUTDOWN_TIMEOUT_MS 10000U
#define NIGHT_LIGHT_CLOUD_STORE_FALLBACK_DWELL_MS 250U
#define NIGHT_LIGHT_MAX_IMAGE_SECTIONS 96U
#define NIGHT_LIGHT_MAX_REGISTRY_BLOB_BYTES 4096U
#define NIGHT_LIGHT_PREVIEW_RELEASE_DELAY_MS 150U
#define NIGHT_LIGHT_SAVE_NOTIFY_TIMEOUT_MS 1500U
#define NIGHT_LIGHT_STATE_READBACK_POLL_MS 25U

#if defined(_MSC_VER)
#define NIGHT_LIGHT_UNCHECKED_CALL __declspec(noinline) __declspec(guard(nocf))
#else
#define NIGHT_LIGHT_UNCHECKED_CALL
#endif

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    namespace
    {
        constexpr wchar_t SETTINGS_HANDLERS_DLL_NAME[] = L"SettingsHandlers_Display.dll";
        constexpr wchar_t SETTINGS_BLOB_KEY_PATH[] =
            L"Software\\Microsoft\\Windows\\CurrentVersion\\CloudStore\\Store\\DefaultAccount\\Current\\"
            L"default$windows.data.bluelightreduction.settings\\"
            L"windows.data.bluelightreduction.settings";
        constexpr wchar_t STATE_BLOB_KEY_PATH[] =
            L"Software\\Microsoft\\Windows\\CurrentVersion\\CloudStore\\Store\\DefaultAccount\\Current\\"
            L"default$windows.data.bluelightreduction.bluelightreductionstate\\"
            L"windows.data.bluelightreduction.bluelightreductionstate";
        constexpr wchar_t CLOUD_STORE_DATA_VALUE_NAME[] = L"Data";

        constexpr int MINIMUM_KELVIN = 1200;
        constexpr int MAXIMUM_KELVIN = 6500;
        constexpr std::uint32_t SINGLETON_REQUIRED_BYTES = 304U;
        constexpr std::uint32_t CODEVIEW_RSDS_SIGNATURE = 0x53445352U;

        struct NightLightStateStatus
        {
            bool IsInitialized;
            bool IsEnabled;
        };

        struct CodeViewHeader
        {
            std::uint32_t Signature;
            GUID Guid;
            std::uint32_t Age;
        };

        static_assert(sizeof(CodeViewHeader) == 24U);

        enum class ImageValidationResult : std::uint32_t
        {
            Valid,
            IdentityMismatch,
            InvalidRVA
        };

        class SRWExclusiveLockGuard final
        {
        public:
            explicit SRWExclusiveLockGuard(SRWLOCK* lock) noexcept : _lock(lock)
            {
                AcquireSRWLockExclusive(_lock);
            }

            SRWExclusiveLockGuard(const SRWExclusiveLockGuard&) = delete;
            SRWExclusiveLockGuard& operator=(const SRWExclusiveLockGuard&) = delete;

            ~SRWExclusiveLockGuard() noexcept
            {
                ReleaseSRWLockExclusive(_lock);
            }

        private:
            SRWLOCK* _lock;
        };

        class RegistryNotification final
        {
        public:
            explicit RegistryNotification(const wchar_t* keyPath) noexcept :
                _key(nullptr),
                _event(nullptr),
                _armed(false)
            {
                LSTATUS openResult = RegOpenKeyExW(
                    HKEY_CURRENT_USER,
                    keyPath,
                    0,
                    KEY_NOTIFY,
                    &_key);
                if (openResult != ERROR_SUCCESS)
                {
                    _key = nullptr;
                    return;
                }

                _event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
                if (_event == nullptr)
                {
                    return;
                }

                LSTATUS notifyResult = RegNotifyChangeKeyValue(
                    _key,
                    FALSE,
                    REG_NOTIFY_CHANGE_LAST_SET,
                    _event,
                    TRUE);
                _armed = notifyResult == ERROR_SUCCESS;
            }

            RegistryNotification(const RegistryNotification&) = delete;
            RegistryNotification& operator=(const RegistryNotification&) = delete;

            ~RegistryNotification() noexcept
            {
                if (_key != nullptr)
                {
                    RegCloseKey(_key);
                }

                if (_event != nullptr)
                {
                    CloseHandle(_event);
                }
            }

            void WaitForSave() const noexcept
            {
                if (_armed)
                {
                    (void)WaitForSingleObject(_event, NIGHT_LIGHT_SAVE_NOTIFY_TIMEOUT_MS);
                    return;
                }

                Sleep(NIGHT_LIGHT_CLOUD_STORE_FALLBACK_DWELL_MS);
            }

        private:
            HKEY _key;
            HANDLE _event;
            bool _armed;
        };

        bool TryAddAddress(
            std::uintptr_t baseAddress,
            std::uint32_t offset,
            std::uintptr_t* result) noexcept
        {
            if (result == nullptr || baseAddress > UINTPTR_MAX - offset)
            {
                return false;
            }

            *result = baseAddress + offset;
            return true;
        }

        bool TryReadLocalMemory(
            std::uintptr_t address,
            void* destination,
            SIZE_T byteCount) noexcept
        {
            if (address == 0U || destination == nullptr || byteCount == 0U)
            {
                return false;
            }

            SIZE_T bytesRead = 0U;
            BOOL readResult = ReadProcessMemory(
                GetCurrentProcess(),
                reinterpret_cast<const void*>(address),
                destination,
                byteCount,
                &bytesRead);
            return readResult != FALSE && bytesRead == byteCount;
        }

        bool IsImageRangeValid(
            std::uint32_t imageSize,
            std::uint32_t startRVA,
            std::uint32_t byteCount) noexcept
        {
            if (startRVA == 0U || byteCount == 0U || startRVA >= imageSize)
            {
                return false;
            }

            return byteCount <= imageSize - startRVA;
        }

        bool IsRVAInSection(
            const IMAGE_SECTION_HEADER* sections,
            WORD sectionCount,
            std::uint32_t rva,
            std::uint32_t byteCount,
            bool executable) noexcept
        {
            if (sections == nullptr || byteCount == 0U)
            {
                return false;
            }

            for (WORD sectionIndex = 0U; sectionIndex < sectionCount; sectionIndex++)
            {
                const IMAGE_SECTION_HEADER& section = sections[sectionIndex];
                std::uint32_t sectionSize = section.Misc.VirtualSize;
                if (section.SizeOfRawData > sectionSize)
                {
                    sectionSize = section.SizeOfRawData;
                }

                if (sectionSize == 0U || rva < section.VirtualAddress)
                {
                    continue;
                }

                std::uint32_t relativeOffset = rva - section.VirtualAddress;
                if (relativeOffset >= sectionSize || byteCount > sectionSize - relativeOffset)
                {
                    continue;
                }

                DWORD characteristics = section.Characteristics;
                if (executable)
                {
                    DWORD required = IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ;
                    return (characteristics & required) == required
                        && (characteristics & IMAGE_SCN_MEM_WRITE) == 0U;
                }

                DWORD required = IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE;
                return (characteristics & required) == required
                    && (characteristics & IMAGE_SCN_MEM_EXECUTE) == 0U;
            }

            return false;
        }

        bool HasMatchingCodeViewIdentity(
            std::uintptr_t moduleBase,
            std::uint32_t imageSize,
            const IMAGE_DATA_DIRECTORY& debugDirectory,
            const GUID& expectedGuid,
            std::uint32_t expectedAge) noexcept
        {
            if (!IsImageRangeValid(imageSize, debugDirectory.VirtualAddress, debugDirectory.Size)
                || debugDirectory.Size < sizeof(IMAGE_DEBUG_DIRECTORY))
            {
                return false;
            }

            std::uint32_t entryCount = debugDirectory.Size / sizeof(IMAGE_DEBUG_DIRECTORY);
            if (entryCount > 64U)
            {
                return false;
            }

            for (std::uint32_t entryIndex = 0U; entryIndex < entryCount; entryIndex++)
            {
                std::uint32_t entryOffset = debugDirectory.VirtualAddress
                    + entryIndex * static_cast<std::uint32_t>(sizeof(IMAGE_DEBUG_DIRECTORY));
                std::uintptr_t entryAddress = 0U;
                if (!TryAddAddress(moduleBase, entryOffset, &entryAddress))
                {
                    return false;
                }

                IMAGE_DEBUG_DIRECTORY entry{};
                if (!TryReadLocalMemory(entryAddress, &entry, sizeof(entry)))
                {
                    return false;
                }

                if (entry.Type != IMAGE_DEBUG_TYPE_CODEVIEW
                    || entry.SizeOfData < sizeof(CodeViewHeader)
                    || !IsImageRangeValid(imageSize, entry.AddressOfRawData, sizeof(CodeViewHeader)))
                {
                    continue;
                }

                std::uintptr_t codeViewAddress = 0U;
                if (!TryAddAddress(moduleBase, entry.AddressOfRawData, &codeViewAddress))
                {
                    return false;
                }

                CodeViewHeader codeView{};
                if (!TryReadLocalMemory(codeViewAddress, &codeView, sizeof(codeView)))
                {
                    return false;
                }

                if (codeView.Signature == CODEVIEW_RSDS_SIGNATURE
                    && codeView.Age == expectedAge
                    && std::memcmp(&codeView.Guid, &expectedGuid, sizeof(GUID)) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        ImageValidationResult ValidateLoadedImage(
            HMODULE module,
            const NightLightBootstrapDescriptor& descriptor) noexcept
        {
            if (module == nullptr || descriptor.ImageSize == 0U || descriptor.PDBAge == 0U)
            {
                return ImageValidationResult::IdentityMismatch;
            }

            std::uintptr_t moduleBase = reinterpret_cast<std::uintptr_t>(module);
            IMAGE_DOS_HEADER dosHeader{};
            if (!TryReadLocalMemory(moduleBase, &dosHeader, sizeof(dosHeader))
                || dosHeader.e_magic != IMAGE_DOS_SIGNATURE
                || dosHeader.e_lfanew < static_cast<LONG>(sizeof(IMAGE_DOS_HEADER))
                || dosHeader.e_lfanew > 0x100000L)
            {
                return ImageValidationResult::IdentityMismatch;
            }

            std::uintptr_t NTHeadersAddress = 0U;
            if (!TryAddAddress(
                    moduleBase,
                    static_cast<std::uint32_t>(dosHeader.e_lfanew),
                    &NTHeadersAddress))
            {
                return ImageValidationResult::IdentityMismatch;
            }

            IMAGE_NT_HEADERS64 NTHeaders{};
            if (!TryReadLocalMemory(NTHeadersAddress, &NTHeaders, sizeof(NTHeaders))
                || NTHeaders.Signature != IMAGE_NT_SIGNATURE
                || NTHeaders.FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
                || NTHeaders.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC
                || NTHeaders.FileHeader.NumberOfSections == 0U
                || NTHeaders.FileHeader.NumberOfSections > NIGHT_LIGHT_MAX_IMAGE_SECTIONS
                || NTHeaders.FileHeader.SizeOfOptionalHeader < sizeof(IMAGE_OPTIONAL_HEADER64)
                || NTHeaders.OptionalHeader.SizeOfImage != descriptor.ImageSize
                || NTHeaders.OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_DEBUG)
            {
                return ImageValidationResult::IdentityMismatch;
            }

            std::uintptr_t sectionTableAddress = NTHeadersAddress
                + offsetof(IMAGE_NT_HEADERS64, OptionalHeader)
                + NTHeaders.FileHeader.SizeOfOptionalHeader;
            std::array<IMAGE_SECTION_HEADER, NIGHT_LIGHT_MAX_IMAGE_SECTIONS> sections{};
            SIZE_T sectionBytes = static_cast<SIZE_T>(NTHeaders.FileHeader.NumberOfSections)
                * sizeof(IMAGE_SECTION_HEADER);
            if (!TryReadLocalMemory(sectionTableAddress, sections.data(), sectionBytes))
            {
                return ImageValidationResult::IdentityMismatch;
            }

            const IMAGE_DATA_DIRECTORY& debugDirectory =
                NTHeaders.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
            if (!HasMatchingCodeViewIdentity(
                    moduleBase,
                    NTHeaders.OptionalHeader.SizeOfImage,
                    debugDirectory,
                    descriptor.PDBGuid,
                    descriptor.PDBAge))
            {
                return ImageValidationResult::IdentityMismatch;
            }

            const std::array<std::uint32_t, 4U> functionRVAs =
            {
                descriptor.InitializeRVA,
                descriptor.SetTemperatureRVA,
                descriptor.SetPreviewRVA,
                descriptor.SetActiveRVA
            };
            for (std::uint32_t functionRVA : functionRVAs)
            {
                if (!IsImageRangeValid(descriptor.ImageSize, functionRVA, 1U)
                    || !IsRVAInSection(
                        sections.data(),
                        NTHeaders.FileHeader.NumberOfSections,
                        functionRVA,
                        1U,
                        true))
                {
                    return ImageValidationResult::InvalidRVA;
                }
            }

            if (!IsImageRangeValid(
                    descriptor.ImageSize,
                    descriptor.SInstanceRVA,
                    SINGLETON_REQUIRED_BYTES)
                || !IsRVAInSection(
                    sections.data(),
                    NTHeaders.FileHeader.NumberOfSections,
                    descriptor.SInstanceRVA,
                    SINGLETON_REQUIRED_BYTES,
                    false))
            {
                return ImageValidationResult::InvalidRVA;
            }

            return ImageValidationResult::Valid;
        }

        bool IsReadablePointer(const void* pointer) noexcept
        {
            if (pointer == nullptr)
            {
                return false;
            }

            MEMORY_BASIC_INFORMATION memoryInformation{};
            SIZE_T queryResult = VirtualQuery(pointer, &memoryInformation, sizeof(memoryInformation));
            if (queryResult != sizeof(memoryInformation)
                || memoryInformation.State != MEM_COMMIT
                || (memoryInformation.Protect & PAGE_GUARD) != 0U)
            {
                return false;
            }

            DWORD protection = memoryInformation.Protect & 0xFFU;
            return protection != PAGE_NOACCESS && protection != 0U;
        }

        LONG NativeCallExceptionFilter(DWORD exceptionCode) noexcept
        {
            switch (exceptionCode)
            {
            case EXCEPTION_ACCESS_VIOLATION:
            case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
            case EXCEPTION_DATATYPE_MISALIGNMENT:
            case EXCEPTION_ILLEGAL_INSTRUCTION:
            case EXCEPTION_IN_PAGE_ERROR:
            case EXCEPTION_PRIV_INSTRUCTION:
                return EXCEPTION_EXECUTE_HANDLER;
            default:
                return EXCEPTION_CONTINUE_SEARCH;
            }
        }

        NIGHT_LIGHT_UNCHECKED_CALL bool TryCallInitialize(
            NightLightInitializeFunction function,
            void* singleton) noexcept
        {
            __try
            {
                function(singleton);
                return true;
            }
            __except (NativeCallExceptionFilter(GetExceptionCode()))
            {
                return false;
            }
        }

        NIGHT_LIGHT_UNCHECKED_CALL bool TryCallSetTemperature(
            NightLightSetTemperatureFunction function,
            void* singleton,
            int kelvin) noexcept
        {
            __try
            {
                function(singleton, kelvin);
                return true;
            }
            __except (NativeCallExceptionFilter(GetExceptionCode()))
            {
                return false;
            }
        }

        NIGHT_LIGHT_UNCHECKED_CALL bool TryCallSetPreview(
            NightLightSetPreviewFunction function,
            void* singleton,
            unsigned char previewEnabled) noexcept
        {
            __try
            {
                function(singleton, previewEnabled);
                return true;
            }
            __except (NativeCallExceptionFilter(GetExceptionCode()))
            {
                return false;
            }
        }

        NIGHT_LIGHT_UNCHECKED_CALL bool TryCallSetActive(
            NightLightSetActiveFunction function,
            void* singleton,
            unsigned char active) noexcept
        {
            __try
            {
                function(singleton, active);
                return true;
            }
            __except (NativeCallExceptionFilter(GetExceptionCode()))
            {
                return false;
            }
        }

        NIGHT_LIGHT_UNCHECKED_CALL bool TryReadSingletonStatePointers(
            void* singleton,
            void** stateInner,
            void** stateWrapper,
            void** settingsInner) noexcept
        {
            __try
            {
                const unsigned char* singletonBytes = static_cast<const unsigned char*>(singleton);
                *stateInner = *reinterpret_cast<void* const*>(singletonBytes + 264U);
                *stateWrapper = *reinterpret_cast<void* const*>(singletonBytes + 272U);
                *settingsInner = *reinterpret_cast<void* const*>(singletonBytes + 296U);
                return true;
            }
            __except (NativeCallExceptionFilter(GetExceptionCode()))
            {
                return false;
            }
        }

        bool MatchesBytes(
            const unsigned char* data,
            std::size_t dataLength,
            std::size_t offset,
            const unsigned char* expected,
            std::size_t expectedLength) noexcept
        {
            if (data == nullptr || expected == nullptr || offset > dataLength
                || expectedLength > dataLength - offset)
            {
                return false;
            }

            return std::memcmp(data + offset, expected, expectedLength) == 0;
        }

        bool TryInspectStateBlob(
            const unsigned char* blob,
            std::size_t blobLength,
            NightLightStateStatus* status) noexcept
        {
            constexpr std::array<unsigned char, 4U> OUTER_MAGIC = { 0x43U, 0x42U, 0x01U, 0x00U };
            constexpr std::array<unsigned char, 6U> OUTER_HEADER =
                { 0x0AU, 0x02U, 0x01U, 0x00U, 0x2AU, 0x06U };
            constexpr std::array<unsigned char, 3U> INNER_PREFIX = { 0x2AU, 0x2BU, 0x0EU };
            constexpr std::array<unsigned char, 2U> ENABLED_MARKER = { 0x10U, 0x00U };
            constexpr std::array<unsigned char, 2U> INITIALIZED_TAG = { 0xD0U, 0x0AU };

            if (status == nullptr)
            {
                return false;
            }

            status->IsInitialized = false;
            status->IsEnabled = false;
            if (blob == nullptr || blobLength < 20U
                || !MatchesBytes(blob, blobLength, 0U, OUTER_MAGIC.data(), OUTER_MAGIC.size())
                || !MatchesBytes(blob, blobLength, 4U, OUTER_HEADER.data(), OUTER_HEADER.size()))
            {
                return false;
            }

            constexpr std::size_t TIMESTAMP_START = 10U;
            std::size_t timestampLength = 0U;
            for (std::size_t byteIndex = 0U; byteIndex < 10U; byteIndex++)
            {
                std::size_t position = TIMESTAMP_START + byteIndex;
                if (position >= blobLength)
                {
                    return false;
                }

                if ((blob[position] & 0x80U) == 0U)
                {
                    timestampLength = byteIndex + 1U;
                    break;
                }
            }

            if (timestampLength == 0U)
            {
                return false;
            }

            std::size_t positionAfterTimestamp = TIMESTAMP_START + timestampLength;
            if (!MatchesBytes(
                    blob,
                    blobLength,
                    positionAfterTimestamp,
                    INNER_PREFIX.data(),
                    INNER_PREFIX.size()))
            {
                return false;
            }

            std::size_t innerLengthPosition = positionAfterTimestamp + INNER_PREFIX.size();
            if (innerLengthPosition >= blobLength)
            {
                return false;
            }

            std::size_t innerLength = blob[innerLengthPosition];
            std::size_t innerStart = innerLengthPosition + 1U;
            if (innerLength < OUTER_MAGIC.size()
                || innerLength > blobLength - innerStart
                || !MatchesBytes(blob, blobLength, innerStart, OUTER_MAGIC.data(), OUTER_MAGIC.size()))
            {
                return false;
            }

            std::size_t innerEnd = innerStart + innerLength;
            std::size_t tagStart = innerStart + OUTER_MAGIC.size();
            bool enabled = tagStart + ENABLED_MARKER.size() <= innerEnd
                && MatchesBytes(blob, innerEnd, tagStart, ENABLED_MARKER.data(), ENABLED_MARKER.size());
            if (enabled)
            {
                tagStart += ENABLED_MARKER.size();
            }

            std::size_t valueStart = tagStart + INITIALIZED_TAG.size();
            bool initialized = valueStart < innerEnd
                && MatchesBytes(blob, innerEnd, tagStart, INITIALIZED_TAG.data(), INITIALIZED_TAG.size())
                && blob[valueStart] == 0x02U;
            status->IsInitialized = initialized;
            status->IsEnabled = initialized && enabled;
            return true;
        }

        bool ReadNightLightState(NightLightStateStatus* status) noexcept
        {
            if (status == nullptr)
            {
                return false;
            }

            status->IsInitialized = false;
            status->IsEnabled = false;
            HKEY key = nullptr;
            LSTATUS openResult = RegOpenKeyExW(
                HKEY_CURRENT_USER,
                STATE_BLOB_KEY_PATH,
                0,
                KEY_QUERY_VALUE,
                &key);
            if (openResult != ERROR_SUCCESS)
            {
                return false;
            }

            std::array<unsigned char, NIGHT_LIGHT_MAX_REGISTRY_BLOB_BYTES> blob{};
            DWORD blobLength = static_cast<DWORD>(blob.size());
            DWORD valueType = REG_NONE;
            LSTATUS readResult = RegQueryValueExW(
                key,
                CLOUD_STORE_DATA_VALUE_NAME,
                nullptr,
                &valueType,
                blob.data(),
                &blobLength);
            RegCloseKey(key);
            if (readResult != ERROR_SUCCESS || valueType != REG_BINARY)
            {
                return false;
            }

            return TryInspectStateBlob(blob.data(), blobLength, status);
        }

        bool IsNightLightEnabled() noexcept
        {
            NightLightStateStatus status{};
            return ReadNightLightState(&status) && status.IsEnabled;
        }

        int PercentToKelvin(int percent) noexcept
        {
            return MAXIMUM_KELVIN - percent * (MAXIMUM_KELVIN - MINIMUM_KELVIN) / 100;
        }
    }

    NightLightBackend::NightLightBackend() noexcept :
        _stateLock{},
        _descriptor{},
        _thread(nullptr),
        _workEvent(nullptr),
        _startedEvent(nullptr),
        _requestCompletedEvent(nullptr),
        _startError(NightLightBackendStartError::None),
        _healthy(false),
        _stopRequested(false),
        _hasPendingKelvin(false),
        _pendingKelvin(0),
        _lastStreamingRequestTick(0U),
        _synchronousRequest(SynchronousRequestKind::None),
        _requestEnabled(false),
        _requestHasEnableStrength(false),
        _requestEnableStrength(0),
        _requestResult(false),
        _settingsHandlersModule(nullptr),
        _singleton(nullptr),
        _initialize(nullptr),
        _setTemperature(nullptr),
        _setPreview(nullptr),
        _setActive(nullptr),
        _previewActive(false),
        _winRTInitialized(false)
    {
        InitializeSRWLock(&_stateLock);
    }

    NightLightBackend::~NightLightBackend() noexcept
    {
        if (!Shutdown())
        {
            // The object owns synchronization state used by the thread. Do not unwind it underneath a hung
            // undocumented Windows call; the managed parent will replace this helper process.
            (void)TerminateProcess(GetCurrentProcess(), ERROR_TIMEOUT);
        }
    }

    bool NightLightBackend::Start(const NightLightBootstrapDescriptor& descriptor) noexcept
    {
        if (_thread != nullptr)
        {
            return false;
        }

        _descriptor = descriptor;
        _workEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        _startedEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        _requestCompletedEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (_workEvent == nullptr || _startedEvent == nullptr || _requestCompletedEvent == nullptr)
        {
            _startError = NightLightBackendStartError::ThreadResources;
            return false;
        }

        _thread = CreateThread(nullptr, 0U, ThreadEntry, this, 0U, nullptr);
        if (_thread == nullptr)
        {
            _startError = NightLightBackendStartError::ThreadStart;
            return false;
        }

        DWORD waitResult = WaitForSingleObject(_startedEvent, NIGHT_LIGHT_BACKEND_START_TIMEOUT_MS);
        if (waitResult != WAIT_OBJECT_0)
        {
            {
                SRWExclusiveLockGuard lock(&_stateLock);
                _startError = NightLightBackendStartError::ThreadStartTimeout;
                _stopRequested = true;
            }

            SetEvent(_workEvent);
            return false;
        }

        SRWExclusiveLockGuard lock(&_stateLock);
        return _healthy;
    }

    NightLightBackendStartError NightLightBackend::GetStartError() const noexcept
    {
        SRWExclusiveLockGuard lock(&_stateLock);
        return _startError;
    }

    bool NightLightBackend::QueueStrengthPercent(int percent) noexcept
    {
        if (percent < 0 || percent > 100 || !IsNightLightEnabled())
        {
            return false;
        }

        {
            SRWExclusiveLockGuard lock(&_stateLock);
            if (!_healthy || _stopRequested)
            {
                return false;
            }

            _pendingKelvin = PercentToKelvin(percent);
            _hasPendingKelvin = true;
            _lastStreamingRequestTick = GetTickCount64();
        }

        return SetEvent(_workEvent) != FALSE;
    }

    bool NightLightBackend::Drain() noexcept
    {
        return SubmitSynchronousRequest(
            SynchronousRequestKind::Drain,
            false,
            false,
            0);
    }

    bool NightLightBackend::SetActive(
        bool enabled,
        bool hasEnableStrength,
        int enableStrength) noexcept
    {
        if ((!enabled && hasEnableStrength)
            || (hasEnableStrength && (enableStrength < 0 || enableStrength > 100)))
        {
            return false;
        }

        return SubmitSynchronousRequest(
            SynchronousRequestKind::SetActive,
            enabled,
            hasEnableStrength,
            enableStrength);
    }

    bool NightLightBackend::Shutdown() noexcept
    {
        if (_thread == nullptr)
        {
            if (_workEvent != nullptr)
            {
                CloseHandle(_workEvent);
                _workEvent = nullptr;
            }

            if (_startedEvent != nullptr)
            {
                CloseHandle(_startedEvent);
                _startedEvent = nullptr;
            }

            if (_requestCompletedEvent != nullptr)
            {
                CloseHandle(_requestCompletedEvent);
                _requestCompletedEvent = nullptr;
            }

            return true;
        }

        {
            SRWExclusiveLockGuard lock(&_stateLock);
            _stopRequested = true;
        }

        SetEvent(_workEvent);
        DWORD waitResult = WaitForSingleObject(_thread, NIGHT_LIGHT_BACKEND_SHUTDOWN_TIMEOUT_MS);
        if (waitResult != WAIT_OBJECT_0)
        {
            return false;
        }

        CloseHandle(_thread);
        _thread = nullptr;
        CloseHandle(_workEvent);
        _workEvent = nullptr;
        CloseHandle(_startedEvent);
        _startedEvent = nullptr;
        CloseHandle(_requestCompletedEvent);
        _requestCompletedEvent = nullptr;
        return true;
    }

    DWORD WINAPI NightLightBackend::ThreadEntry(void* parameter) noexcept
    {
        if (parameter == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        NightLightBackend* backend = static_cast<NightLightBackend*>(parameter);
        return backend->ThreadMain();
    }

    DWORD NightLightBackend::ThreadMain() noexcept
    {
        NightLightBackendStartError startError = InitializeOnMTAThread();
        RecordStartResult(startError);
        if (startError != NightLightBackendStartError::None)
        {
            if (_winRTInitialized)
            {
                RoUninitialize();
                _winRTInitialized = false;
            }

            return ERROR_NOT_SUPPORTED;
        }

        bool keepRunning = true;
        while (keepRunning)
        {
            WorkItem work = TakeWork();
            switch (work.Kind)
            {
            case WorkKind::None:
                (void)WaitForSingleObject(_workEvent, work.WaitTimeoutMilliseconds);
                break;

            case WorkKind::StreamingKelvin:
                if (!ProcessStreamingKelvin(work.Kelvin))
                {
                    MarkUnhealthy();
                    keepRunning = false;
                }
                break;

            case WorkKind::ReleasePreview:
                if (_previewActive && !TryCallSetPreview(_setPreview, _singleton, 0U))
                {
                    MarkUnhealthy();
                    keepRunning = false;
                    break;
                }

                _previewActive = false;
                break;

            case WorkKind::Drain:
            {
                bool drainResult = DrainStreamingOnMTAThread();
                CompleteSynchronousRequest(drainResult);
                if (!drainResult)
                {
                    MarkUnhealthy();
                    keepRunning = false;
                }
                break;
            }

            case WorkKind::SetActive:
            {
                bool activeResult = DrainStreamingOnMTAThread()
                    && SetActiveOnMTAThread(
                        work.Enabled,
                        work.HasEnableStrength,
                        work.EnableStrength);
                CompleteSynchronousRequest(activeResult);
                break;
            }

            case WorkKind::Stop:
                (void)DrainStreamingOnMTAThread();
                keepRunning = false;
                break;
            }
        }

        MarkUnhealthy();
        if (_winRTInitialized)
        {
            RoUninitialize();
            _winRTInitialized = false;
        }

        return ERROR_SUCCESS;
    }

    NightLightBackendStartError NightLightBackend::InitializeOnMTAThread() noexcept
    {
        HRESULT initializeResult = RoInitialize(RO_INIT_MULTITHREADED);
        if (FAILED(initializeResult))
        {
            return NightLightBackendStartError::WinRTInitialization;
        }

        _winRTInitialized = true;
        _settingsHandlersModule = LoadLibraryExW(
            SETTINGS_HANDLERS_DLL_NAME,
            nullptr,
            LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (_settingsHandlersModule == nullptr)
        {
            return NightLightBackendStartError::LoadLibrary;
        }

        ImageValidationResult validationResult = ValidateLoadedImage(
            _settingsHandlersModule,
            _descriptor);
        if (validationResult == ImageValidationResult::IdentityMismatch)
        {
            return NightLightBackendStartError::ImageIdentity;
        }

        if (validationResult == ImageValidationResult::InvalidRVA)
        {
            return NightLightBackendStartError::InvalidRVA;
        }

        std::uintptr_t moduleBase = reinterpret_cast<std::uintptr_t>(_settingsHandlersModule);
        _singleton = reinterpret_cast<void*>(moduleBase + _descriptor.SInstanceRVA);
        _initialize = reinterpret_cast<NightLightInitializeFunction>(moduleBase + _descriptor.InitializeRVA);
        _setTemperature = reinterpret_cast<NightLightSetTemperatureFunction>(
            moduleBase + _descriptor.SetTemperatureRVA);
        _setPreview = reinterpret_cast<NightLightSetPreviewFunction>(moduleBase + _descriptor.SetPreviewRVA);
        _setActive = reinterpret_cast<NightLightSetActiveFunction>(moduleBase + _descriptor.SetActiveRVA);

        if (!TryCallInitialize(_initialize, _singleton))
        {
            return NightLightBackendStartError::SingletonInitialization;
        }

        void* stateInner = nullptr;
        void* stateWrapper = nullptr;
        void* settingsInner = nullptr;
        if (!TryReadSingletonStatePointers(
                _singleton,
                &stateInner,
                &stateWrapper,
                &settingsInner)
            || !IsReadablePointer(stateInner)
            || !IsReadablePointer(stateWrapper)
            || !IsReadablePointer(settingsInner))
        {
            return NightLightBackendStartError::SingletonState;
        }

        return NightLightBackendStartError::None;
    }

    NightLightBackend::WorkItem NightLightBackend::TakeWork() noexcept
    {
        WorkItem work{};
        work.Kind = WorkKind::None;
        work.WaitTimeoutMilliseconds = INFINITE;

        SRWExclusiveLockGuard lock(&_stateLock);
        if (_stopRequested)
        {
            work.Kind = WorkKind::Stop;
            return work;
        }

        if (_synchronousRequest == SynchronousRequestKind::Drain)
        {
            work.Kind = WorkKind::Drain;
            return work;
        }

        if (_synchronousRequest == SynchronousRequestKind::SetActive)
        {
            work.Kind = WorkKind::SetActive;
            work.Enabled = _requestEnabled;
            work.HasEnableStrength = _requestHasEnableStrength;
            work.EnableStrength = _requestEnableStrength;
            return work;
        }

        if (_hasPendingKelvin)
        {
            work.Kind = WorkKind::StreamingKelvin;
            work.Kelvin = _pendingKelvin;
            _hasPendingKelvin = false;
            return work;
        }

        if (_previewActive)
        {
            ULONGLONG elapsedMilliseconds = GetTickCount64() - _lastStreamingRequestTick;
            if (elapsedMilliseconds >= NIGHT_LIGHT_PREVIEW_RELEASE_DELAY_MS)
            {
                work.Kind = WorkKind::ReleasePreview;
                return work;
            }

            work.WaitTimeoutMilliseconds = static_cast<DWORD>(
                NIGHT_LIGHT_PREVIEW_RELEASE_DELAY_MS - elapsedMilliseconds);
        }

        return work;
    }

    bool NightLightBackend::ProcessStreamingKelvin(int kelvin) noexcept
    {
        // Recheck at the last boundary because the main process may disable Night Light after SET is accepted.
        if (!IsNightLightEnabled())
        {
            return true;
        }

        if (!TryCallSetTemperature(_setTemperature, _singleton, kelvin))
        {
            return false;
        }

        if (!_previewActive)
        {
            if (!TryCallSetPreview(_setPreview, _singleton, 1U))
            {
                return false;
            }

            _previewActive = true;
        }

        return true;
    }

    bool NightLightBackend::DrainStreamingOnMTAThread() noexcept
    {
        while (true)
        {
            int pendingKelvin = 0;
            {
                SRWExclusiveLockGuard lock(&_stateLock);
                if (!_hasPendingKelvin)
                {
                    break;
                }

                pendingKelvin = _pendingKelvin;
                _hasPendingKelvin = false;
            }

            if (!ProcessStreamingKelvin(pendingKelvin))
            {
                return false;
            }
        }

        if (_previewActive)
        {
            if (!TryCallSetPreview(_setPreview, _singleton, 0U))
            {
                return false;
            }

            _previewActive = false;
        }

        return true;
    }

    bool NightLightBackend::SaveSettingsKelvinOnMTAThread(int kelvin) noexcept
    {
        RegistryNotification temperatureNotification(SETTINGS_BLOB_KEY_PATH);
        if (!TryCallSetTemperature(_setTemperature, _singleton, kelvin))
        {
            return false;
        }
        temperatureNotification.WaitForSave();

        RegistryNotification previewOnNotification(SETTINGS_BLOB_KEY_PATH);
        if (!TryCallSetPreview(_setPreview, _singleton, 1U))
        {
            return false;
        }
        previewOnNotification.WaitForSave();

        RegistryNotification previewOffNotification(SETTINGS_BLOB_KEY_PATH);
        if (!TryCallSetPreview(_setPreview, _singleton, 0U))
        {
            return false;
        }
        previewOffNotification.WaitForSave();
        return true;
    }

    bool NightLightBackend::SetActiveOnMTAThread(
        bool enabled,
        bool hasEnableStrength,
        int enableStrength) noexcept
    {
        if (enabled && hasEnableStrength)
        {
            int kelvin = PercentToKelvin(enableStrength);
            if (!SaveSettingsKelvinOnMTAThread(kelvin))
            {
                return false;
            }
        }

        ULONGLONG startedAtTick = GetTickCount64();
        RegistryNotification stateNotification(STATE_BLOB_KEY_PATH);
        if (!TryCallSetActive(_setActive, _singleton, enabled ? 1U : 0U))
        {
            return false;
        }
        stateNotification.WaitForSave();

        while (GetTickCount64() - startedAtTick <= NIGHT_LIGHT_SAVE_NOTIFY_TIMEOUT_MS)
        {
            NightLightStateStatus status{};
            if (ReadNightLightState(&status)
                && status.IsInitialized
                && status.IsEnabled == enabled)
            {
                return true;
            }

            Sleep(NIGHT_LIGHT_STATE_READBACK_POLL_MS);
        }

        NightLightStateStatus finalStatus{};
        return ReadNightLightState(&finalStatus)
            && finalStatus.IsInitialized
            && finalStatus.IsEnabled == enabled;
    }

    bool NightLightBackend::SubmitSynchronousRequest(
        SynchronousRequestKind kind,
        bool enabled,
        bool hasEnableStrength,
        int enableStrength) noexcept
    {
        if (_thread == nullptr || ResetEvent(_requestCompletedEvent) == FALSE)
        {
            return false;
        }

        {
            SRWExclusiveLockGuard lock(&_stateLock);
            if (!_healthy || _stopRequested || _synchronousRequest != SynchronousRequestKind::None)
            {
                return false;
            }

            _synchronousRequest = kind;
            _requestEnabled = enabled;
            _requestHasEnableStrength = hasEnableStrength;
            _requestEnableStrength = enableStrength;
            _requestResult = false;
        }

        if (SetEvent(_workEvent) == FALSE)
        {
            MarkUnhealthy();
            return false;
        }

        const std::array<HANDLE, 2U> waitHandles = { _requestCompletedEvent, _thread };
        DWORD waitResult = WaitForMultipleObjects(
            static_cast<DWORD>(waitHandles.size()),
            waitHandles.data(),
            FALSE,
            INFINITE);
        if (waitResult != WAIT_OBJECT_0)
        {
            return false;
        }

        SRWExclusiveLockGuard lock(&_stateLock);
        return _requestResult;
    }

    void NightLightBackend::CompleteSynchronousRequest(bool succeeded) noexcept
    {
        {
            SRWExclusiveLockGuard lock(&_stateLock);
            _requestResult = succeeded;
            _synchronousRequest = SynchronousRequestKind::None;
        }

        SetEvent(_requestCompletedEvent);
    }

    void NightLightBackend::RecordStartResult(NightLightBackendStartError error) noexcept
    {
        {
            SRWExclusiveLockGuard lock(&_stateLock);
            if (_startError != NightLightBackendStartError::ThreadStartTimeout)
            {
                _startError = error;
            }
            _healthy = error == NightLightBackendStartError::None && !_stopRequested;
        }

        SetEvent(_startedEvent);
    }

    void NightLightBackend::MarkUnhealthy() noexcept
    {
        bool completeRequest = false;
        {
            SRWExclusiveLockGuard lock(&_stateLock);
            _healthy = false;
            if (_synchronousRequest != SynchronousRequestKind::None)
            {
                _requestResult = false;
                _synchronousRequest = SynchronousRequestKind::None;
                completeRequest = true;
            }
        }

        if (completeRequest)
        {
            SetEvent(_requestCompletedEvent);
        }
    }

    const char* GetNightLightBackendStartErrorToken(NightLightBackendStartError error) noexcept
    {
        switch (error)
        {
        case NightLightBackendStartError::None:
            return "none";
        case NightLightBackendStartError::ThreadResources:
            return "thread-resources";
        case NightLightBackendStartError::ThreadStart:
            return "thread-start";
        case NightLightBackendStartError::ThreadStartTimeout:
            return "thread-timeout";
        case NightLightBackendStartError::WinRTInitialization:
            return "winrt-init";
        case NightLightBackendStartError::LoadLibrary:
            return "load-library";
        case NightLightBackendStartError::ImageIdentity:
            return "image-identity";
        case NightLightBackendStartError::InvalidRVA:
            return "invalid-rva";
        case NightLightBackendStartError::SingletonInitialization:
            return "singleton-init";
        case NightLightBackendStartError::SingletonState:
            return "singleton-state";
        default:
            return "unknown";
        }
    }
}
