using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;

namespace Quest_Companion.Services;

public class OvrService : IOvrService
{
    public const string ServiceName = "OVRService";

    private static readonly string[] RelatedProcesses =
    [
        "OVRServer_x64",
        "OVRRedir",
        "OVRServiceLauncher"
    ];

    public bool IsInstalled => CheckIfInstalled();

    public OvrServiceState CurrentState => GetServiceStatus();

    public bool IsMetaClientRunning => Process.GetProcessesByName("Client").Length > 0 || Process.GetProcessesByName("OculusClient").Length > 0;

    public string? FindMetaClientPath()
    {
        string[] candidates =
        [
            @"C:\Program Files\Meta Horizon\Support\oculus-client\Client.exe",
            @"C:\Program Files\Oculus\Support\oculus-client\OculusClient.exe",
            @"C:\Program Files\Oculus\Support\oculus-client\Client.exe"
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    public string? FindMqdhPath()
    {
        string[] candidates =
        [
            @"C:\Program Files\Meta Quest Developer Hub\Meta Quest Developer Hub.exe",
            @"C:\Program Files\Oculus Developer Hub\Oculus Developer Hub.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Meta Quest Developer Hub\Meta Quest Developer Hub.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Oculus Developer Hub\Oculus Developer Hub.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    public Task<OvrOperationResult> RestartMetaClientAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                foreach (var name in new[] { "Client", "OculusClient" })
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    }
                }

                Thread.Sleep(500);

                string? clientPath = FindMetaClientPath();
                if (string.IsNullOrEmpty(clientPath))
                {
                    return new OvrOperationResult(false, "Meta Horizon Client (Client.exe) not found.");
                }

                Process.Start(new ProcessStartInfo { FileName = clientPath, UseShellExecute = true });
                return new OvrOperationResult(true, "Meta Horizon Client restarted successfully.");
            }
            catch (Exception ex)
            {
                return new OvrOperationResult(false, $"Error restarting Meta Client: {ex.Message}");
            }
        }, cancellationToken);
    }

    public Task<OvrOperationResult> OpenMetaHorizonLinkAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                string? clientPath = FindMetaClientPath();
                if (string.IsNullOrEmpty(clientPath))
                {
                    return new OvrOperationResult(false, "Meta Horizon Client not found.");
                }

                Process.Start(new ProcessStartInfo { FileName = clientPath, UseShellExecute = true });
                return new OvrOperationResult(true, "Opened Meta Horizon Link.");
            }
            catch (Exception ex)
            {
                return new OvrOperationResult(false, $"Error opening Meta Horizon Link: {ex.Message}");
            }
        });
    }

    public Task<OvrOperationResult> OpenDeveloperHubAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                string? mqdhPath = FindMqdhPath();
                if (string.IsNullOrEmpty(mqdhPath))
                {
                    return new OvrOperationResult(false, "Meta Quest Developer Hub (MQDH) is not installed.");
                }

                Process.Start(new ProcessStartInfo { FileName = mqdhPath, UseShellExecute = true });
                return new OvrOperationResult(true, "Opened Meta Quest Developer Hub.");
            }
            catch (Exception ex)
            {
                return new OvrOperationResult(false, $"Error opening Developer Hub: {ex.Message}");
            }
        });
    }

    public async Task<OvrOperationResult> FullRestartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var name in new[] { "Client", "OculusClient" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }
            }
        }

        var ovrRes = await RestartAsync(cancellationToken);
        if (!ovrRes.Success)
        {
            return ovrRes;
        }

        await Task.Delay(1500, cancellationToken);

        string? clientPath = FindMetaClientPath();
        if (!string.IsNullOrEmpty(clientPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = clientPath, UseShellExecute = true });
            }
            catch { }
        }

        return new OvrOperationResult(true, "Full PC restart (OVRService + Client) completed successfully.");
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public Task<OvrServiceState> RefreshStatusAsync()
    {
        return Task.Run(GetServiceStatus);
    }

    public Task<OvrOperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (IsAdministrator())
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        return new OvrOperationResult(true, "OVRService is already running.");
                    }

                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    return new OvrOperationResult(true, "Successfully started OVRService.");
                }
                catch (Exception ex)
                {
                    return new OvrOperationResult(false, $"Error starting OVRService: {ex.Message}");
                }
            }

            // Fallback with elevation (UAC prompt only for this action)
            return RunElevatedCommand($"net start {ServiceName}");
        }, cancellationToken);
    }

    public Task<OvrOperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (IsAdministrator())
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        try
                        {
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                        }
                        catch
                        {
                            KillRelatedProcesses();
                        }
                    }

                    KillRelatedProcesses();
                    return new OvrOperationResult(true, "Successfully stopped OVRService.");
                }
                catch (Exception ex)
                {
                    return new OvrOperationResult(false, $"Error stopping OVRService: {ex.Message}");
                }
            }

            KillRelatedProcesses();
            return RunElevatedCommand($"net stop {ServiceName}");
        }, cancellationToken);
    }

    public Task<OvrOperationResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            KillRelatedProcesses();

            if (IsAdministrator())
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);

                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        try
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                        }
                        catch { }
                    }

                    KillRelatedProcesses();
                    Thread.Sleep(500);

                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));

                    return new OvrOperationResult(true, "OVRService was successfully restarted.");
                }
                catch (Exception ex)
                {
                    return new OvrOperationResult(false, $"Error restarting OVRService: {ex.Message}");
                }
            }

            return RunElevatedCommand($"net stop {ServiceName} & net start {ServiceName}");
        }, cancellationToken);
    }

    private static OvrOperationResult RunElevatedCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi);
            proc?.WaitForExit(20000);

            if (proc != null && proc.ExitCode == 0)
            {
                return new OvrOperationResult(true, "Command executed successfully with elevated privileges.");
            }

            return new OvrOperationResult(false, "Operation was cancelled or failed.");
        }
        catch (Exception ex)
        {
            return new OvrOperationResult(false, $"Elevation error: {ex.Message}");
        }
    }

    private static bool CheckIfInstalled()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            _ = sc.Status;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static OvrServiceState GetServiceStatus()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status switch
            {
                ServiceControllerStatus.Running => OvrServiceState.Running,
                ServiceControllerStatus.Stopped => OvrServiceState.Stopped,
                ServiceControllerStatus.StartPending => OvrServiceState.StartPending,
                ServiceControllerStatus.StopPending => OvrServiceState.StopPending,
                ServiceControllerStatus.ContinuePending => OvrServiceState.ContinuePending,
                ServiceControllerStatus.PausePending => OvrServiceState.PausePending,
                ServiceControllerStatus.Paused => OvrServiceState.Paused,
                _ => OvrServiceState.Unknown
            };
        }
        catch (InvalidOperationException)
        {
            return OvrServiceState.NotInstalled;
        }
        catch
        {
            return OvrServiceState.Unknown;
        }
    }

    private static void KillRelatedProcesses()
    {
        foreach (var procName in RelatedProcesses)
        {
            try
            {
                var processes = Process.GetProcessesByName(procName);
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(2000);
                        }
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch { }
        }
    }
}
