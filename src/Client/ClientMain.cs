using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Chat;
using Mortz.Client.Stats;

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node, IProvide<IClientChat>, IProvide<IClientStats>
{
    [Export] private ClientChat _clientChat = null!;
    [Export] private ClientStats _clientStats = null!;

    IClientChat IProvide<IClientChat>.Value() => _clientChat;
    IClientStats IProvide<IClientStats>.Value() => _clientStats;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady() => this.Provide();
}
