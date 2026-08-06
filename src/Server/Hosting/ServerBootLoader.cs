using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Server.Hosting;

/// <summary>Resolves the server's boot input: CLI flags over server.toml over defaults.</summary>
public static class ServerBootLoader
{
    private static readonly ILogger _log = MortzLog.For("server");
    private static readonly ILogger _contentLog = MortzLog.For("content");

    private const string DEFAULT_MODE_ID = "deathmatch";

    public static ServerBootLoad? TryLoad(string defaultMapId)
    {
        GameContent? content = GameContent.Load();
        if (content == null)
        {
            _log.Error("content catalog is unusable");
            return null;
        }

        string mapId = CmdArgs.GetValue("--map") ?? defaultMapId;
        MapPackage? map = content.LoadMap(mapId);
        if (map == null)
        {
            _log.Error("failed to load map '{MapId}'", mapId);
            return null;
        }

        MatchConfig? rules = LoadRuleset(content);
        ServerConfig? serverConfig = ServerConfig.Load();
        if (rules == null || serverConfig == null)
            return null;

        string adminPassword = CmdArgs.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (adminPassword.Length > 0)
            _log.Information("admin password set");
        int gamePort = CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT);
        bool allowJoinInProgress = serverConfig.AllowJoinInProgress;
        if (CmdArgs.HasFlag("--allow-jip"))
            allowJoinInProgress = true;
        if (CmdArgs.HasFlag("--no-jip"))
            allowJoinInProgress = false;
        ServerBoot boot = new()
        {
            Map = map.ToSnapshot(),
            Rules = rules,
            Catalog = content.Catalog,
            AdminPassword = adminPassword,
            Name = ServerConfig.SanitizeName(
                CmdArgs.GetValue("--server-name") ?? serverConfig.Name),
            GamePort = gamePort,
            QueryPort = CmdArgs.GetInt("--query-port", ServerQueryProtocol.QueryPort(gamePort)),
            Seed = Random.Shared.Next(),
            NetStats = CmdArgs.HasFlag("--net-stats"),
            AllowJoinInProgress = allowJoinInProgress,
        };
        return new ServerBootLoad(boot, content);
    }

    private static MatchConfig? LoadRuleset(GameContent content)
    {
        string? path = CmdArgs.GetValue("--ruleset");
        string? modeId = CmdArgs.GetValue("--mode");
        if (path != null && modeId != null)
        {
            _log.Error("--ruleset and --mode are mutually exclusive");
            return null;
        }
        if (modeId != null)
            return LoadModeRules(modeId, content);
        if (path == null)
            return LoadModeRules(DEFAULT_MODE_ID, content);

        ContentReadResult<RulesetManifest> result = TomlModel.ReadFile<RulesetManifest>(path);
        PrintDiagnostics(result.Diagnostics);
        if (result.Value == null)
        {
            _log.Error("failed to load ruleset '{Path}'", path);
            return null;
        }
        _log.Information("ruleset '{Path}' loaded", path);
        return new MatchConfig
        {
            Rules = result.Value.Rules,
            Physics = result.Value.Physics,
            Combat = result.Value.Combat,
        };
    }

    private static MatchConfig? LoadModeRules(string modeId, GameContent content)
    {
        if (!content.Catalog.TryGetMode(modeId, out ResolvedContent<GameModeManifest>? mode) ||
            mode == null)
        {
            _log.Error("unknown mode '{ModeId}'", modeId);
            return null;
        }
        _log.Information("mode '{ModeId}' loaded", modeId);
        GameModeManifest manifest = mode.Winner.Manifest;
        return new MatchConfig
        {
            Rules = manifest.Rules,
            Physics = manifest.Physics,
            Combat = manifest.Combat,
        };
    }

    private static void PrintDiagnostics(IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        foreach (ContentDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
                _contentLog.Error("{Diagnostic}", diagnostic);
            else
                _contentLog.Warning("{Diagnostic}", diagnostic);
        }
    }
}
