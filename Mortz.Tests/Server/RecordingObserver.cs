using Mortz.Server.Diagnostics;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Tests.Server;

/// <summary>Writes down what the server told it, so a test can assert on the
/// observer contract instead of reaching into the server.</summary>
public sealed class RecordingObserver : IMatchObserver
{
    private readonly List<string> _trace = [];

    public IReadOnlyList<string> Trace => _trace;

    public MatchFrame? LastFrame { get; private set; }

    public void PlayerJoined(Player player, ServerPhaseKind phase) =>
        _trace.Add($"join:{player.PeerId}");

    public void PlayerLeft(Player player, ServerPhaseKind phase) =>
        _trace.Add($"leave:{player.PeerId}");

    public void PhaseChanged(ServerPhaseKind kind) => _trace.Add($"phase:{kind}");

    public void MatchAdvanced(in MatchFrame frame) => LastFrame = frame;
}
