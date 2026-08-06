namespace Mortz.Core.Match.Scoring;

/// <summary>Who a match outcome names: a single player, or a whole team.</summary>
public abstract record Victor
{
    private Victor()
    {
    }

    public sealed record Player : Victor
    {
        public Player(int peerId)
        {
            if (peerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(peerId));
            PeerId = peerId;
        }

        public int PeerId { get; }
    }

    public sealed record Team(Teams.Team Value) : Victor;
}
