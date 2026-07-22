namespace Mortz.Tools;

internal static class OfficialOverlay
{
    private const string DIRECTORY_NAME = "official";
    private const string MARKER_NAME = "official-overlay.toml";

    public static void Run(string[] args)
    {
        switch (args)
        {
            case ["check"]:
                string root = Program.RepoRoot();
                Validate(root, required: true);
                Console.WriteLine($"official overlay ready at {Path.Combine(root, DIRECTORY_NAME)}");
                return;
            case ["import-3d", ..]:
                Import3D.Run(args[1..]);
                return;
            default:
                throw new Exception("usage: dotnet run --project tools -- official <check|import-3d>");
        }
    }

    public static void Validate(string root, bool required)
    {
        string directory = Path.Combine(root, DIRECTORY_NAME);
        if (!Directory.Exists(directory))
        {
            if (required)
                throw new Exception($"official overlay is missing at {directory}");
            return;
        }

        if (!File.Exists(Path.Combine(directory, MARKER_NAME)))
            throw new Exception($"{directory} is not a Mortz official overlay (missing {MARKER_NAME})");
        if (File.Exists(Path.Combine(directory, "project.godot")))
            throw new Exception("the official overlay must not be a separate Godot project");

        RequireGodotIgnore(directory, "licenses");
        RequireGodotIgnore(directory, "source");
    }

    private static void RequireGodotIgnore(string overlay, string name)
    {
        string directory = Path.Combine(overlay, name);
        if (Directory.Exists(directory) && !File.Exists(Path.Combine(directory, ".gdignore")))
            throw new Exception($"official/{name} must contain .gdignore so Godot cannot export it");
    }
}
