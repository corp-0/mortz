namespace Mortz.E2E.Tests.Harness;

/// <summary>The Godot project directory, found by walking up from the test
/// binary to the solution file.</summary>
public static class RepoRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Mortz.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new E2EProcessException(
            $"Could not find Mortz.sln above {AppContext.BaseDirectory}.");
    }
}
