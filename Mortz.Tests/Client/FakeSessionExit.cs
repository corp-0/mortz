using Mortz.Client.Session;

namespace Mortz.Tests.Client;

/// <summary>Test-driven ISessionExit: records why the session was left.</summary>
public sealed class FakeSessionExit : ISessionExit
{
    public List<string> Reasons { get; } = [];

    public void LeaveSession(string reason) => Reasons.Add(reason);
}
