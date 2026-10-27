using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace Quest_Companion.Services;

[ComImport, Guid("870c3566-037f-4446-ab30-e590937c1403")]
internal class PolicyConfigClient { }

[Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat(string pszDeviceName, int bDefault, IntPtr ppFormat);
    [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
    [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
    [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
    [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
    [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr pMode);
    [PreserveSig] int GetPropertyValue(string pszDeviceName, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetPropertyValue(string pszDeviceName, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint(string pszDeviceName, int role);
    [PreserveSig] int SetEndpointVisibility(string pszDeviceName, int bVisible);
}

public class AudioService : IAudioService
{
    private AudioDeviceState _state = new("Unknown", false, "Unknown", false);

    public AudioDeviceState CurrentState => _state;

    public AudioService()
    {
        _ = RefreshDevicesAsync();
    }

    public Task<AudioDeviceState> RefreshDevicesAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();

                string currentPlayback = "None";
                bool isQuestPlayback = false;

                try
                {
                    var defaultPlayback = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    currentPlayback = defaultPlayback.FriendlyName;
                    isQuestPlayback = IsQuestDevice(currentPlayback);
                }
                catch { }

                string currentCapture = "None";
                bool isQuestCapture = false;

                try
                {
                    var defaultCapture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    currentCapture = defaultCapture.FriendlyName;
                    isQuestCapture = IsQuestDevice(currentCapture);
                }
                catch { }

                _state = new AudioDeviceState(currentPlayback, isQuestPlayback, currentCapture, isQuestCapture);
                return _state;
            }
            catch
            {
                return _state;
            }
        });
    }

    public Task<bool> SetPlaybackToQuestAsync(bool toQuest)
    {
        return Task.Run(() => SwitchEndpoint(DataFlow.Render, toQuest));
    }

    public Task<bool> SetCaptureToQuestAsync(bool toQuest)
    {
        return Task.Run(() => SwitchEndpoint(DataFlow.Capture, toQuest));
    }

    private bool SwitchEndpoint(DataFlow flow, bool targetQuest)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);

            MMDevice? targetDevice = null;

            foreach (var dev in devices)
            {
                bool isQuest = IsQuestDevice(dev.FriendlyName);
                if (targetQuest && isQuest)
                {
                    targetDevice = dev;
                    break;
                }
                else if (!targetQuest && !isQuest)
                {
                    targetDevice ??= dev;
                    if (dev.FriendlyName.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                        dev.FriendlyName.Contains("Speakers", StringComparison.OrdinalIgnoreCase) ||
                        dev.FriendlyName.Contains("Głośniki", StringComparison.OrdinalIgnoreCase))
                    {
                        targetDevice = dev;
                    }
                }
            }

            if (targetDevice != null)
            {
                var policyConfig = (IPolicyConfig)new PolicyConfigClient();
                policyConfig.SetDefaultEndpoint(targetDevice.ID, 0); // eConsole
                policyConfig.SetDefaultEndpoint(targetDevice.ID, 1); // eMultimedia
                policyConfig.SetDefaultEndpoint(targetDevice.ID, 2); // eCommunications

                _ = RefreshDevicesAsync();
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsQuestDevice(string name)
    {
        return name.Contains("Oculus", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Quest", StringComparison.OrdinalIgnoreCase);
    }
}
