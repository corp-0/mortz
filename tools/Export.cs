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
        bool? linux = null; // null = both platforms
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "client" or "server" or "all": preset = arg; break;
                case "--debug": debug = true; break;
                case "--linux": linux = true; break;
                case "--windows": linux = false; break;
                default: throw new Exception($"unexpected argument '{arg}'");
            }
        }

        string root = Program.RepoRoot();
        // Must match the editor version (Godots-managed install), or exports run
        // on different templates than the project was built against.
        string godot = Environment.GetEnvironmentVariable("GODOT_PATH")
            ?? @"C:\Users\filax\AppData\Roaming\Godot\app_userdata\Godots\versions\Godot_v4_6_2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe";
        if (!File.Exists(godot))
            throw new Exception($"Godot not found at {godot} (set GODOT_PATH to override)");

        List<(string Name, string Dir, string Exe)> presets = new List<(string, string, string)>();
        if (preset is "client" or "all")
        {
            if (linux != true) presets.Add(("Mortz Client", "client", "Mortz.exe"));
            if (linux != false) presets.Add(("Mortz Client Linux", "client-linux", "Mortz.x86_64"));
        }
        if (preset is "server" or "all")
        {
            if (linux != true) presets.Add(("Mortz Server", "server", "MortzServer.exe"));
            if (linux != false) presets.Add(("Mortz Server Linux", "server-linux", "MortzServer.x86_64"));
        }
        string exportFlag = debug ? "--export-debug" : "--export-release";

        foreach ((string name, string dir, string exe) in presets)
        {
            string outDir = Path.Combine(root, "build", dir);
            string exePath = Path.Combine(outDir, exe);
            Directory.CreateDirectory(outDir);

            Console.WriteLine($"==> exporting '{name}' to build/{dir}/{exe}");
            RunProcess(godot, ["--headless", "--path", root, exportFlag, name, exePath]);

            // Linux binaries are terminal apps already; only Windows needs the flip.
            if (name == "Mortz Server")
            {
                MakeConsoleApp(exePath);
                File.Delete(Path.ChangeExtension(exePath, ".console.exe")); // stale wrapper from older exports
                Console.WriteLine("==> server exe flipped to console subsystem (blocks the terminal, Ctrl+C kills)");
            }

            string contentDest = Path.Combine(outDir, "content");
            MirrorContent(Path.Combine(root, "content"), contentDest);
            Console.WriteLine($"==> content copied to {contentDest}");
        }
        Console.WriteLine("done");
        if (presets.Any(p => p.Exe.EndsWith(".x86_64")))
            Console.WriteLine("note: exporting from Windows loses the executable bit; chmod +x the .x86_64 on the box");
    }

    /// <summary>
    /// Godot only exports GUI-subsystem exes (its console option ships a
    /// wrapper stub instead), so a terminal never owns the server and Ctrl+C
    /// can't kill it. Flipping the PE subsystem to console fixes that with one
    /// ushort; host-and-play is unaffected because OS.CreateProcess spawns
    /// children windowless.
    /// </summary>
    private static void MakeConsoleApp(string exePath)
    {
        const ushort IMAGE_SUBSYSTEM_WINDOWS_CUI = 3;
        using FileStream fs = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite);
        using BinaryReader r = new BinaryReader(fs);
        fs.Position = 0x3C;
        int peHeader = r.ReadInt32();
        fs.Position = peHeader;
        if (r.ReadUInt32() != 0x00004550) // "PE\0\0"
            throw new Exception($"{exePath} is not a PE image");
        // COFF header (20 bytes), then Subsystem at offset 68 of the optional
        // header (same offset in PE32 and PE32+).
        fs.Position = peHeader + 4 + 20 + 68;
        new BinaryWriter(fs).Write(IMAGE_SUBSYSTEM_WINDOWS_CUI);
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
