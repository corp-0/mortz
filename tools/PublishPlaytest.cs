using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace Mortz.Tools;

internal static class PublishPlaytest
{
    private const string ITCH_TARGET = "gillesgillespie/mortz";

    public static void Run(string[] args)
    {
        if (args.Length != 0)
            throw new Exception("usage: dotnet run --project tools -- publish-playtest");

        string root = Program.RepoRoot();
        string butler = ResolveButler();

        Export.Run(["all", "--require-official"]);

        string buildDirectory = Path.Combine(root, "build");
        string windowsDirectory = Path.Combine(buildDirectory, "Mortz-win");
        string linuxDirectory = Path.Combine(buildDirectory, "Mortz-lin");

        WriteManifest(windowsDirectory, "Mortz.exe");
        WriteManifest(linuxDirectory, "Mortz.x86_64");
        CreateArchives(buildDirectory, windowsDirectory, linuxDirectory);

        RunButler(butler, ["validate", windowsDirectory]);
        RunButler(butler, ["validate", linuxDirectory]);
        RunButler(butler, ["push", windowsDirectory, $"{ITCH_TARGET}:windows"]);
        RunButler(butler, ["push", linuxDirectory, $"{ITCH_TARGET}:linux"]);
    }

    private static string ResolveButler()
    {
        string? configured = Environment.GetEnvironmentVariable("BUTLER_PATH");
        if (configured is not null)
        {
            if (!File.Exists(configured))
                throw new Exception($"Butler not found at {configured}");
            return configured;
        }

        string executable = OperatingSystem.IsWindows() ? "butler.exe" : "butler";
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is not null)
        {
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new Exception("Butler not found. Install it or set BUTLER_PATH");
    }

    internal static void WriteManifest(string directory, string executable)
    {
        File.WriteAllText(Path.Combine(directory, ".itch.toml"),
            $"[[actions]]\nname = \"play\"\npath = \"{executable}\"\n");
    }

    internal static void CreateArchives(string buildDirectory, string windowsDirectory, string linuxDirectory)
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
            throw new Exception($"Butler failed (exit {process.ExitCode})");
    }
}
