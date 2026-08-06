using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace Mortz.Tools;

public static class PublishPlaytest
{
    private const string ITCH_TARGET = "gillesgillespie/mortz";
    private const string USAGE = "usage: dotnet run --project tools -- publish-playtest [--skip-build] [--only itch,steam,docker]";

    public static void Run(string[] args)
    {
        (HashSet<string> targets, bool skipBuild) = ParseOptions(args);
        bool itch = targets.Contains("itch");
        bool steam = targets.Contains("steam");
        bool docker = targets.Contains("docker");

        string root = Program.RepoRoot();
        // Resolve up front so a missing tool fails before the long export.
        string butler = itch ? ToolPath.Resolve("BUTLER_PATH", "butler") : "";
        string steamcmd = steam ? PublishSteam.ResolveSteamCmd() : "";
        string dockerCli = docker ? PublishDocker.ResolveDocker() : "";

        if (!skipBuild)
            Export.Run(["all", "--require-official"]);

        string buildDirectory = Path.Combine(root, "build");
        string windowsDirectory = Path.Combine(buildDirectory, "Mortz-win");
        string linuxDirectory = Path.Combine(buildDirectory, "Mortz-lin");

        // Steam first: the itch manifests written below must not land in the depots.
        if (steam)
            PublishSteam.Push(steamcmd, root);
        if (docker)
            PublishDocker.Push(dockerCli, root);

        if (!itch)
            return;

        WriteManifest(windowsDirectory, "Mortz.exe");
        WriteManifest(linuxDirectory, "Mortz.x86_64");
        CreateArchives(buildDirectory, windowsDirectory, linuxDirectory);

        RunButler(butler, ["validate", windowsDirectory]);
        RunButler(butler, ["validate", linuxDirectory]);
        RunButler(butler, ["push", windowsDirectory, $"{ITCH_TARGET}:windows"]);
        RunButler(butler, ["push", linuxDirectory, $"{ITCH_TARGET}:linux"]);
    }

    private static (HashSet<string> Targets, bool SkipBuild) ParseOptions(string[] args)
    {
        HashSet<string> all = ["itch", "steam", "docker"];
        HashSet<string>? targets = null;
        bool skipBuild = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--skip-build" when !skipBuild:
                    skipBuild = true;
                    break;
                case "--only" when targets is null && index + 1 < args.Length:
                    targets = args[++index]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet();
                    if (targets.Count == 0 || !targets.IsSubsetOf(all))
                        throw new Exception(USAGE);
                    break;
                default:
                    throw new Exception(USAGE);
            }
        }

        return (targets ?? all, skipBuild);
    }

    public static void WriteManifest(string directory, string executable)
    {
        File.WriteAllText(Path.Combine(directory, ".itch.toml"),
            $"[[actions]]\nname = \"play\"\npath = \"{executable}\"\n");
    }

    public static void CreateArchives(string buildDirectory, string windowsDirectory, string linuxDirectory)
    {
        string windowsArchive = Path.Combine(buildDirectory, "Mortz-win.zip");
        ZipFile.CreateFromDirectory(windowsDirectory, windowsArchive,
            CompressionLevel.SmallestSize, includeBaseDirectory: true);
        Console.WriteLine($"==> archived {windowsArchive}");

        string linuxArchive = Path.Combine(buildDirectory, "Mortz-lin.tar.gz");
        CreateLinuxArchive(linuxDirectory, linuxArchive);
        Console.WriteLine($"==> archived {linuxArchive}");
    }

    private static void CreateLinuxArchive(string sourceDirectory, string archivePath)
    {
        UnixFileMode regularFile = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                   UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        UnixFileMode executable = regularFile | UnixFileMode.UserExecute |
                                  UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        string rootName = Path.GetFileName(sourceDirectory);

        using FileStream archive = File.Create(archivePath);
        using GZipStream gzip = new GZipStream(archive, CompressionLevel.SmallestSize);
        using TarWriter writer = new TarWriter(gzip, TarEntryFormat.Pax);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            using FileStream data = File.OpenRead(file);
            PaxTarEntry entry = new PaxTarEntry(TarEntryType.RegularFile, $"{rootName}/{relativePath}")
            {
                DataStream = data,
                Mode = file.EndsWith(".x86_64", StringComparison.OrdinalIgnoreCase) ? executable : regularFile
            };
            writer.WriteEntry(entry);
        }
    }

    private static void RunButler(string butler, string[] args)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(butler) { UseShellExecute = false };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception($"butler failed (exit {process.ExitCode})");
    }
}
