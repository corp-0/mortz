using Mortz.Server.Players;

namespace Mortz.Server.Features;

/// <summary>Bring a late joiner into a match already in progress. Lobby joins
/// belong to IObservePlayers; phase initialization belongs to IObservePhase.</summary>
public interface ISyncJip
{
    void Sync(Player jipPlayer);
}
