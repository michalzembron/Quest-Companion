using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quest_Companion.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Quest_Companion.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IOvrService _ovrService;
    private readonly IAdbService _adbService;
    private readonly IAudioService _audioService;
    private readonly IBitrateService _bitrateService;
    private readonly StartupService _startupService;
    private readonly DispatcherTimer _pollTimer;

    // --- Window Navigation (Sidebar Tabs) ---
    [ObservableProperty]
    private int _selectedTabIndex = 0;

    // --- OVRService (PC Meta Quest Link Runtime) ---
    [ObservableProperty]
    private string _ovrStatusText = "Checking...";

    [ObservableProperty]
    private OvrServiceState _ovrState = OvrServiceState.Unknown;

    // --- Headset Status (ADB) ---
    [ObservableProperty]
    private bool _isHeadsetConnected;

    [ObservableProperty]
    private string _headsetModel = "Not Connected";

    [ObservableProperty]
    private string _headsetSerial = string.Empty;

    [ObservableProperty]
    private bool _isHeadsetAuthorized = true;

    // --- Battery ---
    [ObservableProperty]
    private int _batteryLevel;

    [ObservableProperty]
    private bool _isBatteryCharging;

    [ObservableProperty]
    private string _batteryStatusText = "--";

    [ObservableProperty]
    private float _batteryTemperature;

    // --- Proximity Sensor / Always Awake ---
    [ObservableProperty]
    private bool _isProximityBypassed;

    [ObservableProperty]
    private bool _isStayAwakeUsbEnabled;

    [ObservableProperty]
    private bool _isBoundaryEnabled = true;

    // --- Video Recording ---
    [ObservableProperty]
    private bool _isRecordingVideo;

    [ObservableProperty]
    private string _recordVideoButtonText = "Record";

    [ObservableProperty]
    private string _recordVideoStatusText = "Ready to record";

    [ObservableProperty]
    private string _wifiIpAddress = "--";

    // --- Meta Horizon Desktop Client ---
    [ObservableProperty]
    private bool _isMetaClientRunning;

    [ObservableProperty]
    private string _metaClientStatusText = "Checking...";

    public string MetaClientPillBackground => IsMetaClientRunning ? "#163828" : "#451A1A";
    public string MetaClientPillForeground => IsMetaClientRunning ? "#4ADE80" : "#F87171";
    public string MetaClientPillBorder => IsMetaClientRunning ? "#226140" : "#7F1D1D";

    // --- Current Refresh Rate (72 / 80 / 90 / 120 Hz) ---
    [ObservableProperty]
    private int _currentRefreshRate = 72;

    // --- Meta Quest Link State on Headset ---
    [ObservableProperty]
    private bool _isQuestLinkRunning;

    // --- Audio Endpoints ---
    [ObservableProperty]
    private bool _isQuestAudioOutput;

    [ObservableProperty]
    private bool _isQuestMicrophone;

    [ObservableProperty]
    private string _currentAudioOutputName = "Checking...";

    [ObservableProperty]
    private string _currentMicrophoneName = "Checking...";

    // --- Link Bitrate ---
    [ObservableProperty]
    private int _currentBitrateMbps;

    [ObservableProperty]
    private string _customBitrateText = "500";

    [ObservableProperty]
    private string _bitrateStatusText = string.Empty;

    [ObservableProperty]
    private string _linkConnectionMode = "Detecting...";

    [ObservableProperty]
    private string _linkConnectionWarning = string.Empty;

    [ObservableProperty]
    private bool _isUsb2Mode;

    [ObservableProperty]
    private bool _hasLinkConnectionWarning;

    public IReadOnlyList<BitratePreset> BitratePresets => _bitrateService.Presets;

    // --- Autostart & System ---
    [ObservableProperty]
    private bool _isAutoStartEnabled;

    // --- Sideload / Package Installer ---
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private string _selectedInstallPath = string.Empty;

    [ObservableProperty]
    private string _installItemDescription = string.Empty;

    [ObservableProperty]
    private bool _isInstalling;

    public bool HasSelectedInstallItem => !string.IsNullOrEmpty(SelectedInstallPath);

    // ==========================================
    // MODERN PILL BADGES
    // ==========================================

    // OVR Service Pill
    public string OvrPillBackground => OvrState switch
    {
        OvrServiceState.Running => "#163828",
        OvrServiceState.Stopped => "#3D1B1E",
        OvrServiceState.StartPending or OvrServiceState.StopPending => "#3B2810",
        _ => "#22242A"
    };

    public string OvrPillForeground => OvrState switch
    {
        OvrServiceState.Running => "#4ADE80",
        OvrServiceState.Stopped => "#F87171",
        OvrServiceState.StartPending or OvrServiceState.StopPending => "#FBBF24",
        _ => "#9CA3AF"
    };

    public string OvrPillBorder => OvrState switch
    {
        OvrServiceState.Running => "#226140",
        OvrServiceState.Stopped => "#6E282D",
        OvrServiceState.StartPending or OvrServiceState.StopPending => "#6D4916",
        _ => "#343842"
    };

    // Battery Pill
    public string BatteryPillBackground => !IsHeadsetConnected ? "#22242A" : BatteryLevel <= 20 && !IsBatteryCharging ? "#3D1B1E" : "#163828";
    public string BatteryPillForeground => !IsHeadsetConnected ? "#9CA3AF" : BatteryLevel <= 20 && !IsBatteryCharging ? "#F87171" : "#4ADE80";
    public string BatteryPillBorder => !IsHeadsetConnected ? "#343842" : BatteryLevel <= 20 && !IsBatteryCharging ? "#6E282D" : "#226140";

    // MQDH Solid Connected Badge
    public string HeadsetConnectionStatusText => IsHeadsetConnected ? "Connected" : "Disconnected";
    public string HeadsetConnectionBadgeBackground => IsHeadsetConnected ? "#22C55E" : "#383B44";
    public string HeadsetConnectionBadgeForeground => IsHeadsetConnected ? "#FFFFFF" : "#9CA3AF";

    // Quest Link Pill & Button
    public string LinkPillBackground => IsQuestLinkRunning ? "#163828" : "#22242A";
    public string LinkPillForeground => IsQuestLinkRunning ? "#4ADE80" : "#9CA3AF";
    public string LinkPillBorder => IsQuestLinkRunning ? "#226140" : "#343842";
    public string LinkStatusText => IsQuestLinkRunning ? "Active" : "Disconnected";
    public string QuestLinkButtonText => IsQuestLinkRunning ? "Disconnect Link" : "Start Quest Link";
    public string QuestLinkButtonIcon => IsQuestLinkRunning ? "\ue047" : "\ue157";

    // Audio Output Pill
    public string AudioOutputPillBackground => IsQuestAudioOutput ? "#163828" : "#22242A";
    public string AudioOutputPillForeground => IsQuestAudioOutput ? "#4ADE80" : "#9CA3AF";
    public string AudioOutputPillBorder => IsQuestAudioOutput ? "#226140" : "#343842";

    // Microphone Pill
    public string MicPillBackground => IsQuestMicrophone ? "#163828" : "#22242A";
    public string MicPillForeground => IsQuestMicrophone ? "#4ADE80" : "#9CA3AF";
    public string MicPillBorder => IsQuestMicrophone ? "#226140" : "#343842";

    public string BatteryDisplay => IsHeadsetConnected
        ? $"{BatteryLevel}% ({(IsBatteryCharging ? "⚡ Charging" : "Battery")})"
        : "Disconnected";

    public string BatterySubtext => IsHeadsetConnected
        ? $"{BatteryStatusText} • {BatteryTemperature:F1}°C"
        : "Connect headset via USB cable";

    public string BatteryDetailedStatus => IsHeadsetConnected
        ? $"{BatteryLevel}% [{BatteryStatusText}] | Temp: {BatteryTemperature:F1}°C"
        : "Not Connected";

    public string AdbDeviceDisplay => IsHeadsetConnected
        ? $"{HeadsetModel} ({HeadsetSerial}) [device]"
        : "No ADB device detected";

    public string BitrateDisplay => CurrentBitrateMbps == 0
        ? "Dynamic (Meta Default)"
        : $"{CurrentBitrateMbps} Mbps";

    public string TrayToolTipText
    {
        get
        {
            string headsetPart = IsHeadsetConnected
                ? $"{HeadsetModel}: {BatteryLevel}%{(IsBatteryCharging ? "⚡" : "")}"
                : "Headset disconnected";
            return $"Quest Companion\n{headsetPart}\nOVR: {OvrStatusText}";
        }
    }

    public bool CanRestart => !IsBusy && OvrState != OvrServiceState.NotInstalled;
    public bool CanStart => !IsBusy && OvrState == OvrServiceState.Stopped;
    public bool CanStop => !IsBusy && OvrState == OvrServiceState.Running;

    public event Action<string, string>? RequestNotification;
    public event Action? RequestToggleWindow;
    public event Action? RequestExit;

    public MainViewModel(
        IOvrService ovrService,
        IAdbService adbService,
        IAudioService audioService,
        IBitrateService bitrateService,
        StartupService startupService)
    {
        _ovrService = ovrService;
        _adbService = adbService;
        _audioService = audioService;
        _bitrateService = bitrateService;
        _startupService = startupService;

        UpdateOvrState(ovrService.CurrentState);

        CurrentBitrateMbps = _bitrateService.GetCurrentBitrate();
        CustomBitrateText = CurrentBitrateMbps > 0 ? CurrentBitrateMbps.ToString() : "500";
        IsAutoStartEnabled = _startupService.IsAutoStartEnabled();

        var initConn = _bitrateService.GetConnectionDetail();
        LinkConnectionMode = initConn.Mode;
        LinkConnectionWarning = initConn.Warning;
        IsUsb2Mode = initConn.IsUsb2;
        HasLinkConnectionWarning = !string.IsNullOrEmpty(initConn.Warning);

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += async (_, _) => await PollDeviceStatusAsync();
        _pollTimer.Start();

        _ = Task.Run(async () =>
        {
            await _adbService.InitializeAsync();
            await RefreshAllAsync();
        });
    }

    public MainViewModel() : this(
        new OvrService(),
        new AdbService(),
        new AudioService(),
        new BitrateService(),
        new StartupService())
    {
    }

    partial void OnOvrStateChanged(OvrServiceState value)
    {
        OnPropertyChanged(nameof(OvrPillBackground));
        OnPropertyChanged(nameof(OvrPillForeground));
        OnPropertyChanged(nameof(OvrPillBorder));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(TrayToolTipText));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
    }

    partial void OnSelectedInstallPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedInstallItem));
    }

    partial void OnCurrentBitrateMbpsChanged(int value)
    {
        OnPropertyChanged(nameof(BitrateDisplay));
    }

    partial void OnIsQuestAudioOutputChanged(bool value)
    {
        OnPropertyChanged(nameof(AudioOutputPillBackground));
        OnPropertyChanged(nameof(AudioOutputPillForeground));
        OnPropertyChanged(nameof(AudioOutputPillBorder));
    }

    partial void OnIsQuestMicrophoneChanged(bool value)
    {
        OnPropertyChanged(nameof(MicPillBackground));
        OnPropertyChanged(nameof(MicPillForeground));
        OnPropertyChanged(nameof(MicPillBorder));
    }

    partial void OnBatteryLevelChanged(int value)
    {
        OnPropertyChanged(nameof(BatteryPillBackground));
        OnPropertyChanged(nameof(BatteryPillForeground));
        OnPropertyChanged(nameof(BatteryPillBorder));
        OnPropertyChanged(nameof(BatteryDisplay));
        OnPropertyChanged(nameof(BatterySubtext));
        OnPropertyChanged(nameof(TrayToolTipText));
    }

    partial void OnIsBatteryChargingChanged(bool value)
    {
        OnPropertyChanged(nameof(BatteryPillBackground));
        OnPropertyChanged(nameof(BatteryPillForeground));
        OnPropertyChanged(nameof(BatteryPillBorder));
        OnPropertyChanged(nameof(BatteryDisplay));
        OnPropertyChanged(nameof(BatterySubtext));
        OnPropertyChanged(nameof(BatteryDetailedStatus));
        OnPropertyChanged(nameof(TrayToolTipText));
    }

    partial void OnIsHeadsetConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(BatteryPillBackground));
        OnPropertyChanged(nameof(BatteryPillForeground));
        OnPropertyChanged(nameof(BatteryPillBorder));
        OnPropertyChanged(nameof(HeadsetConnectionStatusText));
        OnPropertyChanged(nameof(HeadsetConnectionBadgeBackground));
        OnPropertyChanged(nameof(HeadsetConnectionBadgeForeground));
        OnPropertyChanged(nameof(BatteryDisplay));
        OnPropertyChanged(nameof(BatterySubtext));
        OnPropertyChanged(nameof(BatteryDetailedStatus));
        OnPropertyChanged(nameof(AdbDeviceDisplay));
        OnPropertyChanged(nameof(TrayToolTipText));
    }

    partial void OnIsQuestLinkRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(LinkPillBackground));
        OnPropertyChanged(nameof(LinkPillForeground));
        OnPropertyChanged(nameof(LinkPillBorder));
        OnPropertyChanged(nameof(LinkStatusText));
        OnPropertyChanged(nameof(QuestLinkButtonText));
        OnPropertyChanged(nameof(QuestLinkButtonIcon));
    }

    public async Task PollDeviceStatusAsync()
    {
        if (IsBusy) return;

        try
        {
            var device = await _adbService.GetConnectedDeviceAsync();
            if (device != null)
            {
                IsHeadsetConnected = true;
                HeadsetModel = device.Model;
                HeadsetSerial = device.Serial;
                IsHeadsetAuthorized = device.IsAuthorized;

                if (device.IsAuthorized)
                {
                    var battery = await _adbService.GetBatteryInfoAsync(device.Serial);
                    if (battery != null)
                    {
                        BatteryLevel = battery.Level;
                        IsBatteryCharging = battery.IsCharging;
                        BatteryStatusText = battery.StatusText;
                        BatteryTemperature = battery.TemperatureCelsius;
                    }

                    IsQuestLinkRunning = await _adbService.IsQuestLinkRunningAsync(device.Serial);
                    CurrentRefreshRate = await _adbService.GetCurrentRefreshRateAsync(device.Serial);
                    WifiIpAddress = await _adbService.GetWifiIpAddressAsync(device.Serial) ?? "--";
                    IsStayAwakeUsbEnabled = await _adbService.GetStayAwakeUsbAsync(device.Serial);
                    IsBoundaryEnabled = await _adbService.GetBoundaryEnabledAsync(device.Serial);

                    OnPropertyChanged(nameof(BatteryDetailedStatus));
                    OnPropertyChanged(nameof(AdbDeviceDisplay));
                }
            }
            else
            {
                IsHeadsetConnected = false;
                HeadsetModel = "Not Connected";
                HeadsetSerial = string.Empty;
                BatteryLevel = 0;
                IsBatteryCharging = false;
                BatteryStatusText = "--";
                WifiIpAddress = "--";
                IsQuestLinkRunning = false;
                IsStayAwakeUsbEnabled = false;
                IsProximityBypassed = false;

                OnPropertyChanged(nameof(BatteryDetailedStatus));
                OnPropertyChanged(nameof(AdbDeviceDisplay));
            }

            IsMetaClientRunning = _ovrService.IsMetaClientRunning;
            MetaClientStatusText = IsMetaClientRunning ? "Running" : "Stopped";
            OnPropertyChanged(nameof(MetaClientPillBackground));
            OnPropertyChanged(nameof(MetaClientPillForeground));
            OnPropertyChanged(nameof(MetaClientPillBorder));

            var audio = await _audioService.RefreshDevicesAsync();
            CurrentAudioOutputName = audio.CurrentPlaybackName;
            IsQuestAudioOutput = audio.IsQuestPlaybackActive;
            CurrentMicrophoneName = audio.CurrentCaptureName;
            IsQuestMicrophone = audio.IsQuestCaptureActive;

            var conn = _bitrateService.GetConnectionDetail();
            LinkConnectionMode = conn.Mode;
            LinkConnectionWarning = conn.Warning;
            IsUsb2Mode = conn.IsUsb2;
            HasLinkConnectionWarning = !string.IsNullOrEmpty(conn.Warning);
        }
        catch { }
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        var ovrState = await _ovrService.RefreshStatusAsync();
        UpdateOvrState(ovrState);
        CurrentBitrateMbps = _bitrateService.GetCurrentBitrate();
        await PollDeviceStatusAsync();
    }

    // --- NAVIGATION TABS ---
    [RelayCommand]
    public void SelectTab(string indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
        {
            SelectedTabIndex = idx;
        }
    }

    // --- AUDIO ACTIONS ---
    [RelayCommand]
    public async Task ToggleAudioOutputAsync()
    {
        bool target = !IsQuestAudioOutput;
        StatusMessage = target ? "Switching audio to Quest Headset..." : "Switching audio to PC Speakers...";
        bool ok = await _audioService.SetPlaybackToQuestAsync(target);
        if (ok)
        {
            await PollDeviceStatusAsync();
            StatusMessage = target ? "Audio playback routed to Quest Headset." : "Audio playback routed to PC Speakers.";
        }
    }

    [RelayCommand]
    public async Task ToggleMicrophoneAsync()
    {
        bool target = !IsQuestMicrophone;
        StatusMessage = target ? "Switching microphone to Quest Headset..." : "Switching microphone to PC Microphone...";
        bool ok = await _audioService.SetCaptureToQuestAsync(target);
        if (ok)
        {
            await PollDeviceStatusAsync();
            StatusMessage = target ? "Quest microphone is now default." : "PC microphone is now default.";
        }
    }

    // --- BITRATE OPTIMIZER ---
    [RelayCommand]
    public void SetPresetBitrate(string mbpsStr)
    {
        if (int.TryParse(mbpsStr, out int bitrate))
        {
            bool ok = _bitrateService.SetBitrate(bitrate);
            if (ok)
            {
                CurrentBitrateMbps = bitrate;
                CustomBitrateText = bitrate.ToString();
                BitrateStatusText = $"Applied profile: {bitrate} Mbps.";
                StatusMessage = BitrateStatusText;
                AskToRestartOvrForBitrate();
            }
        }
    }

    [RelayCommand]
    public void ApplyCustomBitrate()
    {
        if (int.TryParse(CustomBitrateText, out int bitrate) && bitrate >= 0 && bitrate <= 1200)
        {
            bool ok = _bitrateService.SetBitrate(bitrate);
            if (ok)
            {
                CurrentBitrateMbps = bitrate;
                BitrateStatusText = $"Set custom bitrate: {bitrate} Mbps.";
                StatusMessage = BitrateStatusText;
                AskToRestartOvrForBitrate();
            }
        }
        else
        {
            MessageBox.Show("Please enter a valid bitrate (0 - 1200 Mbps).", "Invalid Bitrate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    public void AutoDetectBitrate()
    {
        var (recBitrate, reason) = _bitrateService.AutoDetectOptimalBitrate(IsHeadsetConnected, isUsbCable: true);
        CustomBitrateText = recBitrate.ToString();
        bool ok = _bitrateService.SetBitrate(recBitrate);
        if (ok)
        {
            CurrentBitrateMbps = recBitrate;
            BitrateStatusText = $"Auto-detected: {reason}";
            StatusMessage = BitrateStatusText;
            AskToRestartOvrForBitrate();
        }
    }

    private void AskToRestartOvrForBitrate()
    {
        var ask = MessageBox.Show(
            "Bitrate adjustments require restarting OVRService to take effect.\n\nWould you like to restart OVRService now?",
            "Bitrate Applied",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ask == MessageBoxResult.Yes)
        {
            _ = RestartOvrServiceAsync();
        }
    }

    // --- REFRESH RATE ---
    [RelayCommand]
    public async Task SetRefreshRateAsync(string hzStr)
    {
        if (!IsHeadsetConnected || !int.TryParse(hzStr, out int hz)) return;

        StatusMessage = $"Setting display refresh rate to {hz} Hz...";
        var res = await _adbService.SetRefreshRateAsync(hz, HeadsetSerial);
        if (res.Success)
        {
            CurrentRefreshRate = hz;
        }
        StatusMessage = res.Message;
        RequestNotification?.Invoke("Quest Companion", res.Message);
    }

    // --- AUTOSTART ---
    [RelayCommand]
    public void ToggleAutoStart()
    {
        bool newVal = !IsAutoStartEnabled;
        bool ok = _startupService.SetAutoStart(newVal);
        if (ok)
        {
            IsAutoStartEnabled = newVal;
            StatusMessage = newVal ? "Auto-start enabled with Windows." : "Auto-start disabled.";
        }
    }

    // --- OVRSERVICE ACTIONS ---
    [RelayCommand]
    public async Task RestartOvrServiceAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Restarting OVRService (terminating lingering processes and restarting)...";
        RequestNotification?.Invoke("Quest Companion", "Restarting OVRService...");

        try
        {
            var result = await _ovrService.RestartAsync();
            StatusMessage = result.Message;
            await RefreshAllAsync();

            RequestNotification?.Invoke(
                "Quest Companion",
                result.Success ? "OVRService restarted successfully!" : $"Error: {result.Message}"
            );
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task StartOvrServiceAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Starting OVRService...";

        try
        {
            var result = await _ovrService.StartAsync();
            StatusMessage = result.Message;
            await RefreshAllAsync();
            RequestNotification?.Invoke("Quest Companion", result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task StopOvrServiceAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Stopping OVRService...";

        try
        {
            var result = await _ovrService.StopAsync();
            StatusMessage = result.Message;
            await RefreshAllAsync();
            RequestNotification?.Invoke("Quest Companion", result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- META QUEST LINK ACTIONS ---
    [RelayCommand]
    public async Task ToggleQuestLinkAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        IsBusy = true;
        try
        {
            if (IsQuestLinkRunning)
            {
                StatusMessage = "Stopping Meta Quest Link on headset...";
                var res = await _adbService.StopQuestLinkAsync(HeadsetSerial);
                StatusMessage = res.Message;
            }
            else
            {
                StatusMessage = "Launching wired Meta Quest Link...";
                var res = await _adbService.StartQuestLinkAsync(HeadsetSerial);
                StatusMessage = res.Message;
            }

            await PollDeviceStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- PROXIMITY SENSOR BYPASS ---
    [RelayCommand]
    public async Task ToggleProximitySensorAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        IsBusy = true;
        try
        {
            bool newBypass = !IsProximityBypassed;
            StatusMessage = newBypass
                ? "Bypassing proximity sensor (headset will stay awake)..."
                : "Restoring normal proximity sensor behavior...";

            var result = await _adbService.SetProximitySensorBypassAsync(newBypass, HeadsetSerial);
            if (result.Success)
            {
                IsProximityBypassed = newBypass;
            }
            StatusMessage = result.Message;
            RequestNotification?.Invoke("Quest Companion", result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- BOUNDARY (GUARDIAN) TOGGLE ---
    [RelayCommand]
    public async Task ToggleBoundaryAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        IsBusy = true;
        try
        {
            bool newBoundary = !IsBoundaryEnabled;
            StatusMessage = newBoundary ? "Enabling Guardian boundary..." : "Pausing Guardian boundary...";

            var result = await _adbService.SetBoundaryEnabledAsync(newBoundary, HeadsetSerial);
            if (result.Success)
            {
                IsBoundaryEnabled = newBoundary;
            }
            StatusMessage = result.Message;
            RequestNotification?.Invoke("Quest Companion", result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- VIDEO RECORDING (HEADSET SCREENRECORD) ---
    [RelayCommand]
    public async Task ToggleRecordVideoAsync()
    {
        if (!IsHeadsetConnected) return;

        if (IsRecordingVideo)
        {
            StatusMessage = "Stopping recording and retrieving MP4 video from headset...";
            RecordVideoButtonText = "Saving...";
            RecordVideoStatusText = "Finalizing MP4...";

            string destFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var result = await _adbService.StopScreenRecordAsync(destFolder, HeadsetSerial);

            IsRecordingVideo = false;
            RecordVideoButtonText = "Record";
            RecordVideoStatusText = "Ready to record";

            if (result.Success)
            {
                StatusMessage = $"Saved video recording: {Path.GetFileName(result.Message)}";
                RequestNotification?.Invoke("Quest Companion", $"Video saved to Desktop: {Path.GetFileName(result.Message)}");
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
        else
        {
            var result = await _adbService.StartScreenRecordAsync(HeadsetSerial);
            if (result.Success)
            {
                IsRecordingVideo = true;
                RecordVideoButtonText = "Stop";
                RecordVideoStatusText = "Recording in progress...";
                StatusMessage = "Video recording active on headset (1080p 16Mbps). Click Stop when finished.";
                RequestNotification?.Invoke("Quest Companion", "Headset video recording started.");
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
    }

    // --- CAST DEVICE / MIRRORING ---
    [RelayCommand]
    public void StartCastDevice()
    {
        string? scrcpyPath = FindScrcpyExecutable();
        if (!string.IsNullOrEmpty(scrcpyPath))
        {
            try
            {
                string targetArg = string.IsNullOrEmpty(HeadsetSerial) ? "" : $"-s {HeadsetSerial} ";
                Process.Start(new ProcessStartInfo
                {
                    FileName = scrcpyPath,
                    Arguments = $"{targetArg}--max-size 1920 --max-fps 60 --bit-rate 16M --window-title \"Quest 3 - Mirror\"",
                    UseShellExecute = true
                });

                StatusMessage = "Launched scrcpy low-latency screen mirror.";
                return;
            }
            catch { }
        }

        try
        {
            Process.Start(new ProcessStartInfo("https://www.oculus.com/casting") { UseShellExecute = true });
            StatusMessage = "Opened Meta Quest Casting (https://www.oculus.com/casting).";
            RequestNotification?.Invoke("Meta Quest Casting", "Opened https://www.oculus.com/casting. In your headset, go to Quick Settings -> Cast to Computer.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open cast: {ex.Message}";
        }
    }

    private static string? FindScrcpyExecutable()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            try
            {
                var candidate = Path.Combine(p.Trim(), "scrcpy.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            @"C:\scrcpy\scrcpy.exe",
            Path.Combine(localAppData, @"Programs\scrcpy\scrcpy.exe"),
            Path.Combine(localAppData, @"Programs\SideQuest\resources\app.asar.unpacked\build\scrcpy\scrcpy.exe")
        ];

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }

    // --- HEADSET REBOOT & SHUTDOWN ---
    [RelayCommand]
    public async Task RebootHeadsetAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        var confirm = MessageBox.Show(
            "Are you sure you want to reboot the Meta Quest headset?",
            "Reboot Headset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusMessage = "Sending reboot signal to headset...";

        try
        {
            var res = await _adbService.RebootHeadsetAsync(HeadsetSerial);
            StatusMessage = res.Message;
            RequestNotification?.Invoke("Quest Companion", res.Message);
            await PollDeviceStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ShutdownHeadsetAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        var confirm = MessageBox.Show(
            "Are you sure you want to completely shut down the Meta Quest headset?",
            "Shut Down Headset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusMessage = "Sending shut down signal to headset...";

        try
        {
            var res = await _adbService.ShutdownHeadsetAsync(HeadsetSerial);
            StatusMessage = res.Message;
            RequestNotification?.Invoke("Quest Companion", res.Message);
            await PollDeviceStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- SIDELOAD / PACKAGE INSTALLATION ---
    [RelayCommand]
    public async Task InstallSelectedPackageAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedInstallPath))
        {
            StatusMessage = "Please select an .apk, .obb file or game folder first.";
            return;
        }

        if (!IsHeadsetConnected)
        {
            StatusMessage = "Cannot install: headset is not connected!";
            MessageBox.Show("Connect headset via USB cable and allow ADB debugging.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsInstalling = true;
        StatusMessage = $"Installing: {Path.GetFileName(SelectedInstallPath)}...";
        RequestNotification?.Invoke("Quest Companion", $"Starting installation: {Path.GetFileName(SelectedInstallPath)}");

        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await _adbService.InstallDirectoryOrFileAsync(SelectedInstallPath, progress, HeadsetSerial);

            StatusMessage = result.Message;
            RequestNotification?.Invoke(
                "Quest Companion",
                result.Success ? "Installation completed successfully!" : result.Message
            );

            if (result.Success)
            {
                SelectedInstallPath = string.Empty;
                InstallItemDescription = string.Empty;
            }
        }
        finally
        {
            IsInstalling = false;
        }
    }

    public async void HandleDroppedItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        SelectedTabIndex = 3; // Switch to Sideload Tab

        bool isApk = path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
        bool isObb = path.EndsWith(".obb", StringComparison.OrdinalIgnoreCase);
        bool isDir = Directory.Exists(path);

        if (!isApk && !isObb && !isDir)
        {
            StatusMessage = "Unsupported item format (supported: .apk, .obb, and game folders).";
            return;
        }

        SelectedInstallPath = path;
        InstallItemDescription = isApk
            ? $"Application APK: {Path.GetFileName(path)}"
            : isObb
                ? $"Expansion OBB Data: {Path.GetFileName(path)}"
                : $"Game Folder (APK/OBB): {Path.GetFileName(path)}";

        StatusMessage = $"Ready to sideload: {Path.GetFileName(path)}";

        if (IsHeadsetConnected)
        {
            var ask = MessageBox.Show(
                $"Detected {InstallItemDescription}.\n\nWould you like to install it on {HeadsetModel} now?",
                "Sideload Package",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (ask == MessageBoxResult.Yes)
            {
                await InstallSelectedPackageAsync();
            }
        }
    }

    // --- WIRELESS ADB & SCREENSHOT ---
    [RelayCommand]
    public async Task EnableWirelessAdbAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        IsBusy = true;
        StatusMessage = "Enabling wireless ADB mode (port 5555)...";
        try
        {
            var result = await _adbService.EnableWirelessAdbAsync(5555, HeadsetSerial);
            StatusMessage = result.Message;
            MessageBox.Show(result.Message, "Wireless ADB", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task TakeScreenshotAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        string dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            $"Quest_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        IsBusy = true;
        StatusMessage = "Capturing screenshot from headset...";
        try
        {
            var result = await _adbService.TakeScreenshotAsync(dest, HeadsetSerial);
            StatusMessage = result.Message;
            RequestNotification?.Invoke("Quest Companion", result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CopyWifiIp()
    {
        if (!string.IsNullOrWhiteSpace(WifiIpAddress) && WifiIpAddress != "--")
        {
            Clipboard.SetText(WifiIpAddress);
            StatusMessage = $"Copied IP ({WifiIpAddress}) to clipboard.";
            RequestNotification?.Invoke("Quest Companion", $"Copied IP {WifiIpAddress} to clipboard.");
        }
    }

    [RelayCommand]
    public async Task WakeDisplayAsync()
    {
        if (!IsHeadsetConnected) return;
        var res = await _adbService.WakeDisplayAsync(HeadsetSerial);
        StatusMessage = res.Message;
    }

    [RelayCommand]
    public async Task SleepDisplayAsync()
    {
        if (!IsHeadsetConnected) return;
        var res = await _adbService.SleepDisplayAsync(HeadsetSerial);
        StatusMessage = res.Message;
    }

    [RelayCommand]
    public async Task ToggleStayAwakeUsbAsync()
    {
        if (IsBusy || !IsHeadsetConnected) return;

        bool newStayAwake = !IsStayAwakeUsbEnabled;
        StatusMessage = newStayAwake
            ? "Enabling USB Keep-Awake (headset display will stay on while plugged in)..."
            : "Disabling USB Keep-Awake (normal timeout restored)...";

        var res = await _adbService.SetStayAwakeUsbAsync(newStayAwake, HeadsetSerial);
        if (res.Success)
        {
            IsStayAwakeUsbEnabled = newStayAwake;
        }
        StatusMessage = res.Message;
        RequestNotification?.Invoke("Quest Companion", res.Message);
    }

    [RelayCommand]
    public async Task DisconnectWirelessAdbAsync()
    {
        var res = await _adbService.DisconnectWifiAdbAsync();
        StatusMessage = res.Message;
        RequestNotification?.Invoke("Quest Companion", res.Message);
    }

    [RelayCommand]
    public async Task ClearLogcatAsync()
    {
        if (!IsHeadsetConnected) return;
        var res = await _adbService.ClearLogcatAsync(HeadsetSerial);
        StatusMessage = res.Message;
        RequestNotification?.Invoke("Quest Companion", res.Message);
    }

    [RelayCommand]
    public async Task RestartMetaClientAsync()
    {
        StatusMessage = "Restarting Meta Horizon Client (Client.exe)...";
        var res = await _ovrService.RestartMetaClientAsync();
        StatusMessage = res.Message;
        RequestNotification?.Invoke("Quest Companion", res.Message);
        await PollDeviceStatusAsync();
    }

    [RelayCommand]
    public async Task OpenMetaHorizonLinkAsync()
    {
        var res = await _ovrService.OpenMetaHorizonLinkAsync();
        StatusMessage = res.Message;
    }

    [RelayCommand]
    public async Task OpenDeveloperHubAsync()
    {
        var res = await _ovrService.OpenDeveloperHubAsync();
        StatusMessage = res.Message;
    }

    [RelayCommand]
    public async Task FullPcRestartAsync()
    {
        IsBusy = true;
        StatusMessage = "Performing Full PC Restart (OVRService + Client.exe)...";
        var res = await _ovrService.FullRestartAsync();
        IsBusy = false;
        StatusMessage = res.Message;
        RequestNotification?.Invoke("Quest Companion", res.Message);
        await PollDeviceStatusAsync();
    }

    [RelayCommand]
    public void ToggleWindow()
    {
        RequestToggleWindow?.Invoke();
    }

    [RelayCommand]
    public void ExitApplication()
    {
        Shutdown();
        RequestExit?.Invoke();
    }

    public void Shutdown()
    {
        try
        {
            _adbService.Shutdown();
        }
        catch { }
    }

    private void UpdateOvrState(OvrServiceState state)
    {
        OvrState = state;
        OvrStatusText = state switch
        {
            OvrServiceState.Running => "Active (Running)",
            OvrServiceState.Stopped => "Stopped",
            OvrServiceState.StartPending => "Starting...",
            OvrServiceState.StopPending => "Stopping...",
            OvrServiceState.NotInstalled => "Meta Quest Link Not Installed",
            _ => "Unknown Status"
        };
    }
}
