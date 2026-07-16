using Godot;
using Mortz.Core.Sim;

namespace Mortz.Client.Replay;

internal readonly record struct ReplayMortar(
    long Key, Vector2 Position, Vec2 Velocity);
