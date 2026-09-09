namespace Mortz.Server.Platform;

public readonly record struct ServerDatagram(byte[] Data, string Address, int Port);

public interface IServerPacketRouter
{
    bool HandleIncoming(byte[] packet, string address, int port);
    ServerDatagram? TakeOutgoing();
}
