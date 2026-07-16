using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node, IProvide<IClientChat>
{
    [Export] private ClientChat _clientChat = null!;

    IClientChat IProvide<IClientChat>.Value() => _clientChat;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady() => this.Provide();
}
