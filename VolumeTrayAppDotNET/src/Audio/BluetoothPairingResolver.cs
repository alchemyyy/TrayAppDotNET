using VolumeTrayAppDotNET.Interop;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Separates Bluetooth audio endpoints of a paired device's current pairing from the endpoints
/// Windows keeps after a device is unpaired or paired through another radio.
/// Every one of those endpoints shares the headset's container id, and the pairing store keeps
/// records for radios that are gone, so the paired list Settings shows cannot pick the current
/// pairing on its own. The endpoint's devnode lineage can: the current pairing descends from a
/// radio adapter that is still present.
/// </summary>
internal static class BluetoothPairingResolver
{
    private const string SoftwareEndpointInstancePrefix = @"SWD\MMDEVAPI\";
    private const string RecordedPathSeparator = "}.";
    private const int MaxLineageDepth = 16;
    private const uint PairingFilterExpressionCount = 7;

    private static readonly Guid BluetoothClassGuid = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");
    private static readonly Guid BluetoothClassicProtocolID = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");
    private static readonly Guid BluetoothLowEnergyProtocolID = new("bb7bb05e-5972-42b5-94fc-76eaa7084d49");

    /// <summary>
    /// Devnode where the lineage walk starts: the adapter devnode Windows recorded on the endpoint,
    /// else the endpoint's own SWD devnode, whose documented parent is that same adapter devnode.
    /// </summary>
    internal static string ResolveLineageStart(string endpointID, string? recordedAdapterPath)
    {
        string adapterInstanceID = ParseRecordedAdapterInstanceID(recordedAdapterPath);
        return adapterInstanceID.Length > 0 ? adapterInstanceID : SoftwareEndpointInstancePrefix + endpointID;
    }

    /// <summary>Strips the "{N}." prefix Windows puts in front of the recorded adapter instance id.</summary>
    internal static string ParseRecordedAdapterInstanceID(string? recordedAdapterPath)
    {
        if (string.IsNullOrEmpty(recordedAdapterPath)) return string.Empty;
        if (recordedAdapterPath[0] != '{') return recordedAdapterPath;

        int separatorIndex = recordedAdapterPath.IndexOf(RecordedPathSeparator, StringComparison.Ordinal);
        return separatorIndex < 0
            ? string.Empty
            : recordedAdapterPath[(separatorIndex + RecordedPathSeparator.Length)..];
    }

    /// <summary>
    /// Container ids of every paired Bluetooth Classic or LE device in the Windows pairing store, the
    /// records Settings lists. Returns null when the query fails so callers can keep the last answer.
    /// </summary>
    public static unsafe HashSet<Guid>? QueryPairedContainers()
    {
        Guid classicProtocolID = BluetoothClassicProtocolID;
        Guid lowEnergyProtocolID = BluetoothLowEnergyProtocolID;
        byte pairedValue = CfgMgr32.DEVPROP_TRUE;

        // (ProtocolId == Classic OR ProtocolId == LE) AND IsPaired
        // NOTE: DevGetObjects rejects a group beside another top-level expression with E_INVALIDARG, so the whole filter sits in an AND group
        CfgMgr32.DEVPROP_FILTER_EXPRESSION* filter =
            stackalloc CfgMgr32.DEVPROP_FILTER_EXPRESSION[(int)PairingFilterExpressionCount];
        new Span<CfgMgr32.DEVPROP_FILTER_EXPRESSION>(filter, (int)PairingFilterExpressionCount).Clear();
        filter[0].Operator = CfgMgr32.DEVPROP_OPERATOR_AND_OPEN;
        filter[1].Operator = CfgMgr32.DEVPROP_OPERATOR_OR_OPEN;
        filter[2] = EqualsExpression(
            CfgMgr32.PKEY_Devices_Aep_ProtocolId,
            CfgMgr32.DEVPROP_TYPE_GUID,
            &classicProtocolID,
            (uint)sizeof(Guid));
        filter[3] = EqualsExpression(
            CfgMgr32.PKEY_Devices_Aep_ProtocolId,
            CfgMgr32.DEVPROP_TYPE_GUID,
            &lowEnergyProtocolID,
            (uint)sizeof(Guid));
        filter[4].Operator = CfgMgr32.DEVPROP_OPERATOR_OR_CLOSE;
        filter[5] = EqualsExpression(
            CfgMgr32.PKEY_Devices_Aep_IsPaired,
            CfgMgr32.DEVPROP_TYPE_BOOLEAN,
            &pairedValue,
            sizeof(byte));
        filter[6].Operator = CfgMgr32.DEVPROP_OPERATOR_AND_CLOSE;

        CfgMgr32.DEVPROPCOMPKEY requestedProperty = new()
        {
            Key = CfgMgr32.PKEY_Devices_Aep_ContainerId,
            Store = CfgMgr32.DEVPROP_STORE_SYSTEM
        };

        int hr = CfgMgr32.DevGetObjects(
            CfgMgr32.DevObjectTypeAEP,
            CfgMgr32.DevQueryFlagNone,
            requestedPropertyCount: 1,
            &requestedProperty,
            PairingFilterExpressionCount,
            filter,
            out uint objectCount,
            out CfgMgr32.DEV_OBJECT* objects);
        if (hr < 0)
        {
            TADNLog.LogDebug($"BluetoothPairingResolver: DevGetObjects failed, hr=0x{hr:X8}");
            return null;
        }

        HashSet<Guid> pairedContainers = [];
        try
        {
            for (uint objectIndex = 0; objectIndex < objectCount; objectIndex++)
            {
                CfgMgr32.DEV_OBJECT pairingRecord = objects[objectIndex];
                for (uint propertyIndex = 0; propertyIndex < pairingRecord.PropertyCount; propertyIndex++)
                {
                    CfgMgr32.DEVPROPERTY property = pairingRecord.Properties[propertyIndex];
                    if (!IsSameKey(property.CompKey.Key, CfgMgr32.PKEY_Devices_Aep_ContainerId)) continue;
                    if (property.Type != CfgMgr32.DEVPROP_TYPE_GUID || property.BufferSize != sizeof(Guid)) continue;
                    pairedContainers.Add(*(Guid*)property.Buffer);
                }
            }
        }
        finally
        {
            if (objects != null) CfgMgr32.DevFreeObjects(objectCount, objects);
        }

        return pairedContainers;
    }

