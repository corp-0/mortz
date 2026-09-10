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
            if (player.IsAlive)
            {
                Change(peerId, state, MatchParticipation.Active, changes);
                state.SpectateAtTick = null;
                continue;
            }

            int returnTick = player.RespawnTicks > 0 ? checked(world.Tick + player.RespawnTicks) : -1;
            bool justDied = deaths.Any(death => death.PeerId == peerId);
            MatchParticipation current = state.Current;
            if (justDied || current.Activity == MatchActivity.ACTIVE)
            {
                current = new MatchParticipation(MatchSeat.PLAYER, MatchActivity.DEATH_PRESENTATION,
                    SpectateReason.RESPAWN, returnTick);
                state.SpectateAtTick = world.Config.Rules.SpectateDuringRespawn
                    ? checked(world.Tick + DEATH_VIEW_DURATION_TICKS)
                    : null;
            }
            current = current with { ReturnTick = returnTick };
            if (current.Activity == MatchActivity.DEATH_PRESENTATION &&
                state.SpectateAtTick is int spectateAt && world.Tick >= spectateAt &&
                (player.RespawnTicks == 0 || player.RespawnTicks >= SimConfig.TICK_RATE * 3))
            {
                current = current with { Activity = MatchActivity.SPECTATING };
                state.SpectateAtTick = null;
            }
            Change(peerId, state, current, changes);
        }
        return changes;
    }

    private static void Change(int peerId, ParticipationState state, MatchParticipation next,
        List<MatchParticipationChange> changes)
    {
        if (state.Current == next) return;
        state.Current = next;
        changes.Add(new MatchParticipationChange(peerId, next));
    }
}
