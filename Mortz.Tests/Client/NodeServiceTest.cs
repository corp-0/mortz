using Chickensoft.AutoInject;
using Godot;
using Mortz.Core.Net;
using Mortz.Tests.Net;

namespace Mortz.Tests.Client;

/// <summary>Base for client service-node tests: loops sent messages straight
/// back into the client router and hosts nodes under the headless tree so
/// their lifecycle runs.</summary>
public abstract class NodeServiceTest : IDisposable
{
    private readonly List<Node> _hosted = [];

    /// <summary>The client-side router every hosted handler registers with,
    /// the same instance the loopback dispatches into.</summary>
    protected NetRouter Router { get; } = new();

    protected TestClientSender Sender { get; }

    // No assert on the dispatch result: broadcasts can provoke client-to-server
    // replies from live nodes (MatchSetup requests settings on every lobby
    // state), and those fall out of client dispatch as wrong-direction.
    protected NodeServiceTest() =>
        Sender = new TestClientSender((id, payload, _) => Router.Dispatch(id, payload));

    protected T Host<T>(T node) where T : Node
    {
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
        _hosted.Add(node);
        return node;
    }

    /// <summary>Host with the router dependency already faked, for nodes whose
    /// only dependency is the router.</summary>
    protected T HostRouted<T>(T node) where T : Node, IDependent
    {
        node.FakeDependency(Router);
        return Host(node);
    }

    public void Dispose()
    {
        foreach (Node node in _hosted)
        {
            node.GetParent()?.RemoveChild(node);
            node.Free();
        }
    }
}
