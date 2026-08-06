using System.Diagnostics;
using System.Text;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

/// <summary>
/// The E2E control channel is compiled out of exports with #if TOOLS. A clean
/// compile doesn't prove that. This builds the export configurations for real
/// and greps the type names out of the produced assembly bytes. A silent
/// false pass here is the dangerous failure, so we check the bytes directly
/// instead of just trusting the build.
/// </summary>
public sealed class ExportStripTests
{
    /// <summary>Every type that carries the control channel or its composition.</summary>
    private static readonly string[] _strippedTypes =
    [
        "E2EDriver",
        "E2ELaunch",
        "E2EStdoutWriter",
        "IE2EHandler",
        "IE2EResponder",
        "ServerE2EHandler",
        "ServerE2ERoot",
        "ClientE2EHandler",
        "ClientE2ERoot",
        "IE2EClientBridge",
        "NullE2EClientBridge",
        "E2EMatchProbe",
        "BotInputScript",
    ];

    [Theory]
    [InlineData("ExportRelease")]
    [InlineData("ExportDebug")]
    public void AnExportedAssemblyContainsNoE2ECode(string configuration)
    {
        string output = Build(configuration);
        string assembly = Path.Combine(output, "Mortz.dll");
        Assert.True(File.Exists(assembly), $"{assembly} was not produced");

        string metadata = Encoding.ASCII.GetString(File.ReadAllBytes(assembly));
        string[] survivors = _strippedTypes
            .Where(type => metadata.Contains(type, StringComparison.Ordinal))
            .ToArray();

        Assert.True(survivors.Length == 0,
            $"{configuration} still contains E2E types: {string.Join(", ", survivors)}");
    }

    [Theory]
    [InlineData("ExportRelease")]
    [InlineData("ExportDebug")]
    public void AnExportedBuildDoesNotShipTheProtocolAssembly(string configuration)
    {
        string output = Build(configuration);

        Assert.False(File.Exists(Path.Combine(output, "Mortz.E2E.Protocol.dll")),
            $"{configuration} shipped Mortz.E2E.Protocol.dll");
    }

    [Fact]
    public void TheEditorBuildStillContainsThemSoTheCheckCanFail()
    {
        // Without this, a typo in a type name would make every assertion above
        // pass for the wrong reason.
        string assembly = Path.Combine(
            RepoRoot.Path, ".godot", "mono", "temp", "bin", "Debug", "Mortz.dll");
        Assert.True(File.Exists(assembly), $"{assembly} was not produced");

        string metadata = Encoding.ASCII.GetString(File.ReadAllBytes(assembly));
        string[] missing = _strippedTypes
            .Where(type => !metadata.Contains(type, StringComparison.Ordinal))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"the Debug build is missing types this test claims to look for: " +
            $"{string.Join(", ", missing)}");
    }

    private static string Build(string configuration)
    {
        ProcessStartInfo info = new("dotnet")
        {
            WorkingDirectory = RepoRoot.Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[]
                 { "build", "Mortz.csproj", "-c", configuration, "--nologo", "-v:quiet" })
        {
            info.ArgumentList.Add(argument);
        }

        using Process build = Process.Start(info)
            ?? throw new E2EProcessException("Could not start dotnet build.");
        string output = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
        build.WaitForExit();
        Assert.True(build.ExitCode == 0, $"{configuration} build failed:{Environment.NewLine}{output}");

        return Path.Combine(RepoRoot.Path, ".godot", "mono", "temp", "bin", configuration);
    }
}
