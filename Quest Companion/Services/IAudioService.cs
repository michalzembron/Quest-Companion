namespace Quest_Companion.Services;

public record AudioDeviceState(
    string CurrentPlaybackName,
    bool IsQuestPlaybackActive,
    string CurrentCaptureName,
    bool IsQuestCaptureActive
);

public interface IAudioService
{
    AudioDeviceState CurrentState { get; }
    
    Task<AudioDeviceState> RefreshDevicesAsync();
    Task<bool> SetPlaybackToQuestAsync(bool toQuest);
    Task<bool> SetCaptureToQuestAsync(bool toQuest);
}
