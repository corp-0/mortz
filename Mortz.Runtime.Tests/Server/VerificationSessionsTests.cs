using Mortz.Protocol.Net.Admission;
using Mortz.Server.Admission;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class VerificationSessionsTests
{
    [Fact]
    public void EndRemovesRegistrationBeforeNativeCleanup()
    {
        Backend backend = new();
        using VerificationSessions sessions = new(backend);
        int completions = 0;
        sessions.Completed += _ => completions++;
        Assert.True(sessions.TryBegin(1, 10, [1], out _));
        backend.OnEnd = account => sessions.Result(account, true);
        sessions.End(1);
        sessions.End(1);
        Assert.Equal(0, completions);
        Assert.Equal(new[] { 10UL }, backend.Ends);
        Assert.False(sessions.TryBegin(2, 10, [1], out _));
        sessions.Pump(() => sessions.Result(10, true));
        Assert.True(sessions.TryBegin(2, 10, [1], out _));
    }

    [Fact]
    public void CleanupDuringCallbackRequiresAnotherFullUnregisteredBatch()
    {
        Backend backend = new();
        using VerificationSessions sessions = new(backend);
        Assert.True(sessions.TryBegin(1, 10, [1], out _));
        sessions.Completed += _ => sessions.End(1);
        sessions.Pump(() => sessions.Result(10, false));
        Assert.False(sessions.TryBegin(2, 10, [1], out _));
        sessions.Pump(() => sessions.Result(10, true));
        Assert.True(sessions.TryBegin(2, 10, [1], out _));
    }

    [Fact]
    public void CallbackCannotRegisterReplacementOrReenterPump()
    {
        Backend backend = new();
        using VerificationSessions sessions = new(backend);
        Assert.True(sessions.TryBegin(1, 10, [1], out _));
        int reentered = 0;
        sessions.Completed += result =>
        {
            sessions.End(1);
            sessions.Pump(() => reentered++);
            Assert.False(sessions.TryBegin(2, 10, [1], out _));
            Assert.False(sessions.TryBegin(3, 20, [1], out _));
        };
        sessions.Pump(() => sessions.Result(10, true));
        Assert.Equal(0, reentered);
        Assert.False(sessions.TryBegin(2, 10, [1], out _));
        sessions.Pump(() => { });
        Assert.True(sessions.TryBegin(2, 10, [1], out _));
    }

    [Fact]
    public void FailedPumpDoesNotReleaseReuseBarrier()
    {
        Backend backend = new();
        using VerificationSessions sessions = new(backend);
        Assert.True(sessions.TryBegin(1, 10, [1], out _));
        sessions.End(1);
        Assert.Throws<InvalidOperationException>(() => sessions.Pump(() => throw new InvalidOperationException()));
        Assert.False(sessions.TryBegin(2, 10, [1], out _));
        sessions.Pump(() => { });
        Assert.True(sessions.TryBegin(2, 10, [1], out _));
    }

    [Fact]
    public void UnknownOrDisposedCallbacksHaveNoOwner()
    {
        Backend backend = new();
        VerificationSessions sessions = new(backend);
        int completions = 0;
        sessions.Completed += _ => completions++;
        sessions.Result(10, true);
        Assert.True(sessions.TryBegin(1, 10, [1], out _));
        sessions.Dispose();
        sessions.Result(10, true);
        sessions.Dispose();
        Assert.Equal(0, completions);
        Assert.Single(backend.Ends);
        Assert.False(sessions.TryBegin(2, 10, [1], out AdmissionRejection reason));
        Assert.Equal(AdmissionRejection.AUTHENTICATION_UNAVAILABLE, reason);
    }

    private class Backend : IVerificationBackend
    {
        public bool Available => true;
        public ulong ServerAccountId => 99;
        public readonly List<ulong> Ends = [];
        public Action<ulong>? OnEnd;
        public bool Begin(byte[] ticket, ulong accountId) => true;
        public void End(ulong accountId) { Ends.Add(accountId); OnEnd?.Invoke(accountId); }
    }
}
