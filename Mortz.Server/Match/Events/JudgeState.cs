namespace Mortz.Server.Match.Events;

/// <summary>Match-lifetime cell: everything the event judge remembers about
/// one player between frames.</summary>
public sealed class JudgeState
{
    /// <summary>Kills since their own last death.</summary>
    public int Streak { get; set; }

    /// <summary>Multi-kill chain length and the tick of its last kill. A chain
    /// survives the player's own death: a shell already in flight still counts.</summary>
    public int Chain { get; set; }

    public int ChainTick { get; set; }

    /// <summary>Who killed them last; 0 when nobody or the grudge is settled.</summary>
    public int LastKillerId { get; set; }

    /// <summary>Enemies killed since their own last death, for team wipes.</summary>
    public HashSet<int> KilledSinceDeath { get; } = [];

    /// <summary>Tick of the last kill counted toward a team wipe.</summary>
    public int WipeTick { get; set; }

    /// <summary>Consecutive self-inflicted deaths and the tick of the last.</summary>
    public int SuicideCount { get; set; }

    public int SuicideTick { get; set; }
}
