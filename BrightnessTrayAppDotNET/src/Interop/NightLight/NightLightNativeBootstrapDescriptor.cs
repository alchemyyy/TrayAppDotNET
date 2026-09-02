namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Identifies one SettingsHandlers_Display image and the native entry points required by the Night Light helper.
/// </summary>
internal readonly record struct NightLightNativeBootstrapDescriptor
{
    public NightLightNativeBootstrapDescriptor(
        Guid PDBGuid,
        uint PDBAge,
        uint imageSize,
        uint initializeRVA,
        uint sInstanceRVA,
        uint setTargetColorTemperatureRVA,
        uint setPreviewColorTemperatureChangesRVA,
        uint setBlueLightActiveRVA)
    {
        ValidateValues(
            PDBGuid,
            PDBAge,
            imageSize,
            initializeRVA,
            sInstanceRVA,
            setTargetColorTemperatureRVA,
            setPreviewColorTemperatureChangesRVA,
            setBlueLightActiveRVA);

        this.PDBGuid = PDBGuid;
        this.PDBAge = PDBAge;
        ImageSize = imageSize;
        InitializeRVA = initializeRVA;
        SInstanceRVA = sInstanceRVA;
        SetTargetColorTemperatureRVA = setTargetColorTemperatureRVA;
        SetPreviewColorTemperatureChangesRVA = setPreviewColorTemperatureChangesRVA;
        SetBlueLightActiveRVA = setBlueLightActiveRVA;
    }

    public Guid PDBGuid { get; }

    public uint PDBAge { get; }

    public uint ImageSize { get; }

    public uint InitializeRVA { get; }

    public uint SInstanceRVA { get; }

    public uint SetTargetColorTemperatureRVA { get; }

    public uint SetPreviewColorTemperatureChangesRVA { get; }

    public uint SetBlueLightActiveRVA { get; }

    /// <summary>Serializes the descriptor for the helper's initial protocol command.</summary>
    public string ToInitializationCommand() => NightLightHelperProtocol.SerializeInitialization(this);

    /// <summary>Returns whether this descriptor belongs to the supplied PE and PDB identity.</summary>
    public bool HasImageIdentity(Guid PDBGuid, uint PDBAge, uint imageSize) =>
        this.PDBGuid == PDBGuid && this.PDBAge == PDBAge && ImageSize == imageSize;

    /// <summary>Rejects a default or otherwise invalid descriptor before it crosses the process boundary.</summary>
    public void Validate() => ValidateValues(
        PDBGuid,
        PDBAge,
        ImageSize,
        InitializeRVA,
        SInstanceRVA,
        SetTargetColorTemperatureRVA,
        SetPreviewColorTemperatureChangesRVA,
        SetBlueLightActiveRVA);

    private static void ValidateValues(
        Guid PDBGuid,
        uint PDBAge,
        uint imageSize,
        uint initializeRVA,
        uint sInstanceRVA,
        uint setTargetColorTemperatureRVA,
        uint setPreviewColorTemperatureChangesRVA,
        uint setBlueLightActiveRVA)
    {
        if (PDBGuid == Guid.Empty)
            throw new ArgumentException("The PDB GUID must not be empty.", nameof(PDBGuid));
        if (PDBAge == 0)
            throw new ArgumentOutOfRangeException(nameof(PDBAge), "The PDB age must be positive.");
        if (imageSize == 0)
            throw new ArgumentOutOfRangeException(nameof(imageSize), "The PE image size must be positive.");

        ValidateRVA(initializeRVA, imageSize, nameof(initializeRVA));
        ValidateRVA(sInstanceRVA, imageSize, nameof(sInstanceRVA));
        ValidateRVA(setTargetColorTemperatureRVA, imageSize, nameof(setTargetColorTemperatureRVA));
        ValidateRVA(
            setPreviewColorTemperatureChangesRVA,
            imageSize,
            nameof(setPreviewColorTemperatureChangesRVA));
        ValidateRVA(setBlueLightActiveRVA, imageSize, nameof(setBlueLightActiveRVA));
    }

    private static void ValidateRVA(uint RVA, uint imageSize, string parameterName)
    {
        if (RVA == 0 || RVA >= imageSize)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                RVA,
                $"The RVA must be nonzero and smaller than the PE image size 0x{imageSize:X}.");
        }
    }
}
