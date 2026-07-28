using System.Text;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Content;
using Mortz.Core.Admin;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Server.Chat;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>Server-owned lobby setup consumed by the match lifecycle.</summary>
public interface IServerLobbySettings
{
    /// <summary>An admin's rules edit was accepted and Rules replaced.</summary>
    event Action? RulesChanged;

    MapPackage Map { get; }
    MatchConfig Rules { get; }

    /// <summary>Name of the mode the rules currently match, or "Custom".</summary>
    string ModeName { get; }

    void SendTo(long peerId);
    void Broadcast();
}

/// <summary>
/// Persistent lobby feature that owns map/rule selection, verifies signed
/// admin mutations, and publishes the same canonical state to every client.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class ServerLobbySettings : Node, IServerLobbySettings
{
    private sealed record MapOption(string Id, string Name);

    /// <summary>PresetBytes matching Rules.ToBytes() is what puts the lobby
    /// in this mode.</summary>
    private sealed record ModeOption(string Id, string Name, byte[] PresetBytes);

    private readonly Dictionary<string, MapOption> _maps = new(StringComparer.Ordinal);
    private readonly List<ModeOption> _modes = [];
    private bool _subscribed;

    [Dependency]
    public ServerBootConfig Config => this.DependOn<ServerBootConfig>();

    [Dependency]
    public IServerSession Session => this.DependOn<IServerSession>();

    [Dependency]
    public IServerAdminAuthorizer Admin => this.DependOn<IServerAdminAuthorizer>();

    public event Action? RulesChanged;

    public MapPackage Map { get; private set; } = null!;
    public MatchConfig Rules { get; private set; } = null!;

    public string ModeName
    {
        get
        {
            ModeOption? current = CurrentMode();
            return current?.Name ?? "Custom";
        }
    }

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Map = Config.Map;
        Rules = Config.Rules;
        LoadCatalog();
        LobbySettingsRequestMsg.Received += OnSettingsRequest;
        LobbyRulesUpdateMsg.Received += OnRulesUpdate;
        LobbyMapUpdateMsg.Received += OnMapUpdate;
        LobbyModeUpdateMsg.Received += OnModeUpdate;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        LobbySettingsRequestMsg.Received -= OnSettingsRequest;
        LobbyRulesUpdateMsg.Received -= OnRulesUpdate;
        LobbyMapUpdateMsg.Received -= OnMapUpdate;
        LobbyModeUpdateMsg.Received -= OnModeUpdate;
        _subscribed = false;
    }

    public void SendTo(long peerId) => CreateState().SendTo(peerId);

    public void Broadcast() => CreateState().Broadcast();

    private void OnSettingsRequest(long sender, LobbySettingsRequestMsg message)
    {
        if (Session.IsLobby && Session.ContainsPlayer(sender))
            SendTo(sender);
    }

    private void LoadCatalog()
    {
        ContentCatalog catalog = Config.Content.Catalog;
        foreach ((string id, ResolvedContent<MapManifest> resolved) in catalog.Maps
                     .OrderBy(pair => pair.Value.Winner.Manifest.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _maps[id] = new MapOption(id, resolved.Winner.Manifest.Name);
        }
        foreach ((string id, ResolvedContent<GameModeManifest> resolved) in catalog.Modes
                     .OrderBy(pair => pair.Value.Winner.Manifest.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                     .Take(NetConfig.MAX_LOBBY_MODES))
        {
            GameModeManifest manifest = resolved.Winner.Manifest;
            _modes.Add(new ModeOption(id, manifest.Name, manifest.Rules.ToBytes()));
        }
        if (!_maps.ContainsKey(Map.MapId))
            _maps[Map.MapId] = new MapOption(Map.MapId, Map.DisplayName);
    }

    private ModeOption? CurrentMode()
    {
        byte[] rules = Rules.ToBytes();
        return _modes.Find(mode => rules.AsSpan().SequenceEqual(mode.PresetBytes));
    }

    private void OnRulesUpdate(long sender, LobbyRulesUpdateMsg message)
    {
        if (!CanMutate(sender) ||
            !Admin.TryAuthorize(sender, message.Sequence, AdminAction.SET_LOBBY_RULES,
                message.Config, message.Tag))
        {
            SendTo(sender);
            return;
        }

        try
        {
            Rules = MatchConfig.FromBytes(message.Config);
        }
        catch (IOException)
        {
            SendTo(sender);
            return;
        }
        GD.Print($"[server] lobby rules updated by admin {sender}");
        RulesChanged?.Invoke();
        Broadcast();
    }

    private void OnModeUpdate(long sender, LobbyModeUpdateMsg message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.ModeId);
        ModeOption? mode = _modes.Find(option =>
            StringComparer.Ordinal.Equals(option.Id, message.ModeId));
        if (!CanMutate(sender) ||
            !Admin.TryAuthorize(sender, message.Sequence, AdminAction.SET_LOBBY_MODE,
                payload, message.Tag) ||
            mode == null)
        {
            SendTo(sender);
            return;
        }

        Rules = MatchConfig.FromBytes(mode.PresetBytes);
        GD.Print($"[server] lobby mode set to '{mode.Id}' by admin {sender}");
        RulesChanged?.Invoke();
        Broadcast();
    }

    private void OnMapUpdate(long sender, LobbyMapUpdateMsg message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.MapId);
        if (!CanMutate(sender) ||
            !Admin.TryAuthorize(sender, message.Sequence, AdminAction.SET_LOBBY_MAP,
                payload, message.Tag) ||
            !_maps.ContainsKey(message.MapId))
        {
            SendTo(sender);
            return;
        }

        MapPackage? selected = Config.Content.LoadMap(message.MapId);
        if (selected == null)
        {
            SendTo(sender);
            return;
        }
        Map = selected;
        GD.Print($"[server] lobby map changed to '{Map.MapId}' by admin {sender}");
        Broadcast();
    }

    private bool CanMutate(long sender) =>
        _subscribed && Session.IsLobby && Session.ContainsPlayer(sender);

    private LobbySettingsMsg CreateState()
    {
        List<MapOption> options = _maps.Values
            .OrderBy(option => option.Name, StringComparer.Ordinal)
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .ToList();
        if (options.Count > NetConfig.MAX_LOBBY_MAPS)
        {
            MapOption selected = _maps[Map.MapId];
            options = options.Take(NetConfig.MAX_LOBBY_MAPS - 1)
                .Append(selected)
                .DistinctBy(option => option.Id, StringComparer.Ordinal)
                .OrderBy(option => option.Name, StringComparer.Ordinal)
                .ThenBy(option => option.Id, StringComparer.Ordinal)
                .ToList();
        }
        return new LobbySettingsMsg(
            Map.MapId,
            Map.Hash,
            options.Select(option => option.Id).ToArray(),
            options.Select(option => option.Name).ToArray(),
            _modes.Select(mode => mode.Id).ToArray(),
            _modes.Select(mode => mode.Name).ToArray(),
            CurrentMode()?.Id ?? "",
            Rules.ToBytes());
    }
}
