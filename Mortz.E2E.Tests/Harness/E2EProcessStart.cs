namespace Mortz.E2E.Tests.Harness;

public sealed record E2EProcessStart(
    string Name,
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    ScenarioArtifacts Artifacts,
    ProcessReaper Reaper,
    IReadOnlyDictionary<string, string>? Environment = null);
