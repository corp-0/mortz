using Godot;
using Mortz.Core.Net;
using Mortz.Net;

namespace Mortz.Tests.Client;

/// <summary>Base for client service-node tests: loops sent messages straight
/// back into client dispatch and hosts nodes under the headless tree so their
/// lifecycle runs. Swaps the NetTransport.Send static, which is safe here
/// because the Godot headless collection disables parallelization.</summary>
public abstract class NodeServiceTest : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;
    private readonly List<Node> _hosted = [];

    // No assert on the dispatch result: broadcasts can provoke client-to-server
    // replies from live nodes (MatchSetup requests settings on every lobby
    // state), and those fall out of client dispatch as wrong-direction.
    protected NodeServiceTest() =>
        NetTransport.Send = (id, payload, _, _) =>
            NetRegistry.Dispatch(id, 1, payload, isServer: false);

    protected T Host<T>(T node) where T : Node
    {
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
        _hosted.Add(node);
        return node;
    }

    public void Dispose()
    {
        NetTransport.Send = _original;
        foreach (Node node in _hosted)
        {
            node.GetParent()?.RemoveChild(node);
            node.Free();
        }
        // Boundary tests call ResetPeer, which nulls the peer; later tests
        // expect the engine default (offline peer, unique id 1) back.
        NetworkManager.Instance.Multiplayer.MultiplayerPeer ??= new OfflineMultiplayerPeer();
    }
}
