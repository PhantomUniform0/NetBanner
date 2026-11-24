using System.Diagnostics;
using System.Security;
using System.Text;

namespace NetBanner.Windows;

/// <summary>
/// Manages the logon scheduled task that starts the banner for every user (installer helpers).
/// Uses task XML rather than plain <c>schtasks /Create</c> switches, because those can't set a group
/// principal or remove the default 72-hour execution limit (which would kill the banner after 3 days).
/// </summary>
public static class TaskRegistration
{
    public const string TaskName = @"\NetBanner\NetBannerLogon";

    private static string SchTasks => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    public static string BuildTaskXml(string exePath) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>NetBanner</Author>
            <Description>Starts the NetBanner classification banner when any user logs on.</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Users">
              <GroupId>S-1-5-32-545</GroupId>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
            <RestartOnFailure>
              <Interval>PT1M</Interval>
              <Count>999</Count>
            </RestartOnFailure>
          </Settings>
          <Actions Context="Users">
            <Exec>
              <Command>"{SecurityElement.Escape(exePath)}"</Command>
            </Exec>
          </Actions>
        </Task>
        """;

    /// <summary>Creates or replaces the logon task for <paramref name="exePath"/>.</summary>
    public static (int ExitCode, string Output) Register(string exePath)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"NetBannerTask-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exePath), Encoding.Unicode);
            return Run("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    public static (int ExitCode, string Output) Unregister() => Run("/Delete", "/TN", TaskName, "/F");

    // ------------------------------------------------------------------ NetBanner server sync (SYSTEM)

    public const string SyncTaskName = @"\NetBanner\NetBannerServerSync";

    /// <summary>
    /// SYSTEM task that runs <c>NetBanner.exe --server-sync</c> from boot: fetches this machine's config from the
    /// NetBanner server (if config.json points at one) into the admin-only config folder. Idles otherwise.
    /// </summary>
    public static string BuildSyncTaskXml(string exePath) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>NetBanner</Author>
            <Description>Keeps this machine's NetBanner configuration in sync with the NetBanner server.</Description>
          </RegistrationInfo>
          <Triggers>
            <BootTrigger>
              <Enabled>true</Enabled>
            </BootTrigger>
          </Triggers>
          <Principals>
            <Principal id="System">
              <UserId>S-1-5-18</UserId>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
            <RestartOnFailure>
              <Interval>PT1M</Interval>
              <Count>999</Count>
            </RestartOnFailure>
          </Settings>
          <Actions Context="System">
            <Exec>
              <Command>"{SecurityElement.Escape(exePath)}"</Command>
              <Arguments>--server-sync</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    public static (int ExitCode, string Output) RegisterSync(string exePath)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"NetBannerSyncTask-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildSyncTaskXml(exePath), Encoding.Unicode);
            return Run("/Create", "/TN", SyncTaskName, "/XML", xmlPath, "/F");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    public static (int ExitCode, string Output) UnregisterSync() => Run("/Delete", "/TN", SyncTaskName, "/F");

    /// <summary>Starts (or restarts) the sync task now, e.g. after install or after the server URL changes.</summary>
    public static (int ExitCode, string Output) StartSync()
    {
        Run("/End", "/TN", SyncTaskName);
        return Run("/Run", "/TN", SyncTaskName);
    }

    /// <summary>
    /// Removes legacy NetBanner tasks so the old and new banners don't both start at logon. The legacy installer's task
    /// name varies (e.g. <c>\Microsoft\NetBannerSchTask</c>), so this deletes every task that runs a NetBanner.exe other
    /// than <paramref name="ownExePath"/>.
    /// </summary>
    public static (int ExitCode, string Output) UnregisterLegacy(string ownExePath)
    {
        var (code, xml) = Run("/Query", "/XML", "ONE");
        if (code != 0)
        {
            return (code, "Could not list scheduled tasks: " + xml);
        }

        var names = Core.LegacyTasks.Find(xml, ownExePath);
        if (names.Count == 0)
        {
            return (0, "No legacy NetBanner task found.");
        }

        var results = new List<string>();
        int worst = 0;
        foreach (var name in names)
        {
            var (c, o) = Run("/Delete", "/TN", name, "/F");
            worst = c != 0 ? c : worst;
            results.Add($"{name}: {(c == 0 ? "deleted" : o)}");
        }

        return (worst, "Legacy tasks: " + string.Join("; ", results));
    }


    /// <summary>Starts the task now, for users already logged on (best effort).</summary>
    public static (int ExitCode, string Output) Start() => Run("/Run", "/TN", TaskName);

    private static (int, string) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(SchTasks)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output.Trim());
    }
}
