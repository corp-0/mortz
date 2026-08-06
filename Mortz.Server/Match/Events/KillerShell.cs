namespace Mortz.Server.Match.Events;

/// <summary>One shell fired by one killer, for counting same-shell kills.</summary>
public readonly record struct KillerShell(int KillerId, int ShellId);
