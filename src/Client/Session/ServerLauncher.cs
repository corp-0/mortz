using Godot;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Session;

/// <summary>"Host and play": spawns the dedicated server as a separate process;
/// in the editor it relaunches this project headless instead.</summary>
public static class ServerLauncher
{
    private static readonly ILogger _log = MortzLog.For("client");

    private static string ServerExeName =>
        OS.HasFeature("windows") ? "MortzServer.exe" : "MortzServer.x86_64";

    private static int _pid = -1;

    public static bool Spawn(int port, string adminPassword, string serverName,
        bool allowJoinInProgress = true)
    {
        string[] gameArgs =
        [
            "--server", "--port", port.ToString(),
            "--content-root", ContentRoot.Resolve(),
        ];
        if (adminPassword.Length > 0)
            gameArgs = [.. gameArgs, "--admin-password", adminPassword];
        if (serverName.Length > 0)
            gameArgs = [.. gameArgs, "--server-name", serverName];
        if (!allowJoinInProgress)
            gameArgs = [.. gameArgs, "--no-jip"];

        string exe;
        string[] args;
        if (OS.HasFeature("editor"))
        {
            exe = OS.GetExecutablePath();
            args = ["--path", ProjectSettings.GlobalizePath("res://"), "--headless", "++", .. gameArgs];
        }
        else
        {
            exe = OS.GetExecutablePath().GetBaseDir().PathJoin(ServerExeName);
            args = ["--headless", "++", .. gameArgs];
        }

        _log.Information("spawning local server: {Exe}", exe);
        _pid = OS.CreateProcess(exe, args);
        if (_pid <= 0)
        {
            _log.Error("failed to spawn server process ({Exe})", exe);
            return false;
        }
        return true;
    }

    public static void Kill()
    {
        if (_pid > 0 && OS.IsProcessRunning(_pid))
        {
            _log.Information("stopping local server");
            OS.Kill(_pid);
        }
        _pid = -1;
    }
}
