using Mortz.Core.Match;

namespace Mortz.Core.Net;

/// <summary>The pre-match lobby as of one broadcast.</summary>
public sealed record LobbyRoster(
    IReadOnlyList<LobbyMember> Members,
    IReadOnlyList<SwapOffer> Offers);
