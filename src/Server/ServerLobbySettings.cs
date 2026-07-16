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
    MapPackage Map { get; }
    MatchConfig Rules { get; }
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

    private readonly Dictionary<string, MapOption> _maps = new(StringComparer.Ordinal);
    private bool _subscribed;

    [Dependency]
    public ServerHost Host => this.DependOn<ServerHost>();

    [Dependency]
    public IServerSession Session => this.DependOn<IServerSession>();

    [Dependency]
    public IServerAdminAuthorizer Admin => this.DependOn<IServerAdminAuthorizer>();

    public MapPackage Map { get; private set; } = null!;
    public MatchConfig Rules { get; private set; } = null!;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Map = Host.Map;
        Rules = Host.Rules;
        LoadCatalog();
        LobbySettingsRequestMsg.Received += OnSettingsRequest;
        LobbyRulesUpdateMsg.Received += OnRulesUpdate;
        LobbyMapUpdateMsg.Received += OnMapUpdate;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        LobbySettingsRequestMsg.Received -= OnSettingsRequest;
        LobbyRulesUpdateMsg.Received -= OnRulesUpdate;
        LobbyMapUpdateMsg.Received -= OnMapUpdate;
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
        ContentCatalogResult result = ContentCatalog.Load(Host.ContentRootPath);
        foreach (ContentDiagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
                GD.PrintErr($"[content] {diagnostic}");
            else
                GD.PushWarning($"[content] {diagnostic}");
        }
        if (result.Catalog != null)
        {
            foreach ((string id, ResolvedMapDefinition resolved) in result.Catalog.Maps
                         .OrderBy(pair => pair.Value.Winner.Manifest.Name, StringComparer.Ordinal)
                         .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                _maps[id] = new MapOption(id, resolved.Winner.Manifest.Name);
            }
        }
        if (!_maps.ContainsKey(Map.MapId))
            _maps[Map.MapId] = new MapOption(Map.MapId, Map.DisplayName);
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

        MapPackage? selected = MapPackage.Load(message.MapId, Host.ContentRootPath);
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
            Rules.ToBytes());
    }
}
