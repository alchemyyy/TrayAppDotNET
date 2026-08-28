namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Combines cached adapter metadata with current GPU temperature and engine samples.</summary>
internal sealed class GPUPerformanceDetailsReader
{
    private const int MaximumDetailEngineCount = 4;
    private const int MetadataFailureRetrySeconds = 30;
    private const int MetadataRefreshMinutes = 5;
    private const long MillisecondsPerSecond = 1_000;
    private const long SecondsPerMinute = 60;

    private const long MetadataFailureRetryMilliseconds =
        MetadataFailureRetrySeconds * MillisecondsPerSecond;
    private const long MetadataRefreshIntervalMilliseconds =
        MetadataRefreshMinutes * SecondsPerMinute * MillisecondsPerSecond;

    private static readonly string[] PreferredEngineNames =
    [
        "3D",
        "Copy",
        "Video Encode",
        "Video Decode",
        "Compute",
        "Video Processing",
        "Optical Flow"
    ];

    private readonly Dictionary<GPUAdapterKey, CachedGPUAdapterMetadata> _metadata = [];

    /// <summary>Samples optional detail values for one existing GPU performance snapshot.</summary>
    public GPUPerformanceDetailsSnapshot Sample(
        GPUPerformanceSnapshot snapshot,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        GPUAdapterKey key = new(snapshot.AdapterLUID, snapshot.PhysicalAdapterIndex);
        long currentTick = Environment.TickCount64;
        bool hasCachedMetadata = _metadata.TryGetValue(
            key,
            out CachedGPUAdapterMetadata? cachedMetadata);
        if (!hasCachedMetadata || currentTick >= cachedMetadata!.NextRefreshTick)
        {
            GPUAdapterHardwareMetadata hardware = GPUAdapterNativeDetailsReader.ReadMetadata(
                key,
                snapshot.DedicatedMemoryCapacityBytes,
                out string? metadataError);
            bool hasMetadataError = !string.IsNullOrWhiteSpace(metadataError);
            bool preserveCachedMetadata = hasMetadataError
                                          && cachedMetadata?.Metadata.HasMetadata == true;
            GPUAdapterHardwareMetadata effectiveHardware = SelectMetadataAfterRefresh(
                cachedMetadata?.Metadata ?? GPUAdapterHardwareMetadata.Empty,
                hardware,
                hasMetadataError);
            cachedMetadata = new CachedGPUAdapterMetadata(
                effectiveHardware,
                metadataError,
                CalculateNextMetadataRefreshTick(
                    currentTick,
                    hasMetadataError),
                preserveCachedMetadata ? cachedMetadata!.EngineSlots : null);
            _metadata[key] = cachedMetadata;
        }

        GPUAdapterHardwareMetadata metadata = cachedMetadata.Metadata;
        cachedMetadata.EnsureEngineSlots(
            snapshot.Engines.Span,
            snapshot.HasUtilizationSample);
        GPUPerformanceDetailEngineSnapshot[] engines = CreateEngineSamples(
            cachedMetadata.EngineSlots,
            snapshot.Engines.Span,
            snapshot.HasUtilizationSample);
        bool hasTemperatureData = GPUAdapterNativeDetailsReader.TryReadTemperature(
            key,
            out double temperatureCelsius);
        error = cachedMetadata.Error;

        bool hasDetailData = metadata.HasMetadata
                             || engines.Length > 0
                             || hasTemperatureData;
        return new GPUPerformanceDetailsSnapshot(
            hasDetailData,
            engines,
            hasTemperatureData,
            temperatureCelsius,
            metadata.DriverVersion,
            metadata.DriverDate,
            metadata.DirectXVersion,
            metadata.FeatureLevel,
            metadata.PhysicalLocation,
            metadata.HasHardwareReservedMemoryData,
            metadata.HardwareReservedMemoryBytes);
    }

    /// <summary>Removes cached metadata after display topology or driver changes.</summary>
    public void Clear() => _metadata.Clear();

    /// <summary>Schedules quick failure retries and periodic successful metadata refreshes.</summary>
    internal static long CalculateNextMetadataRefreshTick(long currentTick, bool hasError)
    {
        long delay = hasError
            ? MetadataFailureRetryMilliseconds
            : MetadataRefreshIntervalMilliseconds;
        return currentTick <= long.MaxValue - delay
            ? currentTick + delay
            : long.MaxValue;
    }

