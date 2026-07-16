using Godot;

namespace Mortz.Client.Replay;

internal sealed record ReplayFrame(
    float Tick,
    ReplayPlayer[] Players,
    ReplayMortar[] Mortars,
    (Vector2 From, Vector2 To)[] Ropes);
