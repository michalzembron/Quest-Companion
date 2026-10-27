namespace Quest_Companion.Services;

public record BitratePreset(string Name, int BitrateMbps, string Description);

public record ConnectionDetail(
    string Mode,
    bool IsUsb2,
    bool IsUsb3,
    bool IsAirLink,
    string Description,
    string Warning);

public interface IBitrateService
{
    int CurrentBitrateMbps { get; }
    IReadOnlyList<BitratePreset> Presets { get; }

    int GetCurrentBitrate();
    bool SetBitrate(int bitrateMbps);
    (int RecommendedBitrate, string Reason) AutoDetectOptimalBitrate(bool isHeadsetConnected, bool isUsbCable);
    ConnectionDetail GetConnectionDetail();
}
