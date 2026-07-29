namespace Mortz.Core.Match;

public sealed record PlayerVictor : Victor
{
    public PlayerVictor(int peerId)
    {
        if (peerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(peerId));
        PeerId = peerId;
    }

    public int PeerId { get; }
}