    /// <summary>Retains valid cached fields when a periodic metadata refresh fails.</summary>
    internal static GPUAdapterHardwareMetadata SelectMetadataAfterRefresh(
        GPUAdapterHardwareMetadata previousMetadata,
        GPUAdapterHardwareMetadata refreshedMetadata,
        bool hasError) =>
        hasError && previousMetadata.HasMetadata
            ? previousMetadata
            : refreshedMetadata;

    /// <summary>Selects deterministic Task Manager-style graph lanes from a GPU engine catalog.</summary>
    internal static GPUPerformanceDetailEngineSnapshot[] SelectEngineSlots(
        ReadOnlySpan<GPUAdapterEngineIdentity> catalog,
        ReadOnlySpan<GPUPerformanceEngineSnapshot> liveEngines,
        bool hasUtilizationSample)
    {
        Dictionary<int, GPUAdapterEngineIdentity> identitiesByIndex = [];
        for (int catalogIndex = 0; catalogIndex < catalog.Length; catalogIndex++)
        {
            GPUAdapterEngineIdentity identity = catalog[catalogIndex];
            if (identity.EngineIndex < 0) continue;

            string normalizedName = NormalizeEngineName(identity.Name);
            identitiesByIndex.TryAdd(
                identity.EngineIndex,
                new GPUAdapterEngineIdentity(identity.EngineIndex, normalizedName));
        }

        Dictionary<int, GPUPerformanceEngineSnapshot> liveByIndex = [];
        for (int engineIndex = 0; engineIndex < liveEngines.Length; engineIndex++)
        {
            GPUPerformanceEngineSnapshot engine = liveEngines[engineIndex];
            if (engine.EngineIndex < 0) continue;

            liveByIndex[engine.EngineIndex] = engine;
            if (!identitiesByIndex.ContainsKey(engine.EngineIndex))
            {
                identitiesByIndex.Add(
                    engine.EngineIndex,
                    new GPUAdapterEngineIdentity(
                        engine.EngineIndex,
                        NormalizeEngineName(engine.Name)));
            }
        }

        List<GPUAdapterEngineIdentity> candidates = new(identitiesByIndex.Count);
        foreach (GPUAdapterEngineIdentity identity in identitiesByIndex.Values)
            candidates.Add(identity);
        candidates.Sort(static (left, right) => left.EngineIndex.CompareTo(right.EngineIndex));

        List<GPUAdapterEngineIdentity> selected = new(MaximumDetailEngineCount);
        HashSet<int> selectedIndexes = [];
        HashSet<string> selectedNames = new(StringComparer.OrdinalIgnoreCase);
        for (int preferredIndex = 0;
             preferredIndex < PreferredEngineNames.Length
             && selected.Count < MaximumDetailEngineCount;
             preferredIndex++)
        {
            string preferredName = PreferredEngineNames[preferredIndex];
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                GPUAdapterEngineIdentity candidate = candidates[candidateIndex];
                if (!candidate.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!selectedIndexes.Add(candidate.EngineIndex)) continue;

                selected.Add(candidate);
                selectedNames.Add(candidate.Name);
                break;
            }
        }

        for (int candidateIndex = 0;
             candidateIndex < candidates.Count && selected.Count < MaximumDetailEngineCount;
             candidateIndex++)
        {
            GPUAdapterEngineIdentity candidate = candidates[candidateIndex];
            if (!selectedIndexes.Add(candidate.EngineIndex)) continue;
            if (!selectedNames.Add(candidate.Name)) continue;
            selected.Add(candidate);
        }

        GPUPerformanceDetailEngineSnapshot[] result =
            new GPUPerformanceDetailEngineSnapshot[selected.Count];
        for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
        {
            GPUAdapterEngineIdentity identity = selected[selectedIndex];
            bool hasLiveEngine = liveByIndex.TryGetValue(
                identity.EngineIndex,
                out GPUPerformanceEngineSnapshot liveEngine);
            double utilizationPercent = hasLiveEngine
                                        && double.IsFinite(liveEngine.UtilizationPercent)
                ? Math.Clamp(liveEngine.UtilizationPercent, 0, 100)
                : 0;
            result[selectedIndex] = new GPUPerformanceDetailEngineSnapshot(
                identity.EngineIndex,
                identity.Name,
                hasUtilizationSample,
                utilizationPercent);
        }

