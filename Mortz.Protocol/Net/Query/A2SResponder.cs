using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace Mortz.Protocol.Net.Query;

public class A2SResponder
{
    public const ulong CHALLENGE_LIFETIME_MS = 30_000;
    private readonly byte[] _secret;
    private readonly ServerQueryRateLimiter _limiter;
    private uint _responseId;

    public A2SResponder(ServerQueryRateLimiter? limiter = null, byte[]? secret = null)
    {
        _secret = secret?.ToArray() ?? RandomNumberGenerator.GetBytes(32);
        if (_secret.Length < 32)
            throw new ArgumentException("Challenge secrets require at least 32 bytes.", nameof(secret));
        _limiter = limiter ?? new ServerQueryRateLimiter();
    }

    public IReadOnlyList<byte[]> Respond(byte[] packet, string sourceAddress, int sourcePort, ulong nowMs, ServerInfo info)
    {
        if (!ServerQueryProtocol.TryDecodeRequest(packet, out byte kind, out int? challenge) ||
            !IPAddress.TryParse(sourceAddress, out IPAddress? address) || sourcePort is < 1 or > ushort.MaxValue)
            return [];
        string sourceKey = address.MapToIPv6().ToString();
        if (!_limiter.Allow(sourceKey, nowMs))
            return [];
        ulong second = nowMs / 1_000;
        if (!challenge.HasValue || !ValidChallenge(address, sourcePort, challenge.Value, second))
            return [ServerQueryProtocol.EncodeChallenge(Token(address, sourcePort, second))];
        byte[] response = kind == ServerQueryProtocol.INFO_REQUEST
            ? ServerQueryProtocol.EncodeInfoResponse(info)
            : ServerQueryProtocol.EncodeRulesResponse(info);
        IReadOnlyList<byte[]> fragments = ServerQueryProtocol.SplitResponse(response, ++_responseId);
        List<byte[]> allowed = new(fragments.Count) { fragments[0] };
        for (int i = 1; i < fragments.Count; i++)
        {
            if (!_limiter.Allow(sourceKey, nowMs))
                break;
            allowed.Add(fragments[i]);
        }
        return allowed;
    }

    private bool ValidChallenge(IPAddress address, int port, int challenge, ulong second)
    {
        // Tokens are stateless; checking each possible issue second gives a strict 30-second ceiling.
        for (ulong age = 0; age < CHALLENGE_LIFETIME_MS / 1_000 && age <= second; age++)
        {
            if (Token(address, port, second - age) == challenge)
                return true;
        }
        return false;
    }

    private int Token(IPAddress address, int port, ulong second)
    {
        Span<byte> input = stackalloc byte[26];
        address.MapToIPv6().GetAddressBytes().CopyTo(input);
        BinaryPrimitives.WriteUInt16LittleEndian(input[16..], (ushort)port);
        BinaryPrimitives.WriteUInt64LittleEndian(input[18..], second);
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(_secret, input, hash);
        int token = BinaryPrimitives.ReadInt32LittleEndian(hash);
        return token == -1 ? 0 : token;
    }
}
