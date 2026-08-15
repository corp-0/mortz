using System.Collections.Immutable;
using Godot;
using Mortz.Core.Sim;

namespace Mortz.Client.Views;

public readonly record struct PresentedPlayer(int PeerId, PlayerViewState State);

public enum PresentedMortarSource
{
    AUTHORITATIVE,
    PREDICTED,
}

public readonly record struct PresentedMortarKey(PresentedMortarSource Source, long Id)
{
    public static PresentedMortarKey Authoritative(ushort id) =>
        new(PresentedMortarSource.AUTHORITATIVE, id);

    public static PresentedMortarKey Predicted(int spawnSeq) =>
        new(PresentedMortarSource.PREDICTED, spawnSeq);
}

public readonly record struct PresentedMortar(
    PresentedMortarKey Key,
    Vector2 Position,
    Vec2 Velocity);

public readonly record struct RopeSegment(Vector2 From, Vector2 To);

/// <summary>The immutable world sample consumed by both live and replay rendering.</summary>
public sealed record PresentedMatchFrame(
    float Tick,
    ImmutableArray<PresentedPlayer> Players,
    ImmutableArray<PresentedMortar> Mortars,
    ImmutableArray<RopeSegment> Ropes);
