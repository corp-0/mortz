using System.Net;

namespace Mortz.Protocol.Net.Query;

public readonly record struct ServerEndpoint
{
    public string Address { get; }
    public int Port { get; }
    public int QueryPort { get; }

    public ServerEndpoint(string Address, int Port) : this(Address, Port, ServerQueryProtocol.QueryPort(Port)) { }

    public ServerEndpoint(string Address, int Port, int QueryPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Address);
        ArgumentOutOfRangeException.ThrowIfLessThan(Port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(QueryPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(QueryPort, ushort.MaxValue);
        if (Port == QueryPort)
            throw new ArgumentException("Game and query ports must differ.", nameof(QueryPort));
        string host = Address.Trim().Trim('[', ']');
        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }
            this.Address = ip.ToString();
        }
        else
        {
            this.Address = host.TrimEnd('.').ToLowerInvariant();
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(this.Address);
        this.Port = Port;
        this.QueryPort = QueryPort;
    }

    public override string ToString() => $"{Address}:{Port}";
}