        return result;
    }

    /// <summary>Normalizes kernel and PDH spellings to stable user-facing engine names.</summary>
    internal static string NormalizeEngineName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "GPU Engine";

        ReadOnlySpan<char> value = name.AsSpan().Trim();
        int duplicateSuffixIndex = value.IndexOf('#');
        if (duplicateSuffixIndex >= 0)
            value = value[..duplicateSuffixIndex];

        string compactName = string.Concat(value.ToString().Where(
            static character => character is not ' ' and not '-' and not '_'));
        return compactName.ToLowerInvariant() switch
        {
            "3d" => "3D",
            "copy" => "Copy",
            "compute" => "Compute",
            "videodecode" => "Video Decode",
            "videoencode" => "Video Encode",
            "videoprocessing" => "Video Processing",
            "sceneassembly" => "Scene Assembly",
            "opticalflow" => "Optical Flow",
            "overlay" => "Overlay",
            "crypto" => "Crypto",
            "videocodec" => "Video Codec",
            _ => value.ToString()
        };
    }

    private static GPUPerformanceDetailEngineSnapshot[] CreateEngineSamples(
        ReadOnlySpan<GPUAdapterEngineIdentity> engineSlots,
        ReadOnlySpan<GPUPerformanceEngineSnapshot> liveEngines,
        bool hasUtilizationSample)
    {
        GPUPerformanceDetailEngineSnapshot[] result =
            new GPUPerformanceDetailEngineSnapshot[engineSlots.Length];
        for (int slotIndex = 0; slotIndex < engineSlots.Length; slotIndex++)
        {
            GPUAdapterEngineIdentity identity = engineSlots[slotIndex];
            double utilizationPercent = 0;
            for (int liveIndex = 0; liveIndex < liveEngines.Length; liveIndex++)
            {
                GPUPerformanceEngineSnapshot liveEngine = liveEngines[liveIndex];
                if (liveEngine.EngineIndex != identity.EngineIndex) continue;

                if (double.IsFinite(liveEngine.UtilizationPercent))
                {
                    utilizationPercent = Math.Clamp(
                        liveEngine.UtilizationPercent,
                        0,
                        100);
                }
                break;
            }

            result[slotIndex] = new GPUPerformanceDetailEngineSnapshot(
                identity.EngineIndex,
                identity.Name,
                hasUtilizationSample,
                utilizationPercent);
        }

        return result;
    }

    private sealed class CachedGPUAdapterMetadata
    {
        public CachedGPUAdapterMetadata(
            GPUAdapterHardwareMetadata metadata,
            string? error,
            long nextRefreshTick,
            GPUAdapterEngineIdentity[]? retainedEngineSlots)
        {
            Metadata = metadata;
            Error = error;
            NextRefreshTick = nextRefreshTick;
            if (retainedEngineSlots is { Length: > 0 })
            {
                EngineSlots = retainedEngineSlots;
                return;
            }

            GPUPerformanceDetailEngineSnapshot[] selectedEngines = SelectEngineSlots(
                metadata.EngineCatalog.Span,
                [],
                false);
            EngineSlots = CopyEngineIdentities(selectedEngines);
        }

        public GPUAdapterHardwareMetadata Metadata { get; }
        public string? Error { get; }
        public long NextRefreshTick { get; }
        public GPUAdapterEngineIdentity[] EngineSlots { get; private set; }

        public void EnsureEngineSlots(
            ReadOnlySpan<GPUPerformanceEngineSnapshot> liveEngines,
            bool hasUtilizationSample)
        {
            if (EngineSlots.Length > 0 || liveEngines.Length == 0) return;

            GPUPerformanceDetailEngineSnapshot[] selectedEngines = SelectEngineSlots(
                [],
                liveEngines,
                hasUtilizationSample);
            EngineSlots = CopyEngineIdentities(selectedEngines);
        }

        private static GPUAdapterEngineIdentity[] CopyEngineIdentities(
            ReadOnlySpan<GPUPerformanceDetailEngineSnapshot> engines)
        {
            GPUAdapterEngineIdentity[] identities = new GPUAdapterEngineIdentity[engines.Length];
            for (int engineIndex = 0; engineIndex < engines.Length; engineIndex++)
            {
                GPUPerformanceDetailEngineSnapshot engine = engines[engineIndex];
                identities[engineIndex] = new GPUAdapterEngineIdentity(
                    engine.EngineIndex,
                    engine.Name);
            }
            return identities;
        }
    }
}
