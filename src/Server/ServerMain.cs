using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;
using Mortz.Server.Chat;

namespace Mortz.Server;

/// <summary>Composition root for the dedicated-server scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerMain : Node,
    IProvide<NetworkManager>,
    IProvide<ServerBootConfig>,
    IProvide<IServerIdentity>,
    IProvide<IServerSession>,
    IProvide<IServerAdminAuthorizer>,
    IProvide<IServerLobbySettings>
{
    [Export] private ServerHost _host = null!;
    [Export] private ServerSessionController _session = null!;
    [Export] private ServerChat _chat = null!;
    [Export] private ServerLobbySettings _lobbySettings = null!;

    private NetworkManager _network = null!;
    private ServerBootConfig _config = null!;

    NetworkManager IProvide<NetworkManager>.Value() => _network;
    ServerBootConfig IProvide<ServerBootConfig>.Value() => _config;
    IServerIdentity IProvide<IServerIdentity>.Value() => _config;
    IServerSession IProvide<IServerSession>.Value() => _session;
    IServerAdminAuthorizer IProvide<IServerAdminAuthorizer>.Value() => _chat;
    IServerLobbySettings IProvide<IServerLobbySettings>.Value() => _lobbySettings;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        if (_host.Config is not { } config)
        {
            GetTree().Quit(1);
            return;
        }

        _config = config;
        _network = GetNode<NetworkManager>(NetworkManager.AUTOLOAD_PATH);
        this.Provide();
        if (!_host.Listen(_network))
            GetTree().Quit(1);
    }
}
