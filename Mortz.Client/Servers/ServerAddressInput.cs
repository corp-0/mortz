using System.Net;
using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

public enum ServerAddressError
{
    NONE,
    ADDRESS_REQUIRED,
    INVALID_PORT,
    QUERY_PORT_REQUIRED,
    INVALID_QUERY_PORT,
    SAME_PORT,
}

public static class ServerAddressInput
{
    public static bool TryReadGame(string addressText, string portText,
        out string address, out int port, out ServerAddressError error)
    {
        address = addressText.Trim();
        port = 0;
        error = ServerAddressError.NONE;
        if (address.StartsWith('['))
        {
            int end = address.IndexOf(']');
            if (end < 0 || (end + 1 < address.Length && address[end + 1] != ':'))
            {
                error = ServerAddressError.ADDRESS_REQUIRED;
                return false;
            }
            if (end + 1 < address.Length)
            {
                portText = address[(end + 2)..];
            }
            address = address[1..end];
        }
        else if (!IPAddress.TryParse(address, out _))
        {
            int colon = address.LastIndexOf(':');
            if (colon >= 0)
            {
                portText = address[(colon + 1)..];
                address = address[..colon].Trim();
            }
        }
        if (address.Length == 0)
        {
            error = ServerAddressError.ADDRESS_REQUIRED;
            return false;
        }
        if (!int.TryParse(portText, out port) || port is < 1 or > ushort.MaxValue)
        {
            error = ServerAddressError.INVALID_PORT;
            return false;
        }
        return true;
    }

    public static bool TryReadQuery(string addressText, string portText, string queryText,
        out ServerEndpoint endpoint, out ServerAddressError error)
    {
        endpoint = default;
        if (!TryReadGame(addressText, portText, out string address, out int port, out error))
        {
            return false;
        }
        int queryPort;
        if (string.IsNullOrWhiteSpace(queryText))
        {
            if (port == ushort.MaxValue)
            {
                error = ServerAddressError.QUERY_PORT_REQUIRED;
                return false;
            }
            queryPort = ServerQueryProtocol.QueryPort(port);
        }
        else if (!int.TryParse(queryText, out queryPort) || queryPort is < 1 or > ushort.MaxValue)
        {
            error = ServerAddressError.INVALID_QUERY_PORT;
            return false;
        }
        if (queryPort == port)
        {
            error = ServerAddressError.SAME_PORT;
            return false;
        }
        endpoint = new ServerEndpoint(address, port, queryPort);
        return true;
    }
}
