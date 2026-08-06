namespace Mortz.Server.Phases;

/// <summary>Readable phase for SERVER features that owe users a reply in the
/// wrong phase.</summary>
public sealed class CurrentPhase
{
    public ServerPhaseKind Kind { get; set; }
}
