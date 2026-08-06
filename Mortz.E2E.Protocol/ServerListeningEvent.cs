namespace Mortz.E2E.Protocol;

/// <summary>Real bound ports; -1 when the responder never bound.</summary>
public sealed record ServerListeningEvent(int GamePort, int QueryPort) : E2EEvent;
