namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// One A2DP codec choice as published by the <c>Microsoft.Windows.Bluetooth.BthA2dp</c> ETW
/// provider on every AVDTP SET_CONFIGURATION / RECONFIGURE event.
/// <para/>
/// Standard ids 0x00-0x04 map directly to SBC / MP3 / AAC / ATRAC. 0xFF means look at
/// (VendorId, VendorCodecId) for the vendor-specific codec identity - Qualcomm aptX family,
/// Sony LDAC, Samsung Scalable, Savitech LHDC, etc.
/// <para/>
/// Vendor ids are Bluetooth SIG Company Identifiers
/// (https://www.bluetooth.com/specifications/assigned-numbers/company-identifiers/); codec ids
/// are vendor-defined.
/// </summary>
internal sealed record BluetoothCodec(byte StandardCodecId, int VendorId, int VendorCodecId)
{
    /// <summary>
    /// Display-ready codec name, e.g. "SBC", "AAC", "Qualcomm aptX HD".
    /// Falls back to a raw "Unknown Codec: 0xVVVV:0xCCCC" form when the (vendor, codec) pair
    /// isn't in the known-codec table so the user still sees the ids instead of an empty label.
    /// </summary>
    public string FriendlyName => ResolveFriendlyName();

    private string ResolveFriendlyName()
    {
        switch (StandardCodecId)
        {
            case 0x00: return "SBC";
            case 0x01: return "MP3";
            case 0x02: return "AAC";
            case 0x04: return "ATRAC";
        }

        if (StandardCodecId != 0xFF)
            return AudioLocalization.BluetoothCodecUnknownInvalidVendor(StandardCodecId, VendorId, VendorCodecId);

        // Vendor-codec table - same set BluetoothAudioCodecInspector publishes, sourced from
        // helgeklein.com's ETW walkthrough and the btcodecs catalog.
        return (VendorId, VendorCodecId) switch
        {
            (0x004F, 0x0001) => "Qualcomm/CSR aptX",
            (0x00D7, 0x0024) => "Qualcomm/CSR aptX HD",
            (0x00D7, 0x0002) => "Qualcomm/CSR aptX LL",
            (0x000A, 0x0002) => "Qualcomm/CSR aptX LL",
            (0x000A, 0x0001) => "Qualcomm/CSR FastStream",
            (0x000A, 0x0104) => "Qualcomm/CSR TWS v3 AAC",
            (0x000A, 0x0105) => "Qualcomm/CSR TWS v3 MP3",
            (0x000A, 0x0106) => "Qualcomm/CSR TWS v3 aptX",
            (0x00D7, 0x00AD) => "Qualcomm/CSR aptX Adaptive",
            (0x012D, 0x00AA) => "Sony LDAC",
            (0x0075, 0x0102) => "Samsung HD",
            (0x0075, 0x0103) => "Samsung Scalable",
            (0x0075, 0x0104) => "Samsung UHQ",
            (0x053A, 0x484C) => "Savitech LHDC",
            _ => AudioLocalization.BluetoothCodecUnknown(VendorId, VendorCodecId),
        };
    }

    public override string ToString() => FriendlyName;
}
