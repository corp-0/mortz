namespace Mortz.Core.Net;

public enum NetChannel
{
    RELIABLE,
    UNRELIABLE,
}

public enum NetDirection
{
    SERVER_TO_CLIENT,
    CLIENT_TO_SERVER,
}

/// <summary>
/// Marks a partial record struct as a wire message. Mortz.Net.Gen generates
/// the serializer, the direction-appropriate send methods, the static
/// Received event and the registry/dispatch entry; the struct declaration is
/// all a message ever needs by hand. See plans/2026-07-12-net-messages-design.md.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class NetMessageAttribute(NetChannel channel, NetDirection direction) : Attribute
{
    public NetChannel Channel { get; } = channel;
    public NetDirection Direction { get; } = direction;
}
