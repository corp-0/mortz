namespace Mortz.Core.Sim;

/// <summary>The server took over a predicted shell; sent reliably to the
/// original shooter so their ghost stops flying.</summary>
public readonly record struct ShellRetirement(int FiredBy, int SpawnSeq);
