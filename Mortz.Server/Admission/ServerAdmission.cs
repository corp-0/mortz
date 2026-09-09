using Mortz.Core.Identity;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;
using Mortz.Protocol.Net.Names;

namespace Mortz.Server.Admission;

public readonly record struct AdmittedPlayer(int PeerId, string Name, int Skin, VerifiedAccount? Account);

public interface IServerAdmissionTransport
{
    void Offer(int peerId, AdmissionOffer offer);
    /// <summary>Sends acceptance before opening the gameplay gate on the same reliable ordered path.</summary>
    void Accept(int peerId, AdmissionAccepted accepted);
    /// <summary>Closes the gameplay gate, sends the reason, and ends the transport connection.</summary>
    void Reject(int peerId, AdmissionRejected rejected);
}

public class ServerAdmission(IAdmissionVerifier verifier, IServerAdmissionTransport transport, Func<ulong> clock) : IDisposable
{
    private enum Stage { HELLO, PROOF, VERIFICATION, ADMITTED }
    private class Connection(long generation, ulong deadline)
    {
        public long Generation = generation;
        public int Attempt;
        public Stage Stage;
        public ulong Deadline = deadline;
        public string Name = "";
        public int Skin;
        public AdmissionMode Mode;
        public ulong AccountId;
        public bool Reserved;
    }

    private readonly Dictionary<int, Connection> _connections = [];
    private long _generation;
    private bool _disposed;
    private bool _subscribed;
    public int ReservedCount => _connections.Values.Count(connection => connection.Reserved && connection.Stage != Stage.ADMITTED);
    public int AdmittedCount => _connections.Values.Count(connection => connection.Stage == Stage.ADMITTED);
    public event Action<AdmittedPlayer>? Admitted;
    public event Action<int>? Departed;

    public void Connected(int peerId, ulong now)
    {
        if (_disposed)
        {
            transport.Reject(peerId, new AdmissionRejected(0, AdmissionRejection.SERVER_STOPPED));
            return;
        }
        if (!_subscribed)
        {
            verifier.Completed += Verified;
            _subscribed = true;
        }
        if (_connections.ContainsKey(peerId))
        {
            Reject(peerId, AdmissionRejection.INVALID_STATE);
            return;
        }
        _connections.Add(peerId, new Connection(++_generation, AdmissionLimits.Deadline(now, AdmissionLimits.HELLO_MS)));
    }

    public void Hello(int peerId, AdmissionHello hello, ulong now)
    {
        if (!_connections.TryGetValue(peerId, out Connection? connection))
        {
            return;
        }
        if (connection.Stage != Stage.HELLO)
        {
            Reject(peerId, AdmissionRejection.INVALID_STATE);
            return;
        }
        if (now >= connection.Deadline)
        {
            Reject(peerId, AdmissionRejection.HELLO_TIMEOUT);
            return;
        }
        connection.Attempt = 1;
        if (hello.ProtocolVersion != NetConfig.PROTOCOL_VERSION || hello.SchemaHash != NetRegistry.SCHEMA_HASH)
        {
            Reject(peerId, AdmissionRejection.INCOMPATIBLE);
            return;
        }
        if (hello.Name == null || hello.Name.Length > NetConfig.MAX_NAME_LENGTH || hello.Skin is < 0 or >= SimConfig.SKIN_COUNT)
        {
            Reject(peerId, AdmissionRejection.INVALID_PROFILE);
            return;
        }
        if (_connections.Values.Count(item => item.Reserved) >= AdmissionLimits.RESERVATIONS)
        {
            Reject(peerId, AdmissionRejection.SERVER_FULL);
            return;
        }
        connection.Name = PlayerNameSanitizer.Sanitize(hello.Name);
        if (connection.Name.Length == 0)
        {
            connection.Name = $"Player {peerId}";
        }
        connection.Skin = hello.Skin;
        connection.Reserved = true;
        ulong recipient = verifier.Available ? verifier.ServerAccountId : 0;
        connection.Mode = hello.Steam && recipient != 0 ? AdmissionMode.STEAM : AdmissionMode.GUEST;
        connection.Stage = Stage.PROOF;
        connection.Deadline = AdmissionLimits.Deadline(now, AdmissionLimits.PROOF_MS);
        transport.Offer(peerId, new AdmissionOffer(connection.Attempt, connection.Mode,
            connection.Mode == AdmissionMode.STEAM ? recipient : 0));
        if (connection.Mode == AdmissionMode.GUEST)
        {
            Accept(peerId, connection);
        }
    }

