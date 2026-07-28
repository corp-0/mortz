using Godot;
using Mortz.Content;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>Everything the server resolved at boot: CLI flags over
/// server.toml over defaults. Content is the catalog snapshot everything
/// else reads from, so the options can never disagree.</summary>
public sealed class ServerBootConfig : IServerIdentity
{
    private const string DEFAULT_MODE_ID = "deathmatch";

    public required GameContent Content { get; init; }
    public required MapPackage Map { get; init; }
    public required MatchConfig Rules { get; init; }
    public required string AdminPassword { get; init; }
    public required string ServerName { get; init; }
    public required int GamePort { get; init; }
    public required int QueryPort { get; init; }

    string IServerIdentity.Name => ServerName;

    public static ServerBootConfig? TryLoad(string defaultMapId)
    {
        GameContent? content = GameContent.Load();
        if (content == null)
        {
            GD.PrintErr("[server] content catalog is unusable");
            return null;
        }

        string mapId = CmdArgs.GetValue("--map") ?? defaultMapId;
        MapPackage? map = content.LoadMap(mapId);
        if (map == null)
        {
            GD.PrintErr($"[server] failed to load map '{mapId}'");
            return null;
        }

        MatchConfig? rules = LoadRuleset(content);
        ServerConfig? serverConfig = ServerConfig.Load();
        if (rules == null || serverConfig == null)
            return null;

        string adminPassword = CmdArgs.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (adminPassword.Length > 0)
            GD.Print("[server] admin password set");
        int gamePort = CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT);
        return new ServerBootConfig
        {
            Content = content,
            Map = map,
            Rules = rules,
            AdminPassword = adminPassword,
            ServerName = ServerConfig.SanitizeName(
                CmdArgs.GetValue("--server-name") ?? serverConfig.Name),
            GamePort = gamePort,
            QueryPort = CmdArgs.GetInt("--query-port", ServerQueryProtocol.QueryPort(gamePort)),
        };
    }

    private static MatchConfig? LoadRuleset(GameContent content)
    {
        string? path = CmdArgs.GetValue("--ruleset");
        string? modeId = CmdArgs.GetValue("--mode");
        if (path != null && modeId != null)
        {
            GD.PrintErr("[server] --ruleset and --mode are mutually exclusive");
            return null;
        }
        if (modeId != null)
            return LoadModeRules(modeId, content);
        if (path == null)
            return LoadModeRules(DEFAULT_MODE_ID, content);

        ContentReadResult<MatchConfig> result = ContentManifestReader.ReadRulesetFile(path);
        PrintDiagnostics(result.Diagnostics);
        if (result.Value == null)
        {
            GD.PrintErr($"[server] failed to load ruleset '{path}'");
            return null;
        }
        GD.Print($"[server] ruleset '{path}' loaded");
        return result.Value;
    }

    private static MatchConfig? LoadModeRules(string modeId, GameContent content)
    {
        if (!content.Catalog.TryGetMode(modeId, out ResolvedContent<GameModeManifest>? mode) ||
            mode == null)
        {
            GD.PrintErr($"[server] unknown mode '{modeId}'");
            return null;
        }
        GD.Print($"[server] mode '{modeId}' loaded");
        return mode.Winner.Manifest.Rules;
    }

    private static void PrintDiagnostics(IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        foreach (ContentDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
                GD.PrintErr($"[content] {diagnostic}");
            else
                GD.PushWarning($"[content] {diagnostic}");
        }
    }
}
