using Mortz.Core.Match;

namespace Mortz.Server.Lobby;

internal readonly record struct LobbySeat(bool Ready, Team? Team);
