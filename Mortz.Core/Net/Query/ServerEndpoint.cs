namespace Mortz.Core.Net.Query;

/// <summary>Address plus the port players join. The query port is derived, so
/// saved favorites survive a change to the convention.</summary>
public readonly record struct ServerEndpoint(string Address, int Port)
{
    public int QueryPort => ServerQueryProtocol.QueryPort(Port);

    public override string ToString() => $"{Address}:{Port}";
}
