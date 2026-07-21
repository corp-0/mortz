using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mortz.Tools;

/// <summary>
/// Exports the client and/or server presets into one bundle per platform
/// (build/Mortz-win, build/Mortz-lin). Content stays out of the PCK
/// (exclude_filter in export_presets.cfg) so maps swap without re-exporting.
/// The build dir is wiped first, so a partial export leaves only what it built.
/// </summary>
internal static class Export
{
    public static void Run(string[] args)
    {
        string preset = "all";
        bool debug = false;
        bool requireOfficial = false;
        bool? linux = null; // null = both platforms
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "client" or "server" or "all": preset = arg; break;
                case "--debug": debug = true; break;
                case "--require-official": requireOfficial = true; break;
                case "--linux": linux = true; break;
                case "--windows": linux = false; break;
                default: throw new Exception($"unexpected argument '{arg}'");
            }
        }

        string root = Program.RepoRoot();
        OfficialOverlay.Validate(root, requireOfficial);
        string godot = ResolveGodot(root);

        List<(string Name, string Dir, string Exe)> presets = new List<(string, string, string)>();
        if (preset is "client" or "all")
        {
            if (linux != true) presets.Add(("Mortz Client", "Mortz-win", "Mortz.exe"));
            if (linux != false) presets.Add(("Mortz Client Linux", "Mortz-lin", "Mortz.x86_64"));
        }
        if (preset is "server" or "all")
        {
            if (linux != true) presets.Add(("Mortz Server", "Mortz-win", "MortzServer.exe"));
            if (linux != false) presets.Add(("Mortz Server Linux", "Mortz-lin", "MortzServer.x86_64"));
        }
        string exportFlag = debug ? "--export-debug" : "--export-release";

        CleanBuildDir(Path.Combine(root, "build"));

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
                Console.WriteLine("==> server exe flipped to console subsystem (blocks the terminal, Ctrl+C kills)");
            }
        }

        // Client and server share the bundle, one content copy serves both.
        foreach (string dir in presets.Select(p => p.Dir).Distinct())
        {
            string contentDest = Path.Combine(root, "build", dir, "content");
            MirrorContent(Path.Combine(root, "content"), contentDest);
            Console.WriteLine($"==> content copied to {contentDest}");
        }
        Console.WriteLine("done");
        if (presets.Any(p => p.Exe.EndsWith(".x86_64")))
            Console.WriteLine("note: exporting from Windows loses the executable bit; chmod +x the .x86_64 on the box");
    }

    /// <summary>
    /// The editor must match the SDK the project builds against. A mismatched
    /// editor also silently migrates Mortz.csproj to its own version (backing
    /// it up as .old) and exits 0. GODOT_PATH overrides where to look, never
    /// which version is acceptable.
    /// </summary>
    private static string ResolveGodot(string root)
    {
        string csproj = Path.Combine(root, "Mortz.csproj");
        Match match = Regex.Match(File.ReadAllText(csproj), @"Sdk=""Godot\.NET\.Sdk/([\d.]+)""");
        if (!match.Success)
            throw new Exception($"no Godot.NET.Sdk version found in {csproj}");
        string want = match.Groups[1].Value;

        string godot = Environment.GetEnvironmentVariable("GODOT_PATH") ?? FindGodot(want);
        if (!File.Exists(godot))
            throw new Exception($"Godot not found at {godot} (set GODOT_PATH to override)");

        string reported = RunCapture(godot, ["--version"]).Trim();
        if (NormalizeVersion(reported) != NormalizeVersion(want))
            throw new Exception($"{godot} reports {reported}, but Mortz.csproj builds against {want}");
        return godot;
    }

    /// <summary>Godots installs each version in a directory named for it; the
    /// binary's own name carries a version string, hence the glob.</summary>
    private static string FindGodot(string version)
    {
        string installs = Environment.GetEnvironmentVariable("GODOT_ROOT") ?? @"E:\filax\Godot";
        string dir = Path.Combine(installs, version);
        if (!Directory.Exists(dir))
            throw new Exception($"no Godot {version} install under {installs} (set GODOT_PATH to override)");
        string[] hits = Directory.GetFiles(dir, "*_console.exe");
        if (hits.Length != 1)
            throw new Exception($"expected one *_console.exe in {dir}, found {hits.Length}");
        return hits[0];
    }

    /// <summary>Godot drops a zero patch from its version string ("4.7.stable.mono"),
    /// the csproj SDK version never does. Compare as padded triples.</summary>
    private static string NormalizeVersion(string version)
    {
        List<string> parts = Regex.Match(version, @"^\d+(\.\d+)*").Value.Split('.').ToList();
        while (parts.Count < 3)
        {
            parts.Add("0");
        }
        return string.Join('.', parts);
    }

    /// <summary>
    /// Godot only exports GUI-subsystem exes, so Ctrl+C can't kill the server.
    /// Flip the PE subsystem to console; host-and-play is unaffected because
    /// OS.CreateProcess spawns children windowless.
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

    private static string RunCapture(string exe, string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        using Process proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} {string.Join(' ', args)} failed (exit {proc.ExitCode})");
        return output;
    }

    /// <summary>
    /// Wipe build/ except .gdignore. Also surfaces a still-running previous
    /// build up front: a locked exe would make Godot's export hang at the
    /// final rename.
    /// </summary>
    private static void CleanBuildDir(string buildDir)
    {
        Directory.CreateDirectory(buildDir);
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(buildDir))
            {
                Directory.Delete(dir, recursive: true);
            }
            foreach (string file in Directory.EnumerateFiles(buildDir))
            {
                if (Path.GetFileName(file) != ".gdignore")
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new Exception($"cleaning {buildDir} failed ({e.Message.Trim()}), close any running build and retry");
        }
        Console.WriteLine("==> build directory cleaned");
    }

    private static void RunProcess(string exe, string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        // dotnet publish leaves idle MSBuild nodes behind that inherit the godot
        // console wrapper's stdout pipe; the wrapper reads that pipe until EOF,
        // which never comes while the nodes live, so it hangs after the export.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        using Process proc = Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} failed (exit {proc.ExitCode})");
    }
}
