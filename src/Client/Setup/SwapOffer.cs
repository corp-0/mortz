namespace Mortz.Client.Setup;

/// <summary>A pending team swap offer as replicated by the server.</summary>
public readonly record struct SwapOffer(long From, long To);
