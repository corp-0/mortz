using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Mortz.Core.Net.Query;

/// <summary>Decides what a probe datagram means: nonces, pending table,
/// deadlines, broadcast window. Callers supply monotonic milliseconds.</summary>
public sealed class ProbeTracker(Func<uint>? nonceSource = null)
{
    public const int TIMEOUT_MS = 2_000;

    /// <summary>Extra headroom while a hostname is still resolving; tightened
    /// to TIMEOUT_MS once the probe goes out.</summary>
    public const int RESOLVE_TIMEOUT_MS = 4_000;

    private sealed class Pending(ServerEndpoint endpoint, ulong deadlineMs)
    {
        public readonly ServerEndpoint Endpoint = endpoint;
        public ulong DeadlineMs = deadlineMs;
        public ulong SentAtMs;
        public string ReplyAddress = "";
        public bool Sent;
    }

    private readonly Dictionary<uint, Pending> _pending = [];
    private readonly Func<uint> _nonces = nonceSource ?? NextCryptoNonce;
    private uint _broadcastNonce;
    private ulong _broadcastSentAtMs;
    private int _broadcastPort;

    public event Action<ServerProbeReply>? Replied;

    public event Action<uint, ServerEndpoint>? TimedOut;

    /// <summary>A server answered a broadcast we did not address to it.</summary>
    public event Action<ServerProbeReply>? Discovered;

    public uint Track(ServerEndpoint endpoint, ulong nowMs)
    {
        uint nonce = NextNonce();
        _pending[nonce] = new Pending(endpoint, nowMs + RESOLVE_TIMEOUT_MS);
        return nonce;
    }

    /// <summary>The datagram went out. From here only replyAddress, on the
    /// endpoint's query port, can claim this nonce.</summary>
    public void MarkSent(uint nonce, string replyAddress, ulong nowMs)
    {
        if (!_pending.TryGetValue(nonce, out Pending? pending))
            return;
        pending.Sent = true;
        pending.SentAtMs = nowMs;
        pending.ReplyAddress = replyAddress;
        pending.DeadlineMs = nowMs + TIMEOUT_MS;
    }

    /// <summary>The probe can never be answered, so report it now instead of
    /// waiting out the deadline.</summary>
    public void Fail(uint nonce)
    {
        if (_pending.Remove(nonce, out Pending? pending))
            TimedOut?.Invoke(nonce, pending.Endpoint);
    }

    public uint BeginBroadcast(ulong nowMs, int queryPort)
    {
        _broadcastNonce = NextNonce();
        _broadcastSentAtMs = nowMs;
        _broadcastPort = queryPort;
        return _broadcastNonce;
    }

    /// <summary>Drops everything in flight without reporting it, so a stale
    /// timeout cannot overwrite a fresh reply after a refresh.</summary>
    public void Cancel()
    {
        _pending.Clear();
        _broadcastNonce = 0;
    }

    public void OnResponse(byte[] datagram, string sourceAddress, int sourcePort, ulong nowMs)
    {
        if (!ServerQueryProtocol.TryDecodeResponse(datagram, out ServerQueryReply reply))
            return;
        if (_pending.TryGetValue(reply.Nonce, out Pending? pending))
        {
            // Unsent nonce or wrong source means a forgery. Drop it but keep
            // the pending, so the real server can still answer in time.
            if (!pending.Sent || sourceAddress != pending.ReplyAddress ||
                sourcePort != pending.Endpoint.QueryPort)
                return;
            _pending.Remove(reply.Nonce);
            Replied?.Invoke(new ServerProbeReply(pending.Endpoint, reply.Info,
                (int)(nowMs - pending.SentAtMs)));
            return;
        }
        if (reply.Nonce == 0 || reply.Nonce != _broadcastNonce ||
            sourcePort != _broadcastPort || nowMs > _broadcastSentAtMs + TIMEOUT_MS)
            return;
        // The datagram says where it came from, the payload says which port to join.
        Discovered?.Invoke(new ServerProbeReply(
            new ServerEndpoint(sourceAddress, reply.Info.GamePort), reply.Info,
            (int)(nowMs - _broadcastSentAtMs)));
    }

    public void Expire(ulong nowMs)
    {
        foreach (uint nonce in _pending
                     .Where(entry => nowMs > entry.Value.DeadlineMs)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            Fail(nonce);
        }
    }

    private uint NextNonce()
    {
        uint nonce;
        do
        {
            nonce = _nonces();
        } while (nonce == 0 || nonce == _broadcastNonce || _pending.ContainsKey(nonce));
        return nonce;
    }

    private static uint NextCryptoNonce()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
}
