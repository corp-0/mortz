namespace Mortz.Client.Match;

/// <summary>Which score presentation is alive (ScoreHudHost's key). A future
/// win condition family (king of the hill) adds a member and a scene here.</summary>
public enum ScoreHudKind
{
    PLAYER_KILLS,
    TEAM_KILLS,
}
