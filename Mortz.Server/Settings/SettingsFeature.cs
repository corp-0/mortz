using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Server.Content;
using Mortz.Server.Features;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Serilog;
using Combat = Mortz.Core.Match.Configuration.Combat;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;
using Physics = Mortz.Core.Match.Configuration.Physics;

namespace Mortz.Server.Settings;

/// <summary>Lobby-owned map and rules for the next match.</summary>
public sealed class SettingsFeature : IObservePlayers, IObservePhase
{
    private sealed record ModeOption(string Id, GameModeManifest Manifest)
    {
        public string Name => Manifest.Name;
    }

    private readonly Dictionary<string, ContentOption> _maps = new(StringComparer.Ordinal);
    private readonly List<ModeOption> _modes = [];
    private readonly IMapSource _mapSource;
    private readonly IServerLink _link;
    private readonly ILogger _log;

    public SettingsFeature(ServerBoot boot, IMapSource maps, IServerLink link, ILogger log)
    {
        _mapSource = maps;
        _link = link;
        _log = log;
        Map = boot.Map;
        Config = boot.Rules;
        LoadCatalog(boot.Catalog);
    }

    public MapSnapshot Map { get; private set; }

    public MatchConfig Config { get; private set; }

    public string ModeName => CurrentMode()?.Name ?? "Custom";

    public void PlayerJoined(Player player) => SendTo(player.PeerId);

    public void PlayerLeft(Player player) { }

    public void PhaseChanged(ServerPhaseKind phase)
    {
        if (phase == ServerPhaseKind.LOBBY)
            Broadcast();
    }

    public void SendTo(int peerId) => _link.Send(peerId, LobbySettingsProtocol.Encode(CreateState()));

    public void Broadcast() => _link.Broadcast(LobbySettingsProtocol.Encode(CreateState()));

    public bool TrySetRules(byte[] config, out LobbySettingDelta[] deltas)
    {
        deltas = [];
        MatchConfig next;
        try
        {
            next = MatchConfig.FromBytes(config);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return false;
        }

        deltas = LobbySettingsDiff.Between(Config, next);
        Config = next;
        return true;
    }

    public bool TrySetMode(string modeId, out LobbySettingDelta[] deltas)
    {
        deltas = [];
        ModeOption? mode = _modes.Find(option => StringComparer.Ordinal.Equals(option.Id, modeId));
        if (mode == null)
            return false;

        string previousMode = ModeName;
        Config = new MatchConfig
        {
            Rules = ModeRules.FromBytes(mode.Manifest.Rules.ToBytes()),
            Physics = Physics.FromBytes(mode.Manifest.Physics.ToBytes()),
            Combat = Combat.FromBytes(mode.Manifest.Combat.ToBytes()),
        };
        string nextMode = ModeName;
        if (previousMode != nextMode)
            deltas = [new LobbySettingDelta("Mode", previousMode, nextMode)];
        return true;
    }

    public bool TrySetMap(string mapId, out LobbySettingDelta[] deltas)
    {
        deltas = [];
        if (!_maps.ContainsKey(mapId))
            return false;

        MapSnapshot? selected = _mapSource.Load(mapId);
        if (selected == null)
        {
            _log.Warning("failed to load map '{MapId}'", mapId);
            return false;
        }

        deltas = [new LobbySettingDelta("Map", Map.DisplayName, selected.DisplayName)];
        Map = selected;
        return true;
    }

    private void LoadCatalog(ContentCatalog catalog)
    {
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
            _modes.Add(new ModeOption(id, resolved.Winner.Manifest));
        }
        if (!_maps.ContainsKey(Map.MapId))
            _maps[Map.MapId] = new ContentOption(Map.MapId, Map.DisplayName);
    }

    private ModeOption? CurrentMode()
    {
        ModeOption[] matches = _modes
            .Where(mode => mode.Manifest.Matches(Config))
            .ToArray();
        if (matches.Length == 0)
            return null;
        if (matches.Length > 1)
            return null;
        return matches[0];
    }

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
