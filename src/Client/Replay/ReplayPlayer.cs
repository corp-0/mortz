using Mortz.Client.Views;

namespace Mortz.Client.Replay;

internal readonly record struct ReplayPlayer(int PeerId, PlayerViewState State);
