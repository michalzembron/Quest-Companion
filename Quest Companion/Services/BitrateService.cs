using Microsoft.Win32;
using System.IO;

namespace Quest_Companion.Services;

public class BitrateService : IBitrateService
{
    private const string RegistryKeyPath = @"SOFTWARE\Oculus\RemoteHeadset";
    private const string ValueName = "BitrateMbps";

    public int CurrentBitrateMbps => GetCurrentBitrate();

    public IReadOnlyList<BitratePreset> Presets { get; } =
    [
        new BitratePreset("Default (Dynamic)", 0, "Automatic dynamic bitrate managed by Meta (Zero Stutter)"),
        new BitratePreset("USB 2.0 Safe (100M)", 100, "Safe for USB 2.0 ports & cables (eliminates stuttering)"),
        new BitratePreset("AirLink / USB 2.0 (150M)", 150, "Optimal for 5GHz Wi-Fi and good USB 2.0 ports"),
        new BitratePreset("USB 3.0 High (350M)", 350, "Crisp image without compression artifacts (Requires USB 3.0)"),
        new BitratePreset("USB 3.0 Ultra (500M)", 500, "Maximum sharpness for high-end GPUs on USB 3.0")
    ];

    public int GetCurrentBitrate()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key != null)
            {
                var val = key.GetValue(ValueName);
                if (val is int intVal) return intVal;
                if (val != null && int.TryParse(val.ToString(), out int parsed)) return parsed;
            }
        }
        catch { }

        return 0;
    }

    public bool SetBitrate(int bitrateMbps)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, true);
            if (key != null)
            {
                if (bitrateMbps <= 0)
                {
                    key.DeleteValue(ValueName, false);
                }
                else
                {
                    key.SetValue(ValueName, bitrateMbps, RegistryValueKind.DWord);
                }
                return true;
            }
        }
        catch { }

        return false;
    }

    public ConnectionDetail GetConnectionDetail()
    {
        try
        {
            string cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Oculus", "DeviceCache.json");

            if (File.Exists(cachePath))
            {
                string json = File.ReadAllText(cachePath);

                if (json.Contains("\"isUsingAirLink\":true", StringComparison.OrdinalIgnoreCase))
                {
                    return new ConnectionDetail(
                        "AirLink (Wi-Fi)",
                        IsUsb2: false,
                        IsUsb3: false,
                        IsAirLink: true,
                        "Wireless AirLink connection detected.",
                        "For Wi-Fi, 100-150 Mbps is recommended to avoid network latency spikes.");
                }

                if (json.Contains("\"mode\":\"usb2\"", StringComparison.OrdinalIgnoreCase))
                {
                    return new ConnectionDetail(
                        "USB 2.0 (Bandwidth Limited)",
                        IsUsb2: true,
                        IsUsb3: false,
                        IsAirLink: false,
                        "USB 2.0 connection detected (~300 Mbps usable limit).",
                        "Bitrates above 150 Mbps overload USB 2.0 and cause heavy stuttering. Use 100 Mbps or Default (Dynamic). Plug into a USB 3.0 port for higher quality.");
                }

                if (json.Contains("\"mode\":\"usb3\"", StringComparison.OrdinalIgnoreCase))
                {
                    return new ConnectionDetail(
                        "USB 3.0 (SuperSpeed)",
                        IsUsb2: false,
                        IsUsb3: true,
                        IsAirLink: false,
                        "USB 3.0 SuperSpeed connection active (up to 5 Gbps).",
                        "High bitrates (350 - 500 Mbps) are supported smoothly.");
                }
            }
        }
        catch { }

        return new ConnectionDetail(
            "Disconnected / Standalone",
            IsUsb2: false,
            IsUsb3: false,
            IsAirLink: false,
            "No active Meta Quest Link session found.",
            string.Empty);
    }

    public (int RecommendedBitrate, string Reason) AutoDetectOptimalBitrate(bool isHeadsetConnected, bool isUsbCable)
    {
        var detail = GetConnectionDetail();

        if (detail.IsAirLink)
        {
            return (150, "AirLink Wi-Fi connection detected. Recommended: 150 Mbps for smooth latency.");
        }

        if (detail.IsUsb2)
        {
            return (100, "USB 2.0 detected (~300 Mbps limit). Set to safe 100 Mbps to prevent stuttering. (Use USB 3.0 for 350-500 Mbps).");
        }

        if (detail.IsUsb3)
        {
            return (350, "USB 3.0 connection detected! Set to 350 Mbps for crisp, smooth visuals.");
        }

        if (!isHeadsetConnected)
        {
            return (0, "No headset detected. Reset to Meta Default (Dynamic) to prevent stutter.");
        }

        return (100, "Safe profile: 100 Mbps for smooth gameplay.");
    }
}
