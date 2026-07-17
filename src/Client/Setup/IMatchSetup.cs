using Mortz.Core.Match;

namespace Mortz.Client.Setup;

/// <summary>One lobby member as replicated by the server.</summary>
public readonly record struct LobbyMember(long PeerId, string Name, bool Ready, byte Team);

/// <summary>One selectable map from the replicated server catalog.</summary>
public readonly record struct MapOption(string Id, string Name);

/// <summary>
/// Scene-scoped read access to the canonical match setup as the server
/// currently knows it: parsed rules, selected map and catalog, and the lobby
/// roster with team assignments. UI reads current values here and re-renders
/// on the events instead of tracking the wire messages itself. Events fire on
/// actual value transitions, never once per message.
/// </summary>
public interface IMatchSetup
{
    /// <summary>Any rule value changed.</summary>
    event Action? RulesChanged;

    /// <summary>The Teams rule toggled or a lobby team assignment moved.</summary>
    event Action? TeamsChanged;

    /// <summary>The selected map, catalog, rules, or settings error changed.</summary>
    event Action? SettingsChanged;

    /// <summary>Lobby membership, a name, or a ready state changed.</summary>
    event Action? RosterChanged;

    /// <summary>False until the first valid server settings arrive.</summary>
    bool HasServerState { get; }

    /// <summary>Canonical rules; treat as read-only. Editors mutate a CopyRules().</summary>
    MatchConfig Rules { get; }

    MatchConfig CopyRules();

    string MapId { get; }
    string MapHash { get; }
    IReadOnlyList<MapOption> MapOptions { get; }

    /// <summary>Empty while the last received server settings were valid.</summary>
    string SettingsError { get; }

    IReadOnlyList<LobbyMember> Members { get; }
}
