using Mortz.Core.Match;
using Mortz.Core.Match.Participation;

namespace Mortz.Server.Match;

/// <summary>Match-lifetime cell: where the player sits and, while dead, when
/// their camera flips from the death presentation to spectating.</summary>
public sealed class ParticipationState
{
    public MatchParticipation Current { get; set; } = MatchParticipation.Active;

    /// <summary>Tick that flips DEATH_PRESENTATION into SPECTATING; null when
    /// no flip is scheduled.</summary>
    public int? SpectateAtTick { get; set; }
}
