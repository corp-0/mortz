using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Setup;

/// <summary>Persistent owner of the replicated match setup and its connection
/// lifecycle. Provides <see cref="IMatchSetup"/> to descendant scenes and
/// requests the canonical settings whenever a lobby broadcast arrives, closing
/// the one-shot snapshot race on lobby entry.</summary>
public partial class MatchSetup : SessionScopedNode, IMatchSetup
{
    private readonly MatchSetupSession _session = new();

    public event Action? RulesChanged
    {
        add => _session.RulesChanged += value;
        remove => _session.RulesChanged -= value;
    }

    public event Action? TeamsChanged
    {
        add => _session.TeamsChanged += value;
        remove => _session.TeamsChanged -= value;
    }

    public event Action? SettingsChanged
    {
        add => _session.SettingsChanged += value;
        remove => _session.SettingsChanged -= value;
    }

    public event Action? RosterChanged
    {
        add => _session.RosterChanged += value;
        remove => _session.RosterChanged -= value;
    }

    public bool HasServerState => _session.HasServerState;
    public MatchConfig Rules => _session.Rules;
    public string MapId => _session.MapId;
    public string MapHash => _session.MapHash;
    public IReadOnlyList<MapOption> MapOptions => _session.MapOptions;
    public string SettingsError => _session.SettingsError;
    public IReadOnlyList<LobbyMember> Members => _session.Members;

    public MatchConfig CopyRules() => _session.CopyRules();

    protected override void OnServiceReady()
    {
        _session.Subscribe();
        LobbyStateMsg.Received += OnLobbyState;
    }

    protected override void OnServiceExit()
    {
        LobbyStateMsg.Received -= OnLobbyState;
        _session.Dispose();
    }

    protected override void OnSessionBoundary() => _session.Clear();

    internal void ApplySettingsForTest(LobbySettingsMsg message) => _session.ApplySettings(message);
    internal void ApplyLobbyStateForTest(LobbyStateMsg message) => _session.ApplyLobbyState(message);

    private static void OnLobbyState(LobbyStateMsg message) =>
        new LobbySettingsRequestMsg().SendToServer();
}
