namespace Mortz.Server;

/// <summary>One frame's worth of time, the only clock the server has.</summary>
public readonly record struct ServerTime(ulong Ms, double Delta);
