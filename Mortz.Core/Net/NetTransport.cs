namespace Mortz.Core.Net;

/// <summary>
/// The one seam between the engine-free message layer and whatever moves the
/// bytes. NetworkManager assigns Send at boot; tests point it at a loopback
/// dispatch instead, so the whole protocol round-trips without a socket.
/// </summary>
public static class NetTransport
{
    /// <summary>Send target: every validated peer.</summary>
    public const int BROADCAST = 0;
    /// <summary>Send target: the server (Godot peer id 1).</summary>
    public const int TO_SERVER = 1;

    public delegate void SendDelegate(ushort msgId, byte[] payload, int target, NetChannel channel);

    public static SendDelegate Send { get; set; } =
        static (_, _, _, _) => throw new InvalidOperationException("NetTransport.Send not assigned");
}
