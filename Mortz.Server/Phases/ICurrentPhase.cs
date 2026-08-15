namespace Mortz.Server.Phases;

/// <summary>The current server phase without transition authority.</summary>
public interface ICurrentPhase
{
    ServerPhaseKind Kind { get; }
}
