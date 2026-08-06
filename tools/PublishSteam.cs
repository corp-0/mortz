using System.Diagnostics;

namespace Mortz.Tools;

public static class PublishSteam
{
    private const int APP_ID = 5016960;
    private const int DEPOT_WINDOWS = 5016961;
    private const int DEPOT_LINUX = 5016962;
    private const string BRANCH = "";

    public static string ResolveSteamCmd() => ToolPath.Resolve("STEAMCMD_PATH", "steamcmd");

    public static void Push(string steamcmd, string root)
    {
        string account = Environment.GetEnvironmentVariable("STEAM_ACCOUNT")
                         ?? throw new Exception("set STEAM_ACCOUNT to the upload bot account name");

        string vdfPath = WriteBuildScript(root);
        Console.WriteLine($"==> pushing app {APP_ID} to Steam branch '{BRANCH}'");
        RunSteamCmd(steamcmd, account, vdfPath);
    }

    private static string WriteBuildScript(string root)
    {
        string buildDirectory = Path.Combine(root, "build");
        string steamDirectory = Path.Combine(buildDirectory, "steam");
        string outputDirectory = Path.Combine(steamDirectory, "output");
        Directory.CreateDirectory(outputDirectory);

        string vdfPath = Path.Combine(steamDirectory, "app_build.vdf");
        File.WriteAllText(vdfPath, $$"""
                                     "AppBuild"
                                     {
                                         "AppID" "{{APP_ID}}"
                                         "Desc" "playtest"
                                         "ContentRoot" "{{buildDirectory}}"
                                         "BuildOutput" "{{outputDirectory}}"
                                         "SetLive" "{{BRANCH}}"
                                         "Depots"
                                         {
                                             "{{DEPOT_WINDOWS}}"
                                             {
                                                 "FileMapping"
                                                 {
                                                     "LocalPath" "Mortz-win\*"
                                                     "DepotPath" "."
                                                     "recursive" "1"
                                                 }
                                             }
                                             "{{DEPOT_LINUX}}"
                                             {
                                                 "FileMapping"
                                                 {
                                                     "LocalPath" "Mortz-lin\*"
                                                     "DepotPath" "."
                                                     "recursive" "1"
                                                 }
                                             }
                                         }
                                     }
                                     """);
        return vdfPath;
    }

    private static void RunSteamCmd(string steamcmd, string account, string vdfPath)
    {
        // Locally steamcmd reuses the cached login, so no password. CI sets one
        // plus a restored config.vdf for the Steam Guard sentry.
        List<string> login = ["+login", account];
        string? password = Environment.GetEnvironmentVariable("STEAM_PASSWORD");
        if (password is not null)
            login.Add(password);

        ProcessStartInfo startInfo = new ProcessStartInfo(steamcmd) { UseShellExecute = false };
        foreach (string arg in login.Concat(["+run_app_build", vdfPath, "+quit"]))
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception($"steamcmd failed (exit {process.ExitCode}); " +
                                $"if it asked for a password, run 'steamcmd +login {account}' once to cache credentials");
    }
}
