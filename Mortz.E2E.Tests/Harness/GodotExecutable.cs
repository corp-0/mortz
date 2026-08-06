namespace Mortz.E2E.Tests.Harness;

public static class GodotExecutable
{
    private static readonly string[] _executableNames =
        OperatingSystem.IsWindows()
            ? ["godot.exe", "godot4.exe"]
            : ["godot", "godot4"];

    public static string Resolve() => Resolve(
        Environment.GetEnvironmentVariable("MORTZ_GODOT"),
        Environment.GetEnvironmentVariable("GODOT_PATH"),
        Environment.GetEnvironmentVariable("PATH"),
        File.Exists);

    public static string Resolve(
        string? mortzGodot,
        string? godotPath,
        string? searchPath,
        Func<string, bool> exists)
    {
        foreach (string? configured in new[] { mortzGodot, godotPath })
        {
            if (string.IsNullOrWhiteSpace(configured))
                continue;
            string full = Path.GetFullPath(configured);
            if (exists(full))
                return full;
            throw new FileNotFoundException(
                $"Configured Godot executable does not exist: {full}", full);
        }

        if (!string.IsNullOrWhiteSpace(searchPath))
        {
            foreach (string directory in searchPath.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (string name in _executableNames)
                {
                    string candidate = Path.Combine(directory, name);
                    if (exists(candidate))
                        return Path.GetFullPath(candidate);
                }
            }
        }

        throw new FileNotFoundException(
            "Godot was not found. Set MORTZ_GODOT to the Godot Mono executable.");
    }
}
