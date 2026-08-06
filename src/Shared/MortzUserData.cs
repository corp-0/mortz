namespace Mortz.Shared;

/// <summary>Resolves Mortz-owned persistent data without using Godot's user directory.</summary>
public static class MortzUserData
{
    public const string ENVIRONMENT_VARIABLE = "MORTZ_USER_DATA";

    private const string DIRECTORY_NAME = "Mortz";

    public static string Resolve()
    {
        string? configured = Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE);
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        // %LOCALAPPDATA% on Windows, XDG_DATA_HOME or ~/.local/share elsewhere.
        string baseDirectory =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (baseDirectory.Length == 0)
        {
            throw new InvalidOperationException(
                $"No user data folder on this system; set {ENVIRONMENT_VARIABLE}.");
        }
        return Path.GetFullPath(Path.Combine(baseDirectory, DIRECTORY_NAME));
    }
}
