using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class InputQueueTests
{
    private static PlayerInput In(InputButtons b) => new(b);

    [Fact]
    public void AppliesInputsInSequenceOrder_OnePerTick()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(1, In(InputButtons.Jump));
        q.Enqueue(0, In(InputButtons.Left));

        Assert.Equal(InputButtons.Left, q.Next().Buttons);
        Assert.Equal(0, q.LastAppliedSeq);
        Assert.Equal(InputButtons.Jump, q.Next().Buttons);
        Assert.Equal(1, q.LastAppliedSeq);
    }

    [Fact]
    public void Starvation_RepeatsLastInputWithoutAdvancingAck()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(0, In(InputButtons.Right));
        q.Next();

        Assert.Equal(InputButtons.Right, q.Next().Buttons);
        Assert.Equal(0, q.LastAppliedSeq);
    }

    [Fact]
    public void Gap_SkipsToNextAvailableSequence()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(0, In(InputButtons.Left));
        q.Next();
        q.Enqueue(5, In(InputButtons.Jump)); // 1-4 lost

        Assert.Equal(InputButtons.Jump, q.Next().Buttons);
        Assert.Equal(5, q.LastAppliedSeq);
    }

    [Fact]
    public void StaleAndDuplicateInputs_AreIgnored()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(3, In(InputButtons.Right));
        q.Next();
        q.Enqueue(3, In(InputButtons.Left)); // redundant re-send of applied seq
        q.Enqueue(1, In(InputButtons.Left)); // older than applied

        Assert.Equal(InputButtons.Right, q.Next().Buttons); // starved: repeats
    }

    [Fact]
    public void Backlog_IsBoundedSoItCannotAddPermanentLatency()
    {
        InputQueue q = new InputQueue();
        for (int seq = 0; seq < 20; seq++)
            q.Enqueue(seq, In((InputButtons)(seq % 8)));

        q.Next();
        // The oldest surviving pending input is at most MAX_PENDING behind the newest.
        Assert.True(q.LastAppliedSeq >= 19 - InputQueue.MAX_PENDING);
    }
}
