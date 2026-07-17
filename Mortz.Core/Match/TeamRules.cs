namespace Mortz.Core.Match;

/// <summary>Shared lobby team rules, so the server's validation and the
/// client's empty-slot rendering cannot drift apart.</summary>
public static class TeamRules
{
    /// <summary>Two teams split the lobby; an odd count leaves a spare slot
    /// on each side so the odd player out can always move.</summary>
    public static int SlotsPerTeam(int playerCount) => (playerCount + 1) / 2;
}
