using System.Diagnostics;
using Godot;
using Mortz.Shared;

namespace Mortz.Client.Session;

public static class ServerLauncher
{
    public static ProcessStartInfo CreateStartInfo(int port, string adminPassword, string serverName,
        bool allowJoinInProgress = true)
    {
        bool editor = OS.HasFeature("editor");
        string applicationDirectory = Path.GetDirectoryName(OS.GetExecutablePath())!;
        string directory = editor
            ? ProjectSettings.GlobalizePath("res://")
            : Path.Combine(applicationDirectory, "server");
        string executable = editor ? OS.GetExecutablePath() : Path.Combine(directory,
            OS.HasFeature("windows") ? "MortzServer.exe" : "MortzServer.x86_64");
        ProcessStartInfo start = new(executable) { WorkingDirectory = directory, UseShellExecute = false };
        List<string> args = [];
        if (editor)
        {
            args.AddRange(["--path", directory]);
        }
        args.AddRange(["--headless", "++", "--server", "--port", port.ToString(),
            "--content-root", editor ? ContentRoot.Resolve() : Path.Combine(directory, "content"),
            "--admin-password", adminPassword]);
        if (serverName.Length > 0)
        {
            args.AddRange(["--server-name", serverName]);
        }
        args.Add(allowJoinInProgress ? "--allow-jip" : "--no-jip");
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
