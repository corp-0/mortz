using Mortz.Protocol.Net.Query;

namespace Mortz.Server.Platform;

public interface IServerPlatform : IDisposable
{
    bool Initialize(ServerInfo info, int queryPort, bool isPublic);
    bool AuthenticationAvailable { get; }
    bool PublicationActive { get; }
    string Status { get; }
    void Advance(ServerInfo info);
}
