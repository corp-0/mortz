namespace Mortz.Client.Session;

/// <summary>The client's coarse session lifecycle. It replaces combinations
/// of visibility and nullable fields as the authority for legal message effects.</summary>
public sealed class ClientSession
{
    public ClientSessionStage Stage { get; private set; } = ClientSessionStage.MENU;

    /// <summary>False once the server has admitted us: a second attempt would
    /// drop the peer with no teardown and leave the live session parented.
    /// Retargeting while still connecting stays legal.</summary>
    public bool TryBeginConnecting()
    {
        if (Stage is not (ClientSessionStage.MENU or ClientSessionStage.CONNECTING))
            return false;
        Stage = ClientSessionStage.CONNECTING;
        return true;
    }

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
