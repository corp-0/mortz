using Godot;
using Mortz.Core.Sim;

namespace Mortz.Client.Replay;

public readonly record struct ReplayMortar(
    long Key, Vector2 Position, Vec2 Velocity);
