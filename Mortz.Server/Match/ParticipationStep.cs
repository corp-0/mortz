using Mortz.Core.Match.Participation;
using Mortz.Core.Sim;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

public readonly record struct MatchParticipationChange(
    int PeerId,
    MatchParticipation State);

/// <summary>Owns match participation and respawn presentation transitions.</summary>
public class ParticipationStep(MatchStateKeys keys)
{
    /// <summary>How long a death holds the camera before spectating starts.</summary>
    public const int DEATH_VIEW_DURATION_TICKS = SimConfig.TICK_RATE * 2;

    private readonly MatchStateKey<ParticipationState> _key = keys.Claim<ParticipationState>(typeof(ParticipationStep));

    public void Seat(Player player) =>
        player.State(_key).Current = MatchParticipation.Active;

    public void AddJipSpectator(Player player) =>
        player.State(_key).Current = MatchParticipation.JipSpectator;

    public MatchParticipation Of(Player player) => player.State(_key).Current;

    public IReadOnlyList<MatchParticipationChange> Apply(MatchContext match, IReadOnlyList<Death> deaths)
    {
        List<MatchParticipationChange> changes = [];
        SimWorld world = match.World;
        foreach ((int peerId, Player member) in match.SeatedPlayers)
        {
            PlayerState player = world.Players[peerId];
            ParticipationState state = member.State(_key);
            MatchParticipation current = state.Current;
            if (player.RespawnTicks == 0 && current.Activity != MatchActivity.ACTIVE)
            {
                Change(peerId, state, MatchParticipation.Active, changes);
                state.SpectateAtTick = null;
                continue;
            }
            if (player.RespawnTicks == 0 ||
                current.Activity != MatchActivity.DEATH_PRESENTATION ||
                state.SpectateAtTick is not int spectateAt ||
                world.Tick < spectateAt)
            {
                continue;
            }
            MatchParticipation spectating = new(
                MatchSeat.PLAYER,
                MatchActivity.SPECTATING,
                SpectateReason.RESPAWN,
                current.ReturnTick);
            Change(peerId, state, spectating, changes);
            state.SpectateAtTick = null;
        }

        foreach (Death death in deaths)
        {
            if (!world.Players.TryGetValue(death.PeerId, out PlayerState player))
                continue;
            ParticipationState state = match.SeatedPlayers[death.PeerId].State(_key);
            int respawnAtTick = world.Tick + player.RespawnTicks;
            MatchParticipation presentation = new(
                MatchSeat.PLAYER,
                MatchActivity.DEATH_PRESENTATION,
                SpectateReason.RESPAWN,
                respawnAtTick);
            Change(death.PeerId, state, presentation, changes);
            if (world.Config.Rules.SpectateDuringRespawn)
                ScheduleSpectator(state, player, world.Tick);
        }
        return changes;
    }

    private static void ScheduleSpectator(
        ParticipationState state,
        PlayerState player,
        int tick)
    {
        if (player.RespawnTicks < DEATH_VIEW_DURATION_TICKS + SimConfig.TICK_RATE * 3)
            return;
        state.SpectateAtTick = tick + DEATH_VIEW_DURATION_TICKS;
    }

    private static void Change(
        int peerId,
        ParticipationState state,
        MatchParticipation next,
        List<MatchParticipationChange> changes)
    {
        if (state.Current == next)
            return;
        state.Current = next;
        changes.Add(new MatchParticipationChange(peerId, next));
    }
}
