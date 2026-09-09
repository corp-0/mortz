namespace Mortz.Server.Platform;

public enum QueryOwner
{
    NONE,
    MORTZ,
    STEAM,
}

public readonly record struct ServerCapabilities(
    bool GameListener,
    QueryOwner Query,
    bool Authentication,
    bool Publication,
    string Status);
