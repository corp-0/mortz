using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>Loads server configuration and owns the listening transport.</summary>
public partial class ServerHost : Node
{
    [Export] private string _defaultMap = "castlewars";

    public bool IsConfigured { get; private set; }
    public MapPackage Map { get; private set; } = null!;
    public MatchConfig Rules { get; private set; } = null!;
    public string AdminPassword { get; private set; } = "";
    public string ContentRootPath { get; private set; } = "";

    public override void _Ready() => IsConfigured = TryLoadConfiguration();

    public bool Listen(NetworkManager network)
    {
        int port = CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT);
        Error error = network.StartServer(port);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[server] failed to listen on port {port}: {error}");
            return false;
        }

        GD.Print($"[server] listening on port {port} (protocol v{NetConfig.PROTOCOL_VERSION}, " +
                 $"map '{Map.DisplayName}' {Map.Width}x{Map.Height}, " +
                 $"tick {SimConfig.TICK_RATE} Hz)");
        return true;
    }

    private bool TryLoadConfiguration()
    {
        string mapId = CmdArgs.GetValue("--map") ?? _defaultMap;
        ContentRootPath = ContentRoot.Resolve();
        MapPackage? map = MapPackage.Load(mapId, ContentRootPath);
        if (map == null)
        {
            GD.PrintErr($"[server] failed to load map '{mapId}'");
            return false;
        }

        MatchConfig? rules = LoadRuleset();
        ServerConfig? serverConfig = ServerConfig.Load();
        if (rules == null || serverConfig == null)
            return false;

        AdminPassword = CmdArgs.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (AdminPassword.Length > 0)
            GD.Print("[server] admin password set");
        Map = map;
        Rules = rules;
        return true;
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
