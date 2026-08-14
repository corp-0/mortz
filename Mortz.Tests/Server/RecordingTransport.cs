using Mortz.Core.Net;
using Mortz.Core.Net.Stats;
using Mortz.Server;

namespace Mortz.Tests.Server;

/// <summary>One thing the server put on the wire. Target 0 means broadcast.</summary>
internal readonly record struct Sent(int Target, object Message);

internal readonly record struct SentSnapshot(int Target, byte[] Data, int Ack);

/// <summary>Every byte the server transport emits, in a list.</summary>
internal sealed class RecordingTransport : IServerTransport
{
    public List<Sent> Messages { get; } = [];

    public List<SentSnapshot> Snapshots { get; } = [];

    public List<int> Disconnected { get; } = [];

    /// <summary>The wire as an ordered, readable sequence: "all:RosterMsg",
    /// "7:MatchLoadMsg". Order is the assertion in most flow tests.</summary>
    public string[] Trace() => [.. Messages.Select(Describe)];

    public T Last<T>() => Messages.Select(sent => sent.Message).OfType<T>().Last();

    public void Send<TMsg>(int peerId, in TMsg message) where TMsg : struct, INetMessage<TMsg> =>
        Messages.Add(new Sent(peerId, message));

    public void Broadcast<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg> =>
        Messages.Add(new Sent(0, message));

    public void Disconnect(int peerId, string reason) => Disconnected.Add(peerId);

    public int BroadcastSnapshot(Func<int, byte[]> dataFor, Func<int, int> ackFor) => 0;

    public int SendSnapshot(int peerId, byte[] data, int ack)
    {
        Snapshots.Add(new SentSnapshot(peerId, data, ack));
        return data.Length + sizeof(int);
    }

    public PeerPing[] PeerPings() => [];

    public WireStats PopWireStats() => default;

    private static string Describe(Sent sent) =>
        sent.Target == 0
            ? $"all:{sent.Message.GetType().Name}"
            : $"{sent.Target}:{sent.Message.GetType().Name}";
}
