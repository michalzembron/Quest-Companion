using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Quest_Companion.Services;

public class AdbService : IAdbService
{
    private string? _adbPath;
    private Process? _recordingProcess;
    private string? _activeRecordingRemotePath;

    public bool IsRecordingVideo => _recordingProcess != null && !_recordingProcess.HasExited;

    private static readonly Regex BatteryLevelRegex = new(@"level:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex BatteryStatusRegex = new(@"status:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex BatteryTempRegex = new(@"temperature:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex BatteryAcRegex = new(@"AC powered:\s*(true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BatteryUsbRegex = new(@"USB powered:\s*(true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ObbPackageRegex = new(@"^(?:main|patch)\.\d+\.(?<pkg>[a-zA-Z0-9_\.]+)\.obb$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? AdbPath => _adbPath;
    public bool IsAdbAvailable => !string.IsNullOrEmpty(_adbPath) && File.Exists(_adbPath);

    public AdbService()
    {
        _adbPath = FindAdbExecutable();
    }

    public Task<bool> InitializeAsync()
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrEmpty(_adbPath) || !File.Exists(_adbPath))
            {
                _adbPath = FindAdbExecutable();
            }

            return IsAdbAvailable;
        });
    }

    public async Task<QuestDeviceInfo?> GetConnectedDeviceAsync(CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return null;

        var (exitCode, output, _) = await ExecuteAdbCommandAsync("devices -l", ct);
        if (exitCode != 0) return null;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("*", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var serial = parts[0];
                var status = parts[1];

                bool isAuthorized = string.Equals(status, "device", StringComparison.OrdinalIgnoreCase);
                bool isConnected = true;

                string model = "Meta Quest";
                var modelMatch = Regex.Match(trimmed, @"model:(\S+)");
                if (modelMatch.Success)
                {
                    model = modelMatch.Groups[1].Value.Replace('_', ' ');
                }
                else if (isAuthorized)
                {
                    var (mCode, mOut, _) = await ExecuteAdbCommandAsync($"-s {serial} shell getprop ro.product.model", ct);
                    if (mCode == 0 && !string.IsNullOrWhiteSpace(mOut))
                    {
                        model = mOut.Trim();
                    }
                }

                return new QuestDeviceInfo(serial, model, isAuthorized, isConnected);
            }
        }

