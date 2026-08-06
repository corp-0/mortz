namespace Mortz.Server;

/// <summary>"Now" for code that runs outside Advance. Written once per Advance.</summary>
public sealed class ServerClock
{
    public ulong Ms { get; set; }
}
