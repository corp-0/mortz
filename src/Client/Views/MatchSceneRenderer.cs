namespace Mortz.Client.Views;

public enum MatchRenderMode
{
    LIVE,
    REPLAY,
}

/// <summary>Applies one presented frame to every match-scene view.</summary>
public sealed class MatchSceneRenderer(
    PlayerViewManager players,
    MortarViewManager mortars,
    RopeOverlay ropes)
{
    public void Apply(PresentedMatchFrame frame, MatchRenderMode mode)
    {
        players.BeginFrame();
        foreach (PresentedPlayer player in frame.Players)
        {
            players.Place(player.PeerId, player.State, mode);
        }

        players.Prune();
        mortars.Apply(frame.Mortars, mode);
        ropes.Apply(frame.Ropes);
    }

    public void EndReplay() => mortars.Clear();

    public bool Uses(
        PlayerViewManager playerViews,
        MortarViewManager mortarViews,
        RopeOverlay ropeOverlay) =>
        ReferenceEquals(players, playerViews) &&
        ReferenceEquals(mortars, mortarViews) &&
        ReferenceEquals(ropes, ropeOverlay);
}
