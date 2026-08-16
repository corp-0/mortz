using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Match.Services;

/// <summary>A behavior whose lifetime is exactly one match.</summary>
public interface IMatchService
{
}

/// <summary>The phase finished applying a match roster change.</summary>
public interface IObserveMatchRoster : IMatchService
{
    void RosterChanged();
}

public interface IEnterMatch : IMatchService
{
    void Enter(Player player, int generation, bool initialPhase);
}

public interface IObserveMatchInput : IMatchService
{
    void InputReceived(int payloadBytes);
}

public interface IObserveMatchUpdate : IMatchService
{
    void MatchUpdated(in MatchUpdate update, ServerTime time);
}

/// <summary>Advances before simulation and may stop the match tick with a phase request.</summary>
public interface IAdvanceMatch : IMatchService
{
    PhaseRequest Advance(ServerTime time);
}
