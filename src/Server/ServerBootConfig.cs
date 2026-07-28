using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>Everything the server resolved at boot: CLI flags over
/// server.json over defaults.</summary>
public sealed class ServerBootConfig : IServerIdentity
{
    public required MapPackage Map { get; init; }
    public required MatchConfig Rules { get; init; }
    public required string AdminPassword { get; init; }
    public required string ServerName { get; init; }
    public required string ContentRootPath { get; init; }
    public required int GamePort { get; init; }
    public required int QueryPort { get; init; }

    string IServerIdentity.Name => ServerName;

    public static ServerBootConfig? TryLoad(string defaultMapId)
    {
        string mapId = CmdArgs.GetValue("--map") ?? defaultMapId;
        string contentRoot = ContentRoot.Resolve();
        MapPackage? map = MapPackage.Load(mapId, contentRoot);
        if (map == null)
        {
            GD.PrintErr($"[server] failed to load map '{mapId}'");
            return null;
        }

        MatchConfig? rules = LoadRuleset();
        ServerConfig? serverConfig = ServerConfig.Load();
        if (rules == null || serverConfig == null)
            return null;

        string adminPassword = CmdArgs.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (adminPassword.Length > 0)
            GD.Print("[server] admin password set");
        int gamePort = CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT);
        return new ServerBootConfig
        {
            Map = map,
            Rules = rules,
            AdminPassword = adminPassword,
            ServerName = ServerConfig.SanitizeName(
                CmdArgs.GetValue("--server-name") ?? serverConfig.Name),
            ContentRootPath = contentRoot,
            GamePort = gamePort,
            QueryPort = CmdArgs.GetInt("--query-port", ServerQueryProtocol.QueryPort(gamePort)),
        };
    }

    private static MatchConfig? LoadRuleset()
    {
        string? path = CmdArgs.GetValue("--ruleset");
        if (path == null)
            return new MatchConfig();
        try
        {
            MatchConfig config = MatchConfig.FromJson(File.ReadAllText(path));
            GD.Print($"[server] ruleset '{path}' loaded");
            return config;
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[server] failed to load ruleset '{path}': {exception.Message}");
            return null;
        }
    }
}
