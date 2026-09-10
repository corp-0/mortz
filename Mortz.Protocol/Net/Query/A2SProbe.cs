using System.Net;

namespace Mortz.Protocol.Net.Query;

/// <summary>One endpoint's INFO and RULES exchange; its transport owns an isolated socket.</summary>
public class A2SProbe(ServerEndpoint endpoint, string resolvedAddress, ulong startedAtMs, bool discoverGamePort = false)
{
    public const int TIMEOUT_MS = 3_000;
    public const int MAX_CONCURRENT = 16;
    private A2SReassembler _response = new();
    private int _challengeRetries;
    private ulong _infoRequestedAtMs;
    private int _pingMs;
    private bool _started;
    private ServerInfo? _info;
    public bool IsComplete { get; private set; }
    public ServerProbeReply? Result { get; private set; }
    public bool HasExpired(ulong nowMs) => nowMs - startedAtMs >= TIMEOUT_MS;

    public byte[] StartRequest()
    {
        if (_started)
            throw new InvalidOperationException("The endpoint probe has already started.");
        _started = true;
        _infoRequestedAtMs = startedAtMs;
        return ServerQueryProtocol.EncodeInfoRequest();
    }

    public byte[]? Receive(byte[] packet, string sourceAddress, int sourcePort, ulong nowMs)
    {
        if (IsComplete || !_started || HasExpired(nowMs) || sourcePort != endpoint.QueryPort ||
            !IPAddress.TryParse(sourceAddress, out IPAddress? source) ||
            !IPAddress.TryParse(resolvedAddress, out IPAddress? expected) ||
            !source.MapToIPv6().Equals(expected.MapToIPv6()))
            return null;
        if (ServerQueryProtocol.TryDecodeChallenge(packet, out int challenge))
        {
            if (_challengeRetries++ >= ServerQueryProtocol.MAX_CHALLENGE_RETRIES)
            {
                Finish();
                return null;
            }
            _response = new A2SReassembler();
            if (_info == null)
            {
                _infoRequestedAtMs = nowMs;
                return ServerQueryProtocol.EncodeInfoRequest(challenge);
            }
            return ServerQueryProtocol.EncodeRulesRequest(challenge);
        }
        if (!_response.TryAdd(packet, out byte[] response))
            return null;
        if (_info == null)
        {
            if (!ServerQueryProtocol.TryDecodeInfo(response, endpoint.Port, out ServerInfo info) ||
                discoverGamePort && info.GamePort == endpoint.QueryPort)
                return null;
            _info = info;
            // Measure one INFO round trip, excluding challenges and the later RULES exchange.
            _pingMs = (int)Math.Min(nowMs - _infoRequestedAtMs, int.MaxValue);
            _response = new A2SReassembler();
            _challengeRetries = 0;
            return ServerQueryProtocol.EncodeRulesRequest();
        }
        if (ServerQueryProtocol.TryDecodeRules(response, out Dictionary<string, string> rules))
        {
            _info = ServerQueryMetadata.ApplyRules(_info, rules);
            Finish();
        }
        return null;
    }

    /// <summary>Retains readable INFO as unknown compatibility when RULES fail or time out.</summary>
    public void Finish()
    {
        if (IsComplete)
            return;
        IsComplete = true;
        if (_info != null)
        {
            // Explicit endpoints may use a forwarded port that differs from the server's local INFO port.
            ServerEndpoint resultEndpoint = endpoint;
            if (discoverGamePort)
            {
                resultEndpoint = new ServerEndpoint(endpoint.Address, _info.GamePort, endpoint.QueryPort);
            }
            Result = new ServerProbeReply(resultEndpoint, _info, _pingMs, resolvedAddress);
        }
    }
}
