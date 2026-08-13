using Godot;
using Mortz.Client.Views;
using Mortz.Core.Sim;

namespace Mortz.Client.Replay;

public readonly record struct ReplayPlayer(int PeerId, PlayerViewState State);

public readonly record struct ReplayMortar(
    long Key, Vector2 Position, Vec2 Velocity);

public sealed record ReplayFrame(
    float Tick,
    ReplayPlayer[] Players,
    ReplayMortar[] Mortars,
    (Vector2 From, Vector2 To)[] Ropes);
