namespace Mortz.Client.Session;

/// <summary>The client's coarse session lifecycle. It replaces combinations
/// of visibility and nullable fields as the authority for legal message effects.</summary>
internal sealed class ClientSession
{
    public ClientSessionStage Stage { get; private set; } = ClientSessionStage.MENU;

    public void BeginConnecting() => Stage = ClientSessionStage.CONNECTING;

    public bool TryEnterLobby()
    {
        if (Stage == ClientSessionStage.MENU)
            return false;
        Stage = ClientSessionStage.LOBBY;
        return true;
    }

    public bool TryBeginMatchLoad()
    {
        if (Stage is not (ClientSessionStage.CONNECTING or ClientSessionStage.LOBBY))
            return false;
        Stage = ClientSessionStage.LOADING_MATCH;
        return true;
    }

    public bool TryEnterMatch()
    {
        if (Stage != ClientSessionStage.LOADING_MATCH)
            return false;
        Stage = ClientSessionStage.PLAYING;
        return true;
    }

    public void ReturnToMenu() => Stage = ClientSessionStage.MENU;
}
