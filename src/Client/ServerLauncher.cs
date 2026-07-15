using Godot;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// "Host and play": spawns the dedicated server as a separate process next to
/// the game, so hosting locally and renting a cloud box run the exact same
/// server binary. In the editor (no exports yet) it launches this project
/// again in headless server mode instead.
/// </summary>
public static class ServerLauncher
{
    private static string ServerExeName =>
        OS.HasFeature("windows") ? "MortzServer.exe" : "MortzServer.x86_64";

    private static int _pid = -1;

    public static bool Spawn(int port, string adminPassword)
    {
        string[] gameArgs =
        [
            "--server", "--port", port.ToString(),
            "--content-root", ContentRoot.Resolve(),
        ];
        if (adminPassword.Length > 0)
            gameArgs = [.. gameArgs, "--admin-password", adminPassword];

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

        GD.Print($"[client] spawning local server: {exe}");
        _pid = OS.CreateProcess(exe, args);
        if (_pid <= 0)
        {
            GD.PrintErr($"[client] failed to spawn server process ({exe})");
            return false;
        }
        return true;
    }

    public static void Kill()
    {
        if (_pid > 0 && OS.IsProcessRunning(_pid))
        {
            GD.Print("[client] stopping local server");
            OS.Kill(_pid);
        }
        _pid = -1;
    }
}