        return null;
    }

    public async Task<QuestBatteryInfo?> GetBatteryInfoAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return null;

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, _) = await ExecuteAdbCommandAsync($"{target}shell dumpsys battery", ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;

        int level = -1;
        var levelMatch = BatteryLevelRegex.Match(output);
        if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int parsedLevel))
        {
            level = parsedLevel;
        }

        int statusCode = 1;
        var statusMatch = BatteryStatusRegex.Match(output);
        if (statusMatch.Success && int.TryParse(statusMatch.Groups[1].Value, out int parsedStatus))
        {
            statusCode = parsedStatus;
        }

        bool ac = BatteryAcRegex.Match(output).Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        bool usb = BatteryUsbRegex.Match(output).Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        bool isCharging = statusCode == 2 || ac || usb;

        float temperature = 0f;
        var tempMatch = BatteryTempRegex.Match(output);
        if (tempMatch.Success && float.TryParse(tempMatch.Groups[1].Value, out float parsedTemp))
        {
            temperature = parsedTemp / 10f;
        }

        string statusText = statusCode switch
        {
            2 => "Charging",
            3 => "On Battery",
            4 => "Not Charging",
            5 => "Fully Charged",
            _ => isCharging ? "Charging" : "On Battery"
        };

        return new QuestBatteryInfo(level, isCharging, statusText, temperature);
    }

    public async Task<bool> IsQuestLinkRunningAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return false;

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, _) = await ExecuteAdbCommandAsync($"{target}shell pidof com.oculus.xrstreamingclient", ct);

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return Regex.IsMatch(output.Trim(), @"^\d+");
        }

        return false;
    }

    public async Task<AdbOperationResult> StartQuestLinkAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, error) = await ExecuteAdbCommandAsync(
            $"{target}shell am start -S com.oculus.xrstreamingclient/.MainActivity", ct);

        if (exitCode == 0 && (output.Contains("Starting:") || string.IsNullOrWhiteSpace(error)))
        {
            return new AdbOperationResult(true, "Meta Quest Link client started on headset.");
        }

        return new AdbOperationResult(false, $"Error starting Quest Link: {output} {error}".Trim());
    }

    public async Task<AdbOperationResult> StopQuestLinkAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync(
            $"{target}shell am force-stop com.oculus.xrstreamingclient", ct);

        if (exitCode == 0)
        {
            return new AdbOperationResult(true, "Meta Quest Link has been stopped.");
        }

        return new AdbOperationResult(false, $"Error stopping Quest Link: {error}".Trim());
    }

    public async Task<AdbOperationResult> SetProximitySensorBypassAsync(bool bypass, string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        string intent = bypass
            ? "com.oculus.vrpowermanager.prox_close"
            : "com.oculus.vrpowermanager.automation_disable";

        var (exitCode, output, error) = await ExecuteAdbCommandAsync(
            $"{target}shell am broadcast -a {intent}", ct);

        if (exitCode == 0 && output.Contains("result=0"))
        {
            string msg = bypass
                ? "Proximity sensor bypassed (headset display will stay awake)."
                : "Proximity sensor restored to default behavior.";
            return new AdbOperationResult(true, msg);
        }

        return new AdbOperationResult(false, $"Error switching sensor: {output} {error}".Trim());
    }

    public async Task<string?> GetWifiIpAddressAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return null;

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, _) = await ExecuteAdbCommandAsync($"{target}shell ip -f inet addr show wlan0", ct);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            var match = Regex.Match(output, @"inet\s+([0-9\.]+)");
            if (match.Success) return match.Groups[1].Value;
        }

        var (exitCode2, output2, _) = await ExecuteAdbCommandAsync($"{target}shell ip route", ct);
        if (exitCode2 == 0 && !string.IsNullOrWhiteSpace(output2))
        {
            var match = Regex.Match(output2, @"src\s+([0-9\.]+)");
            if (match.Success) return match.Groups[1].Value;
        }

        return null;
    }

    public async Task<AdbOperationResult> ForceProximitySensorActiveAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}shell am broadcast -a com.oculus.vrpowermanager.prox_close", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, "Proximity sensor forced active.")
            : new AdbOperationResult(false, $"Error forcing sensor: {error}".Trim());
    }

    public async Task<AdbOperationResult> RestoreDefaultProximitySensorAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}shell am broadcast -a com.oculus.vrpowermanager.automation_disable", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, "Proximity sensor restored to default.")
            : new AdbOperationResult(false, $"Error restoring sensor: {error}".Trim());
    }

    public async Task<AdbOperationResult> WakeDisplayAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}shell input keyevent KEYCODE_WAKEUP", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, "Display wake signal sent.")
            : new AdbOperationResult(false, $"Error waking display: {error}".Trim());
    }

    public async Task<AdbOperationResult> SleepDisplayAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}shell input keyevent KEYCODE_SLEEP", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, "Display sleep signal sent.")
            : new AdbOperationResult(false, $"Error putting display to sleep: {error}".Trim());
    }

    public async Task<bool> GetStayAwakeUsbAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return false;
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, _) = await ExecuteAdbCommandAsync($"{target}shell settings get global stay_on_while_plugged_in", ct);
        if (exitCode == 0 && int.TryParse(output.Trim(), out int val))
        {
            return val != 0;
        }
        return false;
    }

    public async Task<AdbOperationResult> SetStayAwakeUsbAsync(bool enable, string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        int val = enable ? 3 : 0;
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}shell settings put global stay_on_while_plugged_in {val}", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, enable ? "Stay awake on USB enabled." : "Stay awake on USB disabled.")
            : new AdbOperationResult(false, $"Error setting stay awake: {error}".Trim());
    }

    public async Task<AdbOperationResult> DisconnectWifiAdbAsync(CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        var (exitCode, _, error) = await ExecuteAdbCommandAsync("disconnect", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, "Disconnected Wi-Fi ADB sessions.")
            : new AdbOperationResult(false, $"Error disconnecting Wi-Fi ADB: {error}".Trim());
    }

    public async Task<AdbOperationResult> ClearLogcatAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}logcat -c", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, "Logcat buffer cleared.")
            : new AdbOperationResult(false, $"Error clearing logcat: {error}".Trim());
    }

    public async Task<bool> GetBoundaryEnabledAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return true;
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, _) = await ExecuteAdbCommandAsync($"{target}shell getprop debug.oculus.guardian_pause", ct);
        if (exitCode == 0)
        {
            var trimmed = output.Trim();
            if (trimmed == "1") return false;
        }
        return true;
    }

    public async Task<AdbOperationResult> SetBoundaryEnabledAsync(bool enable, string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        int pauseVal = enable ? 0 : 1;
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}shell setprop debug.oculus.guardian_pause {pauseVal}", ct);
        return exitCode == 0
            ? new AdbOperationResult(true, enable ? "Boundary (Guardian) enabled." : "Boundary (Guardian) paused.")
            : new AdbOperationResult(false, $"Error setting boundary: {error}".Trim());
    }

    public async Task<AdbOperationResult> StartScreenRecordAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        if (IsRecordingVideo) return new AdbOperationResult(false, "Video recording is already in progress.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        _activeRecordingRemotePath = "/sdcard/quest_recording.mp4";

        await ExecuteAdbCommandAsync($"{target}shell rm -f {_activeRecordingRemotePath}", ct);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _adbPath!,
                Arguments = $"{target}shell screenrecord --size 1920x1080 --bit-rate 16000000 {_activeRecordingRemotePath}",
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _recordingProcess = Process.Start(psi);
            if (_recordingProcess == null)
            {
                return new AdbOperationResult(false, "Failed to start screenrecord process.");
            }

            return new AdbOperationResult(true, "Video recording started on headset.");
        }
        catch (Exception ex)
        {
            return new AdbOperationResult(false, $"Failed to start recording: {ex.Message}");
        }
    }

    public async Task<AdbOperationResult> StopScreenRecordAsync(string destinationDirectory, string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";

        try
        {
            await ExecuteAdbCommandAsync($"{target}shell pkill -2 screenrecord", ct);

            if (_recordingProcess != null && !_recordingProcess.HasExited)
            {
                try { _recordingProcess.WaitForExit(3000); } catch { }
                if (!_recordingProcess.HasExited)
                {
                    try { _recordingProcess.Kill(true); } catch { }
                }
                _recordingProcess.Dispose();
                _recordingProcess = null;
            }

            await Task.Delay(1000, ct);

            Directory.CreateDirectory(destinationDirectory);
            string fileName = $"Quest_Recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            string localFilePath = Path.Combine(destinationDirectory, fileName);

            string remotePath = _activeRecordingRemotePath ?? "/sdcard/quest_recording.mp4";
            var (pullCode, _, pullErr) = await ExecuteAdbCommandAsync($"{target}pull \"{remotePath}\" \"{localFilePath}\"", ct, timeoutMs: 60000);

            await ExecuteAdbCommandAsync($"{target}shell rm -f \"{remotePath}\"", ct);

            if (pullCode == 0 && File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 0)
            {
                return new AdbOperationResult(true, localFilePath);
            }

            return new AdbOperationResult(false, $"Could not retrieve recording file: {pullErr}");
        }
        catch (Exception ex)
        {
            return new AdbOperationResult(false, $"Error stopping recording: {ex.Message}");
        }
        finally
        {
            _recordingProcess = null;
        }
    }

    public async Task<AdbOperationResult> RebootHeadsetAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}reboot", ct);

        if (exitCode == 0)
        {
            return new AdbOperationResult(true, "Reboot signal sent to headset.");
        }

        return new AdbOperationResult(false, $"Error rebooting headset: {error}".Trim());
    }

    public async Task<AdbOperationResult> ShutdownHeadsetAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, _, error) = await ExecuteAdbCommandAsync($"{target}shell reboot -p", ct);

        if (exitCode == 0)
        {
            return new AdbOperationResult(true, "Shut down signal sent to headset.");
        }

        return new AdbOperationResult(false, $"Error shutting down headset: {error}".Trim());
    }

    public async Task<AdbOperationResult> InstallApkAsync(
        string apkPath,
        IProgress<string>? progress = null,
        string? serial = null,
        CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        if (!File.Exists(apkPath)) return new AdbOperationResult(false, "Specified APK file does not exist.");

        progress?.Report($"Uploading and installing {Path.GetFileName(apkPath)} on headset...");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, error) = await ExecuteAdbCommandAsync(
            $"{target}install -r -d \"{apkPath}\"", ct, timeoutMs: 180000);

        if (exitCode == 0 && output.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            return new AdbOperationResult(true, $"{Path.GetFileName(apkPath)} successfully installed!");
        }

        string failureReason = output.Contains("Failure") ? output.Trim() : error.Trim();
        return new AdbOperationResult(false, $"APK installation error: {failureReason}");
    }

    public async Task<AdbOperationResult> PushObbAsync(
        string obbPath,
        string? packageName = null,
        IProgress<string>? progress = null,
        string? serial = null,
        CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");
        if (!File.Exists(obbPath)) return new AdbOperationResult(false, "OBB file does not exist.");

        string fileName = Path.GetFileName(obbPath);

        if (string.IsNullOrWhiteSpace(packageName))
        {
            var match = ObbPackageRegex.Match(fileName);
            if (match.Success)
            {
                packageName = match.Groups["pkg"].Value;
            }
            else
            {
                var parentDir = Path.GetFileName(Path.GetDirectoryName(obbPath));
                if (!string.IsNullOrEmpty(parentDir) && parentDir.Contains('.'))
                {
                    packageName = parentDir;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(packageName))
        {
            return new AdbOperationResult(false, $"Could not determine package name for {fileName}. Make sure the file follows main.<version>.<package>.obb naming or is inside its package folder.");
        }

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        string remoteDir = $"/sdcard/Android/obb/{packageName}";

        progress?.Report($"Creating directory {remoteDir}...");
        await ExecuteAdbCommandAsync($"{target}shell mkdir -p \"{remoteDir}\"", ct);

        progress?.Report($"Pushing OBB file ({fileName}) to headset...");
        var (pushExit, pushOut, pushErr) = await ExecuteAdbCommandAsync(
            $"{target}push \"{obbPath}\" \"{remoteDir}/{fileName}\"", ct, timeoutMs: 600000);

        if (pushExit == 0)
        {
            return new AdbOperationResult(true, $"OBB file ({fileName}) successfully uploaded to {remoteDir}!");
        }

        return new AdbOperationResult(false, $"Error pushing OBB: {pushErr} {pushOut}".Trim());
    }

    public async Task<AdbOperationResult> InstallDirectoryOrFileAsync(
        string path,
        IProgress<string>? progress = null,
        string? serial = null,
        CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            if (path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                return await InstallApkAsync(path, progress, serial, ct);
            }
            if (path.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
            {
                return await PushObbAsync(path, null, progress, serial, ct);
            }
        }
        else if (Directory.Exists(path))
        {
            var apkFiles = Directory.GetFiles(path, "*.apk", SearchOption.AllDirectories);
            var obbFiles = Directory.GetFiles(path, "*.obb", SearchOption.AllDirectories);

            if (apkFiles.Length == 0 && obbFiles.Length == 0)
            {
                return new AdbOperationResult(false, "No .apk or .obb files found in the selected folder.");
            }

            int installedApks = 0;
            int pushedObbs = 0;

            foreach (var apk in apkFiles)
            {
                var apkRes = await InstallApkAsync(apk, progress, serial, ct);
                if (!apkRes.Success) return apkRes;
                installedApks++;
            }

            foreach (var obb in obbFiles)
            {
                var obbRes = await PushObbAsync(obb, null, progress, serial, ct);
                if (!obbRes.Success) return obbRes;
                pushedObbs++;
            }

            return new AdbOperationResult(true, $"Success! Installed {installedApks} APK(s) and pushed {pushedObbs} OBB(s) from {Path.GetFileName(path)}.");
        }

        return new AdbOperationResult(false, "Path does not exist or format is unsupported.");
    }

    public async Task<AdbOperationResult> EnableWirelessAdbAsync(int port = 5555, string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, error) = await ExecuteAdbCommandAsync($"{target}tcpip {port}", ct);

        if (exitCode == 0 && output.Contains("restarting in TCP mode"))
        {
            var (ipCode, ipOut, _) = await ExecuteAdbCommandAsync($"{target}shell ip route", ct);
            string ipAddress = "headset-ip";
            var ipMatch = Regex.Match(ipOut, @"src\s+([0-9\.]+)");
            if (ipMatch.Success)
            {
                ipAddress = ipMatch.Groups[1].Value;
            }

            return new AdbOperationResult(true, $"Wireless ADB enabled on port {port}! You can disconnect the cable and run: adb connect {ipAddress}:{port}");
        }

        return new AdbOperationResult(false, $"Error enabling wireless ADB: {output} {error}".Trim());
    }

    public async Task<int> GetCurrentRefreshRateAsync(string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return 72;

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, _) = await ExecuteAdbCommandAsync($"{target}shell dumpsys display", ct, timeoutMs: 10000);

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            var match = Regex.Match(output, @"renderFrameRate\s*([0-9\.]+)");
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double fps))
            {
                return (int)Math.Round(fps);
            }
        }

        var (pCode, pOut, _) = await ExecuteAdbCommandAsync($"{target}shell getprop debug.oculus.refreshRate", ct);
        if (pCode == 0 && int.TryParse(pOut.Trim(), out int propHz) && propHz > 0)
        {
            return propHz;
        }

        return 72;
    }

    public async Task<AdbOperationResult> SetRefreshRateAsync(int refreshRateHz, string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        var (exitCode, output, error) = await ExecuteAdbCommandAsync($"{target}shell setprop debug.oculus.refreshRate {refreshRateHz}", ct);

        if (exitCode == 0)
        {
            return new AdbOperationResult(true, $"Display refresh rate set to {refreshRateHz} Hz.");
        }

        return new AdbOperationResult(false, $"Error setting refresh rate: {error}".Trim());
    }

    public async Task<AdbOperationResult> TakeScreenshotAsync(string destinationPath, string? serial = null, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new AdbOperationResult(false, "ADB not available.");

        string target = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
        string remoteTmp = "/sdcard/screenshot_tmp.png";

        var (sCode, _, sErr) = await ExecuteAdbCommandAsync($"{target}shell screencap -p {remoteTmp}", ct);
        if (sCode != 0) return new AdbOperationResult(false, $"Error capturing screen: {sErr}");

        var (pCode, _, pErr) = await ExecuteAdbCommandAsync($"{target}pull {remoteTmp} \"{destinationPath}\"", ct);
        await ExecuteAdbCommandAsync($"{target}shell rm {remoteTmp}", ct);

        if (pCode == 0)
        {
            return new AdbOperationResult(true, $"Screenshot saved to: {destinationPath}");
        }

        return new AdbOperationResult(false, $"Error pulling screenshot: {pErr}");
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecuteAdbCommandAsync(string arguments, CancellationToken ct, int timeoutMs = 30000)
    {
        if (string.IsNullOrEmpty(_adbPath)) return (-1, string.Empty, "ADB path not configured");

        return await Task.Run(() =>
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetTempPath(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync(ct);
                var errorTask = process.StandardError.ReadToEndAsync(ct);

                bool finished = process.WaitForExit(timeoutMs);
                if (!finished)
                {
                    try { process.Kill(true); } catch { }
                    return (-1, string.Empty, "ADB operation timeout");
                }

                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();

                return (process.ExitCode, output, error);
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }, ct);
    }

    private static string? FindAdbExecutable()
    {
        // 0. Check local application directory and platform-tools subfolder
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] localLocations =
            [
                Path.Combine(baseDir, "adb.exe"),
                Path.Combine(baseDir, "platform-tools", "adb.exe")
            ];
            foreach (var loc in localLocations)
            {
                if (File.Exists(loc)) return loc;
            }
        }
        catch { }

        // 1. Check system PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            try
            {
                var candidate = Path.Combine(p.Trim(), "adb.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] commonLocations =
        [
            @"C:\Android\platform-tools\adb.exe",
            Path.Combine(localAppData, @"Android\Sdk\platform-tools\adb.exe"),
            Path.Combine(programFiles, @"Meta Horizon\Support\oculus-diagnostics\adb.exe"),
            Path.Combine(programFiles, @"Oculus\Support\oculus-diagnostics\adb.exe"),
            Path.Combine(localAppData, @"Programs\SideQuest\resources\app.asar.unpacked\build\platform-tools\adb.exe"),
            Path.Combine(localAppData, @"Programs\Meta Quest Developer Hub\resources\bin\adb.exe")
        ];

        foreach (var loc in commonLocations)
        {
            if (File.Exists(loc)) return loc;
        }

        return null;
    }

    public void Shutdown()
    {
        try
        {
            if (IsRecordingVideo)
            {
                try { _recordingProcess?.Kill(true); } catch { }
                _recordingProcess?.Dispose();
                _recordingProcess = null;
            }
        }
        catch { }

        try
        {
            if (!string.IsNullOrEmpty(_adbPath) && File.Exists(_adbPath))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "kill-server",
                    WorkingDirectory = Path.GetTempPath(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(3000);
            }
        }
        catch { }

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            foreach (var proc in Process.GetProcessesByName("adb"))
            {
                try
                {
                    if (proc.MainModule?.FileName.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        proc.Kill(true);
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
