using System.ServiceProcess;

namespace Quest_Companion.Services;

public enum OvrServiceState
{
    NotInstalled,
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused,
    Unknown
}

public record OvrOperationResult(bool Success, string Message);

public interface IOvrService
{
    bool IsInstalled { get; }
    OvrServiceState CurrentState { get; }
    bool IsMetaClientRunning { get; }
    
    Task<OvrOperationResult> StartAsync(CancellationToken cancellationToken = default);
    Task<OvrOperationResult> StopAsync(CancellationToken cancellationToken = default);
    Task<OvrOperationResult> RestartAsync(CancellationToken cancellationToken = default);
    Task<OvrServiceState> RefreshStatusAsync();

    string? FindMetaClientPath();
    string? FindMqdhPath();
    Task<OvrOperationResult> RestartMetaClientAsync(CancellationToken cancellationToken = default);
    Task<OvrOperationResult> OpenMetaHorizonLinkAsync();
    Task<OvrOperationResult> OpenDeveloperHubAsync();
    Task<OvrOperationResult> FullRestartAsync(CancellationToken cancellationToken = default);
}
