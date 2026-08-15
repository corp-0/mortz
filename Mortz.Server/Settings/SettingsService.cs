using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Server.Content;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Mortz.Server.Services;
using Serilog;

namespace Mortz.Server.Settings;

/// <summary>Server-lifetime map and rules for the next match.</summary>
public sealed class SettingsService : IObservePlayers, IObservePhase
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

    public SettingsService(ServerBoot boot, IMapSource maps, IServerLink link, ILogger log)
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

    public SettingsMutationResult SetRules(byte[] config)
    {
        MatchConfig next;
        try
        {
            next = MatchConfig.FromBytes(config);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return new SettingsMutationResult.Rejected(SettingsRejectReason.INVALID_RULES);
        }

        LobbySettings before = CreateState();
        MatchConfigSnapshot nextSnapshot = next.ToSnapshot();
        LobbySettingDelta[] deltas = LobbySettingsDiff.Between(Config.ToSnapshot(), nextSnapshot);
        Config = nextSnapshot.ToMutable();
        return Applied(before, deltas);
    }

    public SettingsMutationResult SetMode(string modeId)
    {
        ModeOption? mode = _modes.Find(option => StringComparer.Ordinal.Equals(option.Id, modeId));
        if (mode == null)
            return new SettingsMutationResult.Rejected(SettingsRejectReason.UNKNOWN_MODE);

        LobbySettings before = CreateState();
        string previousMode = ModeName;
        MatchConfig next = mode.Manifest.ToMatchConfigSnapshot().ToMutable();
        next.Clamp();
        Config = next.ToSnapshot().ToMutable();
        string nextMode = ModeName;
        LobbySettingDelta[] deltas = previousMode == nextMode
            ? []
            : [new LobbySettingDelta("Mode", previousMode, nextMode)];
        return Applied(before, deltas);
    }

    public SettingsMutationResult SetMap(string mapId)
    {
        if (!_maps.ContainsKey(mapId))
            return new SettingsMutationResult.Rejected(SettingsRejectReason.UNKNOWN_MAP);

        MapSnapshot? selected = _mapSource.Load(mapId);
        if (selected == null)
        {
            _log.Warning("failed to load map '{MapId}'", mapId);
            return new SettingsMutationResult.Rejected(SettingsRejectReason.MAP_LOAD_FAILED);
        }

        LobbySettings before = CreateState();
        LobbySettingDelta[] deltas =
            [new("Map", Map.DisplayName, selected.DisplayName)];
        Map = selected;
        return Applied(before, deltas);
    }

    private SettingsMutationResult Applied(
        LobbySettings before,
        IReadOnlyList<LobbySettingDelta> deltas)
    {
        LobbySettings after = CreateState();
        return new SettingsMutationResult.Applied(new SettingsChange(
            before,
            after,
            deltas,
            before.Config.Rules.Teams != after.Config.Rules.Teams));
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
        List<ContentOption> options =
        [
            .. _maps.Values
                .OrderBy(option => option.Name, StringComparer.Ordinal)
                .ThenBy(option => option.Id, StringComparer.Ordinal)
        ];
        if (options.Count > NetConfig.MAX_LOBBY_MAPS)
        {
            ContentOption selected = _maps[Map.MapId];
            options =
            [
                .. options.Take(NetConfig.MAX_LOBBY_MAPS - 1)
                    .Append(selected)
                    .DistinctBy(option => option.Id, StringComparer.Ordinal)
                    .OrderBy(option => option.Name, StringComparer.Ordinal)
                    .ThenBy(option => option.Id, StringComparer.Ordinal)
            ];
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
