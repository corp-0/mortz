namespace Mortz.Client;

internal enum ClientSessionStage
{
    Menu,
    Connecting,
    Lobby,
    LoadingMatch,
    Playing,
}

/// <summary>The client's coarse session lifecycle. It replaces combinations
/// of visibility and nullable fields as the authority for legal message effects.</summary>
internal sealed class ClientSession
{
    public ClientSessionStage Stage { get; private set; } = ClientSessionStage.Menu;

    public void BeginConnecting() => Stage = ClientSessionStage.Connecting;

    public bool TryEnterLobby()
    {
        if (Stage == ClientSessionStage.Menu)
            return false;
        Stage = ClientSessionStage.Lobby;
        return true;
    }

    public bool TryBeginMatchLoad()
    {
        if (Stage is not (ClientSessionStage.Connecting or ClientSessionStage.Lobby))
            return false;
        Stage = ClientSessionStage.LoadingMatch;
        return true;
    }

    public bool TryEnterMatch()
    {
        if (Stage != ClientSessionStage.LoadingMatch)
            return false;
        Stage = ClientSessionStage.Playing;
        return true;
    }

    public void ReturnToMenu() => Stage = ClientSessionStage.Menu;

    public bool CanEnterSlowMotion => Stage == ClientSessionStage.Playing;
}
