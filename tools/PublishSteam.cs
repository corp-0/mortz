using System.Diagnostics;

namespace Mortz.Tools;

public static class PublishSteam
{
    private const int GAME_APP_ID = 5016960;
    private const int CLIENT_WINDOWS_DEPOT = 5016961;
    private const int CLIENT_LINUX_DEPOT = 5016962;
    private const int TOOL_APP_ID = 5067530;
    private const int TOOL_WINDOWS_DEPOT = 5067533;
    private const int TOOL_LINUX_DEPOT = 5067532;
    private const string BRANCH = "";

    public static string ResolveSteamCmd() => ToolPath.Resolve("STEAMCMD_PATH", "steamcmd");

    public static void Push(string steamcmd, string root)
    {
        string account = Environment.GetEnvironmentVariable("STEAM_ACCOUNT")
                         ?? throw new Exception("set STEAM_ACCOUNT to the upload bot account name");

        string clientScript = WriteBuildScript(root, GAME_APP_ID, "client", CLIENT_WINDOWS_DEPOT, CLIENT_LINUX_DEPOT);
        string serverScript = WriteBuildScript(root, TOOL_APP_ID, "server", TOOL_WINDOWS_DEPOT, TOOL_LINUX_DEPOT);
        Console.WriteLine($"==> uploading client app {GAME_APP_ID}");
        Console.WriteLine($"==> uploading server Tool {TOOL_APP_ID}: Windows depot {TOOL_WINDOWS_DEPOT}, Linux depot {TOOL_LINUX_DEPOT}");
        RunSteamCmd(steamcmd, account, [clientScript, serverScript]);
    }

    private static string WriteBuildScript(string root, int appId, string role, int windowsDepot, int linuxDepot)
    {
        string buildDirectory = Path.Combine(root, "build");
        string steamDirectory = Path.Combine(buildDirectory, "steam");
        string outputName = "output";
        string windowsExclusions = "";
        string linuxExclusions = "";
        if (role == "server")
        {
            outputName = "output-server";
            // The Tool gets these files from Steam's Dedicated Server Redistributables.
            windowsExclusions = RedistributableExclusions(root, "windows", "win64");
            linuxExclusions = RedistributableExclusions(root, "linux", "linux64");
        }
        string outputDirectory = Path.Combine(steamDirectory, outputName);
        Directory.CreateDirectory(outputDirectory);

        string vdfPath = Path.Combine(steamDirectory, $"app_build_{appId}.vdf");
        File.WriteAllText(vdfPath, $$"""
                                     "AppBuild"
                                     {
                                         "AppID" "{{appId}}"
                                         "Desc" "playtest {{role}}"
                                         "ContentRoot" "{{buildDirectory}}"
                                         "BuildOutput" "{{outputDirectory}}"
                                         "SetLive" "{{BRANCH}}"
                                         "Depots"
                                         {
                                             "{{windowsDepot}}"
                                             {
                                                 "FileMapping"
                                                 {
                                                     "LocalPath" "steam\windows\{{role}}\*"
                                                     "DepotPath" "."
                                                     "recursive" "1"
                                                 }
                                                 {{windowsExclusions}}
                                             }
                                             "{{linuxDepot}}"
                                             {
                                                 "FileMapping"
                                                 {
                                                     "LocalPath" "steam\linux\{{role}}\*"
                                                     "DepotPath" "."
                                                     "recursive" "1"
                                                 }
                                                 {{linuxExclusions}}
                                             }
                                         }
                                     }
                                     """);
        return vdfPath;
    }

    private static string RedistributableExclusions(string root, string platform, string runtimeDirectory)
    {
        string source = Path.Combine(root, "addons", "godotsteam_server", "redistributables", runtimeDirectory);
        return string.Join("\n", Directory.EnumerateFiles(source).Order()
            .Select(file => $"\"FileExclusion\" \"steam/{platform}/server/{Path.GetFileName(file)}\""));
    }

    private static void RunSteamCmd(string steamcmd, string account, IEnumerable<string> buildScripts)
    {
        // Locally steamcmd reuses the cached login, so no password. CI sets one
        // plus a restored config.vdf for the Steam Guard sentry.
        List<string> login = ["+login", account];
        string? password = Environment.GetEnvironmentVariable("STEAM_PASSWORD");
        if (password is not null)
            login.Add(password);

        ProcessStartInfo startInfo = new ProcessStartInfo(steamcmd) { UseShellExecute = false };
        foreach (string arg in login)
        {
            startInfo.ArgumentList.Add(arg);
        }
        foreach (string buildScript in buildScripts)
        {
            startInfo.ArgumentList.Add("+run_app_build");
            startInfo.ArgumentList.Add(buildScript);
        }
        startInfo.ArgumentList.Add("+quit");

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception($"steamcmd failed (exit {process.ExitCode}); " +
                                $"if it asked for a password, run 'steamcmd +login {account}' once to cache credentials");
    }
}
