namespace Mortz.Tools;

internal static class ToolPath
{
    public static string Resolve(string envVar, string baseName)
    {
        string? configured = Environment.GetEnvironmentVariable(envVar);
        if (configured is not null)
        {
            if (!File.Exists(configured))
                throw new Exception($"{baseName} not found at {configured}");
            return configured;
        }

        string executable = OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;
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

        throw new Exception($"{baseName} not found. Install it or set {envVar}");
    }
}