    public void Proof(int peerId, SteamProof proof, ulong now)
    {
        if (!_connections.TryGetValue(peerId, out Connection? connection))
        {
            return;
        }
        if (connection.Stage != Stage.PROOF || connection.Mode != AdmissionMode.STEAM || proof.Attempt != connection.Attempt)
        {
            Reject(peerId, AdmissionRejection.INVALID_STATE);
            return;
        }
        if (now >= connection.Deadline)
        {
            Reject(peerId, AdmissionRejection.PROOF_TIMEOUT);
            return;
        }
        if (proof.AccountId == 0 || proof.Ticket.Length is 0 or > AdmissionLimits.TICKET_BYTES)
        {
            Reject(peerId, AdmissionRejection.INVALID_PROOF);
            return;
        }
        connection.AccountId = proof.AccountId;
        connection.Stage = Stage.VERIFICATION;
        connection.Deadline = AdmissionLimits.Deadline(now, AdmissionLimits.VERIFICATION_MS);
        if (!verifier.TryBegin(connection.Generation, proof.AccountId, proof.Ticket, out AdmissionRejection reason))
        {
            Reject(peerId, reason);
        }
    }

    private void Verified(VerificationResult result)
    {
        KeyValuePair<int, Connection> entry = _connections.FirstOrDefault(pair => pair.Value.Generation == result.Generation);
        Connection? connection = entry.Value;
        if (_disposed || connection == null || connection.AccountId != result.AccountId ||
            connection.Stage is not (Stage.VERIFICATION or Stage.ADMITTED))
        {
            return;
        }
        if (connection.Stage == Stage.VERIFICATION && clock() >= connection.Deadline)
        {
            Reject(entry.Key, AdmissionRejection.VERIFICATION_TIMEOUT);
            return;
        }
        if (!result.Accepted)
        {
            Reject(entry.Key, connection.Stage == Stage.ADMITTED ? AdmissionRejection.REVOKED : AdmissionRejection.VERIFICATION_FAILED);
        }
        else if (connection.Stage == Stage.VERIFICATION && connection.Reserved)
        {
            if (!verifier.Available)
            {
                Reject(entry.Key, AdmissionRejection.AUTHENTICATION_UNAVAILABLE);
                return;
            }
            Accept(entry.Key, connection);
        }
    }

    private void Accept(int peerId, Connection connection)
    {
        connection.Stage = Stage.ADMITTED;
        transport.Accept(peerId, new AdmissionAccepted(connection.Attempt, connection.Mode));
        Admitted?.Invoke(new AdmittedPlayer(peerId, connection.Name, connection.Skin,
            connection.Mode == AdmissionMode.STEAM ? new VerifiedAccount(AccountProvider.STEAM, connection.AccountId) : null));
    }

    public void Advance(ulong now)
    {
        foreach ((int peerId, Connection connection) in _connections.ToArray())
        {
            if (connection.Mode == AdmissionMode.STEAM && !verifier.Available)
            {
                Reject(peerId, AdmissionRejection.AUTHENTICATION_UNAVAILABLE);
            }
            else if (connection.Stage != Stage.ADMITTED && now >= connection.Deadline)
            {
                Reject(peerId, connection.Stage switch
                {
                    Stage.HELLO => AdmissionRejection.HELLO_TIMEOUT,
                    Stage.PROOF => AdmissionRejection.PROOF_TIMEOUT,
                    _ => AdmissionRejection.VERIFICATION_TIMEOUT,
                });
            }
        }
    }

    public void Disconnected(int peerId)
    {
        if (!_connections.Remove(peerId, out Connection? connection))
        {
            return;
        }
        verifier.End(connection.Generation);
        if (connection.Stage == Stage.ADMITTED)
        {
            Departed?.Invoke(peerId);
        }
    }

    private void Reject(int peerId, AdmissionRejection reason)
    {
        if (!_connections.TryGetValue(peerId, out Connection? connection))
        {
            return;
        }
        _connections.Remove(peerId);
        transport.Reject(peerId, new AdmissionRejected(connection.Attempt, reason));
        verifier.End(connection.Generation);
        if (connection.Stage == Stage.ADMITTED)
        {
            Departed?.Invoke(peerId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        verifier.Completed -= Verified;
        foreach (int peerId in _connections.Keys.ToArray())
        {
            Reject(peerId, AdmissionRejection.SERVER_STOPPED);
        }
    }
}
