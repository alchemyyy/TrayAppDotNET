using VolumeTrayAppDotNET.Interop;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>Lightweight app-group state used to resolve cross-device drawer ownership.</summary>
internal readonly record struct AppDrawerVisibilityCandidate(
    EDataFlow DataFlow,
    string DeviceID,
    bool IsDefaultDevice,
    string AppID,
    AudioSessionState State);

/// <summary>
/// Resolves which locally eligible app groups should be displayed when Windows exposes the same
/// app session on multiple endpoints. This is presentation-only; hidden sessions remain tracked.
/// </summary>
internal static class AppDrawerVisibilityPolicy
{
    /// <summary>
    /// Returns one visibility flag per input candidate. Active copies take precedence. When every
    /// copy is inactive, the current default endpoint owns the drawer entry if it has a copy.
    /// </summary>
    public static bool[] Resolve(IReadOnlyList<AppDrawerVisibilityCandidate> candidates)
    {
        bool[] isVisible = new bool[candidates.Count];
        Dictionary<AppDrawerVisibilityKey, List<int>> candidateIndexesByApp = [];

        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            AppDrawerVisibilityCandidate candidate = candidates[candidateIndex];
            if (candidate.State == AudioSessionState.Expired) continue;

            isVisible[candidateIndex] = true;
            AppDrawerVisibilityKey key = new(candidate.DataFlow, candidate.AppID);
            if (!candidateIndexesByApp.TryGetValue(key, out List<int>? candidateIndexes))
            {
                candidateIndexes = [];
                candidateIndexesByApp.Add(key, candidateIndexes);
            }

            candidateIndexes.Add(candidateIndex);
        }

        foreach (KeyValuePair<AppDrawerVisibilityKey, List<int>> appCandidates in candidateIndexesByApp)
        {
            List<int> candidateIndexes = appCandidates.Value;
            if (candidateIndexes.Count < 2) continue;

            HashSet<string> deviceIDs = new(StringComparer.Ordinal);
            bool hasActiveCopy = false;
            bool hasDefaultCopy = false;
            foreach (int candidateIndex in candidateIndexes)
            {
                AppDrawerVisibilityCandidate candidate = candidates[candidateIndex];
                deviceIDs.Add(candidate.DeviceID);
                hasActiveCopy |= candidate.State == AudioSessionState.Active;
                hasDefaultCopy |= candidate.IsDefaultDevice;
            }

            if (deviceIDs.Count < 2) continue;

            if (hasActiveCopy)
            {
                foreach (int candidateIndex in candidateIndexes)
                {
                    isVisible[candidateIndex] =
                        candidates[candidateIndex].State == AudioSessionState.Active;
                }

                continue;
            }

            if (!hasDefaultCopy) continue;

            foreach (int candidateIndex in candidateIndexes)
                isVisible[candidateIndex] = candidates[candidateIndex].IsDefaultDevice;
        }

        return isVisible;
    }

    private readonly record struct AppDrawerVisibilityKey(EDataFlow DataFlow, string AppID);
}
