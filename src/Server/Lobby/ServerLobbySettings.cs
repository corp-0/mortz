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
using Mortz.Server.Hosting;
using Mortz.Server.Session;
using Mortz.Shared;

namespace Mortz.Server.Lobby;

public interface IServerLobbySettings
{
    event Action<LobbySettingsChange>? Changed;

    MapPackage Map { get; }
    MatchConfig Config { get; }

    string ModeName { get; }

    void SendTo(int peerId);
    void Broadcast();
}

[Meta(typeof(IAutoNode))]
public partial class ServerLobbySettings : Node, IServerLobbySettings
{
    private sealed record ModeOption(string Id, GameModeManifest Manifest)
    {
        public string Name => Manifest.Name;
    }

    private readonly Dictionary<string, ContentOption> _maps = new(StringComparer.Ordinal);
    private readonly List<ModeOption> _modes = [];
    private bool _subscribed;

    [Dependency]
    public ServerBootConfig Boot => this.DependOn<ServerBootConfig>();

    [Dependency]
    public IServerSession Session => this.DependOn<IServerSession>();

    [Dependency]
    public IServerAdminAuthorizer Admin => this.DependOn<IServerAdminAuthorizer>();

    public event Action<LobbySettingsChange>? Changed;

    public MapPackage Map { get; private set; } = null!;
    public MatchConfig Config { get; private set; } = null!;

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
        Map = Boot.Map;
        Config = Boot.Rules;
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

    public void SendTo(int peerId) =>
        LobbySettingsProtocol.Encode(CreateState()).SendTo(peerId);

    public void Broadcast() => LobbySettingsProtocol.Encode(CreateState()).Broadcast();

    private void OnSettingsRequest(int sender, LobbySettingsRequestMsg message)
    {
        if (Session.IsLobby && Session.ContainsPlayer(sender))
            SendTo(sender);
    }

    private void LoadCatalog()
    {
        ContentCatalog catalog = Boot.Content.Catalog;
        foreach ((string id, ResolvedContent<MapManifest> resolved) in catalog.Maps
                     .OrderBy(pair => pair.Value.Winner.Manifest.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _maps[id] = new ContentOption(id, resolved.Winner.Manifest.Name);
        }
        foreach ((string id, ResolvedContent<GameModeManifest> resolved) in catalog.Modes
                     .OrderBy(pair => pair.Value.Winner.Manifest.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                     .Take(NetConfig.MAX_LOBBY_MODES))
        {
            GameModeManifest manifest = resolved.Winner.Manifest;
            _modes.Add(new ModeOption(id, manifest));
        }
        if (!_maps.ContainsKey(Map.MapId))
            _maps[Map.MapId] = new ContentOption(Map.MapId, Map.DisplayName);
    }

    private ModeOption? CurrentMode()
    {
        ModeOption[] matches = _modes
            .Where(mode => mode.Manifest.MatchesIdentity(Config))
            .OrderByDescending(mode => mode.Manifest.IdentitySpecificity)
            .ToArray();
        if (matches.Length == 0)
            return null;
        if (matches.Length > 1 &&
            matches[0].Manifest.IdentitySpecificity == matches[1].Manifest.IdentitySpecificity)
        {
            return null;
        }
        return matches[0];
    }

    private void OnRulesUpdate(int sender, LobbyRulesUpdateMsg message)
    {
        MatchConfig previous = Config;
        MatchConfig next;

        if (!CanMutate(sender) ||
            !Admin.TryAuthorize(sender, message.Sequence, AdminAction.SET_LOBBY_RULES,
                message.Config, message.Tag))
        {
            SendTo(sender);
            return;
        }

        try
        {
            next = MatchConfig.FromBytes(message.Config);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            SendTo(sender);
            return;
        }

        LobbySettingDelta[] deltas =
            LobbySettingsDiff.Between(previous, next);
        Config = next;
        Broadcast();
        GD.Print($"[server] lobby rules updated by admin {sender}");
        if (deltas.Length > 0)
        {
            Changed?.Invoke(new LobbySettingsChange(
                sender,
                LobbySettingsChangeKind.RULES,
                deltas));
        }
    }

    private void OnModeUpdate(int sender, LobbyModeUpdateMsg message)
    {
        string previousMode = ModeName;
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

        Physics physics = Physics.FromBytes(Config.Physics.ToBytes());
        foreach (AuthoredKey authored in mode.Manifest.PhysicsOverrides)
        {
            physics.TryApplyKey(authored.Key, authored.Value, out _);
        }
        physics.Clamp();
        Config = new MatchConfig
        {
            Rules = ModeRules.FromBytes(mode.Manifest.Config.Rules.ToBytes()),
            Physics = physics,
        };
        string nextMode = ModeName;
        Broadcast();
        GD.Print($"[server] lobby mode set to '{mode.Id}' by admin {sender}");
        if (previousMode != nextMode)
        {
            Changed?.Invoke(new LobbySettingsChange(
                sender,
                LobbySettingsChangeKind.MODE,
                [new LobbySettingDelta("Mode", previousMode, nextMode)]));
        }
    }

    private void OnMapUpdate(int sender, LobbyMapUpdateMsg message)
    {
        string previousMap = Map.DisplayName;
        byte[] payload = Encoding.UTF8.GetBytes(message.MapId);
        if (!CanMutate(sender) ||
            !Admin.TryAuthorize(sender, message.Sequence, AdminAction.SET_LOBBY_MAP,
                payload, message.Tag) ||
            !_maps.ContainsKey(message.MapId))
        {
            SendTo(sender);
            return;
        }

        MapPackage? selected = Boot.Content.LoadMap(message.MapId);
        if (selected == null)
        {
            SendTo(sender);
            return;
        }
        Map = selected;
        Broadcast();
        Changed?.Invoke(new LobbySettingsChange(
            sender,
            LobbySettingsChangeKind.MAP,
            [new LobbySettingDelta("Map", previousMap, Map.DisplayName)]
        ));
        GD.Print($"[server] lobby map changed to '{Map.MapId}' by admin {sender}");
    }

    private bool CanMutate(int sender) =>
        _subscribed && Session.IsLobby && Session.ContainsPlayer(sender);

    private LobbySettings CreateState()
    {
        List<ContentOption> options = _maps.Values
            .OrderBy(option => option.Name, StringComparer.Ordinal)
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .ToList();
        if (options.Count > NetConfig.MAX_LOBBY_MAPS)
        {
            ContentOption selected = _maps[Map.MapId];
            options = options.Take(NetConfig.MAX_LOBBY_MAPS - 1)
                .Append(selected)
                .DistinctBy(option => option.Id, StringComparer.Ordinal)
                .OrderBy(option => option.Name, StringComparer.Ordinal)
                .ThenBy(option => option.Id, StringComparer.Ordinal)
                .ToList();
        }
        return new LobbySettings(
            new LobbySelection(
                Map.MapId,
                Map.Hash,
                new LobbyCatalog(options),
                new LobbyCatalog(_modes.Select(mode => new ContentOption(mode.Id, mode.Name))),
                CurrentMode()?.Id),
            Config);
    }
}
