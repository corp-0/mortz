namespace Mortz.Core.Net.Lobby;

/// <summary>One pending team-swap offer, offerer to target.</summary>
[NetRow]
public readonly partial record struct SwapOffer(int From, int To);
