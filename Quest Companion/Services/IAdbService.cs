namespace Quest_Companion.Services;

public record QuestDeviceInfo(
    string Serial,
    string Model,
    bool IsAuthorized,
    bool IsConnected
);

public record QuestBatteryInfo(
    int Level,
    bool IsCharging,
    string StatusText,
    float TemperatureCelsius
);

public record AdbOperationResult(bool Success, string Message);

public interface IAdbService
{
    string? AdbPath { get; }
    bool IsAdbAvailable { get; }

    Task<bool> InitializeAsync();
    Task<QuestDeviceInfo?> GetConnectedDeviceAsync(CancellationToken ct = default);
    Task<QuestBatteryInfo?> GetBatteryInfoAsync(string? serial = null, CancellationToken ct = default);
    Task<bool> IsQuestLinkRunningAsync(string? serial = null, CancellationToken ct = default);

    Task<AdbOperationResult> StartQuestLinkAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> StopQuestLinkAsync(string? serial = null, CancellationToken ct = default);

    Task<string?> GetWifiIpAddressAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> SetProximitySensorBypassAsync(bool bypass, string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> ForceProximitySensorActiveAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> RestoreDefaultProximitySensorAsync(string? serial = null, CancellationToken ct = default);

    Task<AdbOperationResult> WakeDisplayAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> SleepDisplayAsync(string? serial = null, CancellationToken ct = default);
    Task<bool> GetStayAwakeUsbAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> SetStayAwakeUsbAsync(bool enable, string? serial = null, CancellationToken ct = default);

    Task<AdbOperationResult> RebootHeadsetAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> ShutdownHeadsetAsync(string? serial = null, CancellationToken ct = default);

    Task<AdbOperationResult> InstallApkAsync(string apkPath, IProgress<string>? progress = null, string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> PushObbAsync(string obbPath, string? packageName = null, IProgress<string>? progress = null, string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> InstallDirectoryOrFileAsync(string path, IProgress<string>? progress = null, string? serial = null, CancellationToken ct = default);

    Task<AdbOperationResult> EnableWirelessAdbAsync(int port = 5555, string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> DisconnectWifiAdbAsync(CancellationToken ct = default);

    Task<bool> GetBoundaryEnabledAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> SetBoundaryEnabledAsync(bool enable, string? serial = null, CancellationToken ct = default);

    Task<AdbOperationResult> StartScreenRecordAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> StopScreenRecordAsync(string destinationDirectory, string? serial = null, CancellationToken ct = default);
    bool IsRecordingVideo { get; }

    Task<AdbOperationResult> ClearLogcatAsync(string? serial = null, CancellationToken ct = default);
    Task<int> GetCurrentRefreshRateAsync(string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> SetRefreshRateAsync(int refreshRateHz, string? serial = null, CancellationToken ct = default);
    Task<AdbOperationResult> TakeScreenshotAsync(string destinationPath, string? serial = null, CancellationToken ct = default);

    void Shutdown();
}
