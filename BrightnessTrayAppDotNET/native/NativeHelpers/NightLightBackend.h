#pragma once

#include <Windows.h>

#include <cstdint>

namespace BrightnessTrayAppDotNET::NativeHelpers
{
    /// Identifies one exact SettingsHandlers_Display image and its required symbols.
    struct NightLightBootstrapDescriptor
    {
        GUID PDBGuid;
        std::uint32_t PDBAge;
        std::uint32_t ImageSize;
        std::uint32_t InitializeRVA;
        std::uint32_t SInstanceRVA;
        std::uint32_t SetTemperatureRVA;
        std::uint32_t SetPreviewRVA;
        std::uint32_t SetActiveRVA;
    };

    enum class NightLightBackendStartError : std::uint32_t
    {
        None,
        ThreadResources,
        ThreadStart,
        ThreadStartTimeout,
        WinRTInitialization,
        LoadLibrary,
        ImageIdentity,
        InvalidRVA,
        SingletonInitialization,
        SingletonState
    };

    using NightLightInitializeFunction = void(__fastcall*)(void* singleton);
    using NightLightSetTemperatureFunction = void(__fastcall*)(void* singleton, int kelvin);
    using NightLightSetPreviewFunction = void(__fastcall*)(void* singleton, unsigned char previewEnabled);
    using NightLightSetActiveFunction = void(__fastcall*)(void* singleton, unsigned char active);

    /// Owns the permanent MTA thread used for every SettingsHandlers call.
    class NightLightBackend final
    {
    public:
        NightLightBackend() noexcept;
        NightLightBackend(const NightLightBackend&) = delete;
        NightLightBackend& operator=(const NightLightBackend&) = delete;
        ~NightLightBackend() noexcept;

        /// Starts the MTA thread and waits until the native singleton is ready.
        bool Start(const NightLightBootstrapDescriptor& descriptor) noexcept;

        /// Returns the startup failure recorded by the MTA thread.
        NightLightBackendStartError GetStartError() const noexcept;

        /// Returns whether the MTA thread can still accept backend work.
        bool IsHealthy() const noexcept;

        /// Queues one latest-wins strength update in the inclusive range 0 through 100.
        bool QueueStrengthPercent(int percent) noexcept;

        /// Flushes queued strength work and releases preview mode.
        bool Drain() noexcept;

        /// Changes active state, optionally committing strength before an enable.
        bool SetActive(bool enabled, bool hasEnableStrength, int enableStrength) noexcept;

        /// Stops and joins the MTA thread. Returns false if the thread cannot be joined.
        bool Shutdown() noexcept;

    private:
        enum class NativeOperationResult : std::uint32_t
        {
            Succeeded,
            Failed,
            Fatal
        };

        enum class SynchronousRequestKind : std::uint32_t
        {
            None,
            Drain,
            SetActive
        };

        enum class WorkKind : std::uint32_t
        {
            None,
            StreamingKelvin,
            ReleasePreview,
            Drain,
            SetActive,
            Stop
        };

        struct WorkItem
        {
            WorkKind Kind;
            int Kelvin;
            bool Enabled;
            bool HasEnableStrength;
            int EnableStrength;
            DWORD WaitTimeoutMilliseconds;
        };

        static DWORD WINAPI ThreadEntry(void* parameter) noexcept;
        DWORD ThreadMain() noexcept;
        NightLightBackendStartError InitializeOnMTAThread() noexcept;
        WorkItem TakeWork() noexcept;
        bool ProcessStreamingKelvin(int kelvin) noexcept;
        bool DrainStreamingOnMTAThread() noexcept;
        NativeOperationResult SaveSettingsKelvinOnMTAThread(int kelvin) noexcept;
        NativeOperationResult SetActiveOnMTAThread(
            bool enabled,
            bool hasEnableStrength,
            int enableStrength) noexcept;
        bool SubmitSynchronousRequest(
            SynchronousRequestKind kind,
            bool enabled,
            bool hasEnableStrength,
            int enableStrength) noexcept;
        void CompleteSynchronousRequest(bool succeeded) noexcept;
        void RecordStartResult(NightLightBackendStartError error) noexcept;
        void MarkUnhealthy() noexcept;

        mutable SRWLOCK _stateLock;
        NightLightBootstrapDescriptor _descriptor;
        HANDLE _thread;
        HANDLE _workEvent;
        HANDLE _startedEvent;
        HANDLE _requestCompletedEvent;
        NightLightBackendStartError _startError;
        bool _healthy;
        bool _stopRequested;
        bool _hasPendingKelvin;
        int _pendingKelvin;
        ULONGLONG _lastStreamingRequestTick;
        SynchronousRequestKind _synchronousRequest;
        bool _requestEnabled;
        bool _requestHasEnableStrength;
        int _requestEnableStrength;
        bool _requestResult;

        HMODULE _settingsHandlersModule;
        void* _singleton;
        NightLightInitializeFunction _initialize;
        NightLightSetTemperatureFunction _setTemperature;
        NightLightSetPreviewFunction _setPreview;
        NightLightSetActiveFunction _setActive;
        bool _previewActive;
        bool _winRTInitialized;
    };

    /// Returns a stable ASCII token for an initialization failure.
    const char* GetNightLightBackendStartErrorToken(NightLightBackendStartError error) noexcept;
}
