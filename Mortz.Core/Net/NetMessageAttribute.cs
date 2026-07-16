namespace Mortz.Core.Net;

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
