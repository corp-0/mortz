using Mortz.Protocol.Net;
using Mortz.Runtime.Tests.Net;

namespace Mortz.Runtime.Tests.Client;

public abstract class RuntimeServiceTest : IDisposable
{
    private readonly List<object> _features = [];
    protected TestClientSender Sender { get; } = new();
    protected NetRouter Router { get; } = new();

    protected T RegisterRuntime<T>(T feature) where T : class
    {
        Router.Add(feature);
        _features.Add(feature);
        return feature;
    }

    public void Dispose()
    {
        foreach (object feature in _features)
        {
            Router.Remove(feature);
            if (feature is IDisposable disposable)
                disposable.Dispose();
        }
        _features.Clear();
    }
}
