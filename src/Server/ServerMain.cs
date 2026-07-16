using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Server.Chat;

namespace Mortz.Server;

/// <summary>Composition root for the dedicated-server scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerMain : Node,
    IProvide<ServerHost>,
    IProvide<IServerSession>,
    IProvide<IServerAdminAuthorizer>,
    IProvide<IServerLobbySettings>
{
    [Export] private ServerHost _host = null!;
    [Export] private ServerSessionController _session = null!;
    [Export] private ServerChat _chat = null!;
    [Export] private ServerLobbySettings _lobbySettings = null!;

    ServerHost IProvide<ServerHost>.Value() => _host;
    IServerSession IProvide<IServerSession>.Value() => _session;
    IServerAdminAuthorizer IProvide<IServerAdminAuthorizer>.Value() => _chat;
    IServerLobbySettings IProvide<IServerLobbySettings>.Value() => _lobbySettings;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        if (!_host.IsConfigured)
        {
            GetTree().Quit(1);
            return;
        }

        this.Provide();
        if (!_host.Listen())
            GetTree().Quit(1);
    }
}
