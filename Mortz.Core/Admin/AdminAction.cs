namespace Mortz.Core.Admin;

/// <summary>Stable action ids included in signed privileged mutations.</summary>
public static class AdminAction
{
    public const byte SET_LOBBY_RULES = 1;
    public const byte SET_LOBBY_MAP = 2;
    public const byte END_MATCH = 3;
    public const byte SET_LOBBY_MODE = 4;
}
