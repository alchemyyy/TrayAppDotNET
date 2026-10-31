namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// The single normalizer from PDH engine types (<c>engtype_VideoDecode</c>, <c>3D#2</c>) and kernel node names
/// (<c>Video Decode</c>, <c>Legacy-Overlay</c>) to the stable user-facing engine names the GPU and NPU views show.
/// </summary>
internal static class GPUEngineNames
{
    public const string Fallback = "GPU Engine";

    public static string Normalize(ReadOnlySpan<char> name)
    {
        ReadOnlySpan<char> value = name.Trim();
        if (value.IsEmpty) return Fallback;

        int duplicateSuffixIndex = value.IndexOf('#');
        if (duplicateSuffixIndex >= 0) value = value[..duplicateSuffixIndex].TrimEnd();
        if (value.IsEmpty) return Fallback;

        Span<char> compact = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        int length = 0;
        foreach (char character in value)
        {
            if (character is ' ' or '-' or '_') continue;
            compact[length++] = char.ToLowerInvariant(character);
        }

        return compact[..length] switch
        {
            "3d" => "3D",
            "copy" => "Copy",
            "compute" => "Compute",
            "videodecode" => "Video Decode",
            "videoencode" => "Video Encode",
            "videoprocessing" => "Video Processing",
            "videocodec" => "Video Codec",
            "legacyoverlay" => "Legacy Overlay",
            "overlay" => "Overlay",
            "sceneassembly" => "Scene Assembly",
            "opticalflow" => "Optical Flow",
            "security" => "Security",
            "crypto" => "Crypto",
            _ => value.ToString()
        };
    }
}
