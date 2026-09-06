using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Protocol.Replication;

namespace Mortz.Client.Players;

/// <summary>The latest replicated presentation and stats for one player in the current match.</summary>
public sealed class ClientMatchPlayer(PlayerStats baseStats)
{
    private StatsModifier[] _modifiers = [];
    private int _latestPresentationTick = -1;

    public PlayerPresentationState LatestPresentation { get; private set; }

    public int ModifierRevision { get; private set; } = -1;
    public int ModifierEffectiveTick { get; private set; }

    public PlayerStats Stats { get; private set; } = baseStats;

    public IReadOnlyList<StatsModifier> Modifiers => _modifiers;

    internal void ApplyPresentation(int tick, in PlayerPresentationState presentation)
    {
        if (tick <= _latestPresentationTick)
            return;
        _latestPresentationTick = tick;
        LatestPresentation = presentation;
    }

    public bool ApplyModifiers(PlayerStats stats, IReadOnlyList<StatsModifier> modifiers,
        int revision, int effectiveTick)
    {
        if (revision <= ModifierRevision)
            return false;
        ModifierRevision = revision;
        ModifierEffectiveTick = effectiveTick;
        Stats = stats;
        _modifiers = [.. modifiers];
        return true;
    }
}
