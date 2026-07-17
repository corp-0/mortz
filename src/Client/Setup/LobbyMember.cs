namespace Mortz.Client.Setup;

/// <summary>One lobby member as replicated by the server.</summary>
public readonly record struct LobbyMember(long PeerId, string Name, bool Ready, byte Team);
