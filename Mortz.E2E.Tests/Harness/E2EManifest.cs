namespace Mortz.E2E.Tests.Harness;

public sealed record E2EManifest(
    string RunId,
    string Scenario,
    string SchemaHash,
    double Timescale,
    int GamePort,
    int QueryPort,
    IReadOnlyList<string> SweepActions,
    IReadOnlyList<E2EProcessRecord> Processes);
