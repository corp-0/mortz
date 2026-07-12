using System.Diagnostics;

namespace Mortz.Tools;

/// <summary>
/// Exports the client and/or server presets and copies content/ next to the
/// executable. Content is deliberately not embedded in the PCK (see
/// exclude_filter in export_presets.cfg); builds load it from disk so map
/// files can be swapped without re-exporting.
/// </summary>
internal static class Export
{
    public static void Run(string[] args)
    {
        string preset = "all";
        bool debug = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "client" or "server" or "all": preset = arg; break;
                case "--debug": debug = true; break;
                default: throw new Exception($"unexpected argument '{arg}'");
            }
        }

        string root = Program.RepoRoot();
        string godot = Environment.GetEnvironmentVariable("GODOT_PATH")
            ?? @"E:\filax\Godot\4.6.1\Godot_v4.6.1-stable_mono_win64_console.exe";
        if (!File.Exists(godot))
            throw new Exception($"Godot not found at {godot} (set GODOT_PATH to override)");

        List<(string Name, string Dir, string Exe)> presets = new List<(string, string, string)>();
        if (preset is "client" or "all") presets.Add(("Mortz Client", "client", "Mortz.exe"));
        if (preset is "server" or "all") presets.Add(("Mortz Server", "server", "MortzServer.exe"));
        string exportFlag = debug ? "--export-debug" : "--export-release";

        foreach ((string name, string dir, string exe) in presets)
        {
            string outDir = Path.Combine(root, "build", dir);
            string exePath = Path.Combine(outDir, exe);
            Directory.CreateDirectory(outDir);

            Console.WriteLine($"==> exporting '{name}' to build/{dir}/{exe}");
            RunProcess(godot, ["--headless", "--path", root, exportFlag, name, exePath]);

            string contentDest = Path.Combine(outDir, "content");
            MirrorContent(Path.Combine(root, "content"), contentDest);
            Console.WriteLine($"==> content copied to {contentDest}");
        }
        Console.WriteLine("done");
    }

    /// <summary>Mirror source into dest, skipping .import files (editor
    /// metadata; the game reads the raw files) and deleting anything stale.</summary>
    private static void MirrorContent(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        HashSet<string> wanted = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file) == ".import")
                continue;
            string rel = Path.GetRelativePath(source, file);
            wanted.Add(rel);
            string target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        foreach (string file in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
        {
            if (!wanted.Contains(Path.GetRelativePath(dest, file)))
                File.Delete(file);
        }
        // Deepest first, so emptied parents empty out too.
        foreach (string subDir in Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                Directory.Delete(subDir);
        }
    }

    private static void RunProcess(string exe, string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);
        using Process proc = Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} failed (exit {proc.ExitCode})");
    }
}
