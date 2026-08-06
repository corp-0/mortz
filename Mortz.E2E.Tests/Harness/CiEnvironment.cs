namespace Mortz.E2E.Tests.Harness;

/// <summary>Whether this run is unattended. A watched-run feature must never
/// switch itself on where nobody can see it.</summary>
public static class CiEnvironment
{
    private static readonly string[] _signals =
        ["CI", "GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "JENKINS_URL", "BUILDKITE"];

    public static bool Detected => DetectedIn(Environment.GetEnvironmentVariable);

    public static bool DetectedIn(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        foreach (string signal in _signals)
        {
            string? value = read(signal);
            if (string.IsNullOrWhiteSpace(value))
                continue;
            // GitHub sets CI=true; a literal false is still a deliberate "no".
            if (!value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0")
                return true;
        }
        return false;
    }
}
