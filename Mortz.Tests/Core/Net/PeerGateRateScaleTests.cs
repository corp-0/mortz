using Mortz.Core.Net;
using Mortz.Core.Net.Abuse;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>The documented budgets are what production ships; rateScale only
/// exists so an E2E process at timescale is not throttled by wall-clock refill.</summary>
public class PeerGateRateScaleTests
{
    private const int INPUT_CAPACITY = 120;
    private const int INPUT_PER_SECOND = 60;
    private const int MESSAGE_CAPACITY = 64;

    /// <summary>Drains a full bucket at a fixed instant and reports how many
    /// calls the gate allowed before refusing.</summary>
    private static int DrainInputs(PeerGate gate, int peerId, int attempts)
    {
        int allowed = 0;
        for (int i = 0; i < attempts; i++)
        {
            if (gate.AllowInput(peerId, nowMs: 0))
                allowed++;
        }
        return allowed;
    }

    private static int DrainMessages(PeerGate gate, int peerId, int attempts)
    {
        int allowed = 0;
        for (int i = 0; i < attempts; i++)
        {
            if (gate.AllowMessage(peerId, nowMs: 0, cost: 1))
                allowed++;
        }
        return allowed;
    }

    [Fact]
    public void DefaultGateEnforcesTheDocumentedBudgets()
    {
        PeerGate gate = new();
        gate.Connected(7, nowMs: 0);

        Assert.Equal(INPUT_CAPACITY, DrainInputs(gate, 7, INPUT_CAPACITY + 20));
        Assert.Equal(MESSAGE_CAPACITY, DrainMessages(gate, 7, MESSAGE_CAPACITY + 20));
    }

    [Fact]
    public void DefaultGateRefillsAtTheDocumentedRate()
    {
        PeerGate gate = new();
        gate.Connected(7, nowMs: 0);
        DrainInputs(gate, 7, INPUT_CAPACITY);

        int refilled = 0;
        for (int i = 0; i < INPUT_PER_SECOND + 10; i++)
        {
            if (gate.AllowInput(7, nowMs: 1000))
                refilled++;
        }

        Assert.Equal(INPUT_PER_SECOND, refilled);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void RateScaleScalesBothBucketsLinearly(int scale)
    {
        PeerGate gate = new(rateScale: scale);
        gate.Connected(7, nowMs: 0);

        Assert.Equal(INPUT_CAPACITY * scale, DrainInputs(gate, 7, INPUT_CAPACITY * scale + 20));
        Assert.Equal(MESSAGE_CAPACITY * scale, DrainMessages(gate, 7, MESSAGE_CAPACITY * scale + 20));
    }

    [Fact]
    public void RateScaleScalesTheRefillToo()
    {
        PeerGate gate = new(rateScale: 4);
        gate.Connected(7, nowMs: 0);
        DrainInputs(gate, 7, INPUT_CAPACITY * 4);

        int refilled = DrainInputsAt(gate, 7, nowMs: 1000, attempts: INPUT_PER_SECOND * 4 + 10);

        Assert.Equal(INPUT_PER_SECOND * 4, refilled);
    }

    private static int DrainInputsAt(PeerGate gate, int peerId, ulong nowMs, int attempts)
    {
        int allowed = 0;
        for (int i = 0; i < attempts; i++)
        {
            if (gate.AllowInput(peerId, nowMs))
                allowed++;
        }
        return allowed;
    }
}
