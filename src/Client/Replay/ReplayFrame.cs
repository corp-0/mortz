using Godot;

namespace Mortz.Client.Replay;

public sealed record ReplayFrame(
    float Tick,
    ReplayPlayer[] Players,
    ReplayMortar[] Mortars,
    (Vector2 From, Vector2 To)[] Ropes);
