using Mortz.Client.Views;

namespace Mortz.Client.Replay;

public readonly record struct ReplayPlayer(int PeerId, PlayerViewState State);
