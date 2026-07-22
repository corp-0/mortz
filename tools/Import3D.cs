using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mortz.Tools;

internal static partial class Import3D
{
    public static void Run(string[] args)
    {
        string? blenderOverride = null;
        bool rebuild = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--blender": blenderOverride = args[++i]; break;
                case "--rebuild": rebuild = true; break;
                default: throw new Exception($"unexpected argument '{args[i]}'");
            }
        }

        string root = Program.RepoRoot();
        OfficialOverlay.Validate(root, required: true);
        string overlay = Path.Combine(root, "official");
        string oldSource = Path.Combine(overlay, "Assets", "itchio");
        string sourceRoot = Path.Combine(overlay, "source", "3d", "itchio");
        MigrateVendorFiles(oldSource, sourceRoot);

        string blender = blenderOverride ?? ResolveBlender();
        string script = Path.Combine(root, "tools", "import_3d_assets.py");
        string outputRoot = Path.Combine(overlay, "Assets", "3D");
        string report = Path.Combine(overlay, "source", "3d", "import-3d-report.json");
        string overrides = Path.Combine(overlay, "import-3d-overrides.toml");
        string blenderState = Path.Combine(overlay, "source", "3d", ".blender");

        List<string> processArgs =
        [
            "--background",
            "--python-exit-code", "1",
            "--python", script,
            "--",
            "--source-root", sourceRoot,
            "--output-root", outputRoot,
            "--report", report,
            "--overrides", overrides,
        ];
        if (rebuild)
            processArgs.Add("--rebuild");

        Console.WriteLine($"==> importing 3D assets with {blender}");
        RunProcess(blender, processArgs, blenderState);
        Console.WriteLine($"==> import report: {Path.GetRelativePath(root, report)}");
    }

    private static void MigrateVendorFiles(string oldSource, string sourceRoot)
    {
        if (!Directory.Exists(oldSource))
        {
            if (!Directory.Exists(sourceRoot))
                throw new Exception($"no 3D vendor assets found at {sourceRoot}");
            return;
        }
        if (Directory.Exists(sourceRoot))
            throw new Exception($"both {oldSource} and {sourceRoot} exist; merge them before importing");

        Directory.CreateDirectory(Path.GetDirectoryName(sourceRoot)!);
        Directory.Move(oldSource, sourceRoot);
        Console.WriteLine($"==> moved vendor files to {sourceRoot}");
    }

    private static string ResolveBlender()
    {
        string? configured = Environment.GetEnvironmentVariable("BLENDER_PATH");
        if (File.Exists(configured))
            return configured;

        string executable = OperatingSystem.IsWindows() ? "blender.exe" : "blender";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate))
                return candidate;
        }

        string settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot",
            "editor_settings-4.7.tres");
        if (File.Exists(settings))
        {
            Match match = BlenderPathRegex().Match(File.ReadAllText(settings));
            if (match.Success)
            {
                string candidate = match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new Exception("Blender not found (set BLENDER_PATH or pass --blender PATH)");
    }

    private static void RunProcess(string executable, IEnumerable<string> args, string blenderState)
    {
        ProcessStartInfo startInfo = new(executable) { UseShellExecute = false };
        startInfo.Environment["BLENDER_USER_CONFIG"] = Path.Combine(blenderState, "config");
        startInfo.Environment["BLENDER_USER_DATAFILES"] = Path.Combine(blenderState, "datafiles");
        startInfo.Environment["BLENDER_USER_EXTENSIONS"] = Path.Combine(blenderState, "extensions");
        startInfo.Environment["BLENDER_USER_RESOURCES"] = blenderState;
        startInfo.Environment["BLENDER_USER_SCRIPTS"] = Path.Combine(blenderState, "scripts");
        startInfo.Environment["APPDATA"] = Path.Combine(blenderState, "appdata");
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using Process process = Process.Start(startInfo)
            ?? throw new Exception($"failed to start {executable}");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(executable)} failed (exit {process.ExitCode})");
    }

    [GeneratedRegex("filesystem/import/blender/blender_path\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex BlenderPathRegex();
}