    /// <summary>Tracked lineage starts whose lineage is a current pairing with its radio off.</summary>
    public static HashSet<string> ResolveRadioOffLineages(
        IReadOnlyList<string> lineageStarts,
        IReadOnlySet<string> presentInstanceIDs)
    {
        HashSet<string> radioOffLineages = new(StringComparer.OrdinalIgnoreCase);
        for (int lineageIndex = 0; lineageIndex < lineageStarts.Count; lineageIndex++)
        {
            string lineageStart = lineageStarts[lineageIndex];
            if (IsRadioOffPairing(ResolveLineage(lineageStart), presentInstanceIDs))
                radioOffLineages.Add(lineageStart);
        }

        return radioOffLineages;
    }

    /// <summary>
    /// Walks DEVPKEY_Device_Parent upward from a devnode, phantoms included, recording whether each
    /// devnode is Bluetooth class. Stops at the root, at a devnode Windows no longer has, or at
    /// <see cref="MaxLineageDepth"/>.
    /// </summary>
    public static List<LineageNode> ResolveLineage(string startInstanceID)
    {
        List<LineageNode> lineage = [];
        string? instanceID = startInstanceID;
        for (int depth = 0; depth < MaxLineageDepth && !string.IsNullOrEmpty(instanceID); depth++)
        {
            int cr = CfgMgr32.CM_Locate_DevNodeW(out uint devInst, instanceID, CfgMgr32.CM_LOCATE_DEVNODE_PHANTOM);
            if (cr != CfgMgr32.CR_SUCCESS) break;

            Guid? classGuid = CfgMgr32.TryReadGuidProperty(devInst, CfgMgr32.DEVPKEY_Device_ClassGuid);
            lineage.Add(new LineageNode(instanceID, classGuid == BluetoothClassGuid));
            instanceID = CfgMgr32.TryReadStringProperty(devInst, CfgMgr32.DEVPKEY_Device_Parent);
        }

        return lineage;
    }

    /// <summary>
    /// Index of the radio adapter: the top of the first run of Bluetooth-class devnodes. Below it sit
    /// the Bluetooth stack enumerators, above it the bus the adapter hangs off. -1 when there is none.
    /// </summary>
    internal static int FindRadioAdapterIndex(IReadOnlyList<LineageNode> lineage)
    {
        int nodeIndex = 0;
        while (nodeIndex < lineage.Count && !lineage[nodeIndex].IsBluetoothClass) nodeIndex++;
        if (nodeIndex == lineage.Count) return -1;

        while (nodeIndex + 1 < lineage.Count && lineage[nodeIndex + 1].IsBluetoothClass) nodeIndex++;
        return nodeIndex;
    }

    /// <summary>
    /// True when the first present devnode in the lineage is the radio adapter itself.
    /// That is the shape of a radio that is off: the adapter stays present but tears down its Bluetooth stack.
    /// With the radio on, the stack is present, so the first present devnode sits below the adapter;
    /// an older pairing's adapter is gone, so its first present devnode sits above.
    /// </summary>
    internal static bool IsRadioOffPairing(IReadOnlyList<LineageNode> lineage, IReadOnlySet<string> presentInstanceIDs)
    {
        int radioAdapterIndex = FindRadioAdapterIndex(lineage);
        if (radioAdapterIndex < 0) return false;

        for (int nodeIndex = 0; nodeIndex < lineage.Count; nodeIndex++)
        {
            if (presentInstanceIDs.Contains(lineage[nodeIndex].InstanceID)) return nodeIndex == radioAdapterIndex;
        }

        return false;
    }

    private static unsafe CfgMgr32.DEVPROP_FILTER_EXPRESSION EqualsExpression(
        CfgMgr32.DEVPROPKEY key,
        uint type,
        void* value,
        uint valueSize) => new()
    {
        Operator = CfgMgr32.DEVPROP_OPERATOR_EQUALS,
        Property = new CfgMgr32.DEVPROPERTY
        {
            CompKey = new CfgMgr32.DEVPROPCOMPKEY { Key = key, Store = CfgMgr32.DEVPROP_STORE_SYSTEM },
            Type = type,
            BufferSize = valueSize,
            Buffer = value
        }
    };

    private static bool IsSameKey(CfgMgr32.DEVPROPKEY left, CfgMgr32.DEVPROPKEY right) =>
        left.fmtid == right.fmtid && left.pid == right.pid;

    internal readonly record struct LineageNode(string InstanceID, bool IsBluetoothClass);
}
