using Mortz.Protocol.Net.Admission;

namespace Mortz.Server.Admission;

public readonly record struct VerificationResult(long Generation, ulong AccountId, bool Accepted);

public interface IAdmissionVerifier
{
    bool Available { get; }
    ulong ServerAccountId { get; }
    event Action<VerificationResult>? Completed;
    bool TryBegin(long generation, ulong accountId, byte[] ticket, out AdmissionRejection reason);
    void End(long generation);
}

public class GuestAdmissionVerifier : IAdmissionVerifier
{
    public bool Available => false;
    public ulong ServerAccountId => 0;
    public event Action<VerificationResult>? Completed { add { } remove { } }
    public bool TryBegin(long generation, ulong accountId, byte[] ticket, out AdmissionRejection reason)
    {
        reason = AdmissionRejection.AUTHENTICATION_UNAVAILABLE;
        return false;
    }
    public void End(long generation) { }
}

public interface IVerificationBackend
{
    bool Available { get; }
    ulong ServerAccountId { get; }
    bool Begin(byte[] ticket, ulong accountId);
    void End(ulong accountId);
}

/// <summary>Account registrations remain unavailable for reuse until an unregistered callback batch finishes.</summary>
public class VerificationSessions(IVerificationBackend backend) : IAdmissionVerifier, IDisposable
{
    private readonly Dictionary<ulong, long> _registrations = [];
    private readonly HashSet<ulong> _draining = [];
    private bool _pumping;
    private bool _disposed;
    public bool Available => !_disposed && backend.Available;
    public ulong ServerAccountId => Available ? backend.ServerAccountId : 0;
    public event Action<VerificationResult>? Completed;

    public bool TryBegin(long generation, ulong accountId, byte[] ticket, out AdmissionRejection reason)
    {
        reason = AdmissionRejection.AUTHENTICATION_UNAVAILABLE;
        if (!Available)
        {
            return false;
        }
        reason = AdmissionRejection.DUPLICATE_ACCOUNT;
        if (_pumping || _registrations.ContainsKey(accountId) || _draining.Contains(accountId) ||
            _registrations.ContainsValue(generation))
        {
            return false;
        }
        reason = AdmissionRejection.INVALID_PROOF;
        if (accountId == 0 || ticket.Length is 0 or > AdmissionLimits.TICKET_BYTES)
        {
            return false;
        }
        _registrations.Add(accountId, generation);
        bool started;
        try { started = backend.Begin(ticket, accountId); }
        catch { started = false; }
        if (started)
        {
            return true;
        }
        _registrations.Remove(accountId);
        reason = AdmissionRejection.VERIFICATION_FAILED;
        return false;
    }

    public void Result(ulong accountId, bool accepted)
    {
        if (!_disposed && _registrations.TryGetValue(accountId, out long generation))
        {
            Completed?.Invoke(new VerificationResult(generation, accountId, accepted));
        }
    }

    public void End(long generation)
    {
        ulong accountId = _registrations.FirstOrDefault(pair => pair.Value == generation).Key;
        if (accountId == 0 || !_registrations.Remove(accountId))
        {
            return;
        }
        _draining.Add(accountId);
        backend.End(accountId);
    }

    public void Pump(Action callbacks)
    {
        if (_disposed || _pumping)
        {
            return;
        }
        // Cleanup during this batch needs another full pump while unregistered.
        ulong[] draining = [.. _draining];
        _pumping = true;
        try
        {
            callbacks();
            foreach (ulong account in draining)
            {
                _draining.Remove(account);
            }
        }
        finally { _pumping = false; }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (long generation in _registrations.Values.ToArray())
        {
            End(generation);
        }
        _draining.Clear();
    }
}
