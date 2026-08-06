namespace Mortz.Core.Net;

/// <summary>
/// The wire identity of a generated [NetMessage]. Lets a sender take the id,
/// channel and codec straight off the type parameter, so a link needs no
/// per-message overload and no reflection.
/// </summary>
public interface INetMessage<TSelf>
    where TSelf : struct, INetMessage<TSelf>
{
    static abstract ushort MsgId { get; }

    static abstract NetChannel MsgChannel { get; }

    static abstract byte[] Serialize(in TSelf message);
}
