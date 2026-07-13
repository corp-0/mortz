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

    [Fact]
    public void Backlog_DrainsToOneBufferedInput_ThenStaysThere()
    {
        InputQueue q = new InputQueue();
        for (int seq = 0; seq < 3; seq++)
            q.Enqueue(seq, In(InputButtons.None));

        // Steady state: one new input arrives per tick, like a live client.
        for (int seq = 3; seq < 8; seq++)
        {
            q.Enqueue(seq, In(InputButtons.None));
            q.Next();
        }

        Assert.Equal(1, q.PendingCount);
        Assert.Equal(6, q.LastAppliedSeq); // caught up to one behind the newest
    }

    [Fact]
    public void Drain_CarriesTheOvertakenButtons_IntoTheAppliedInput()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(0, In(InputButtons.Left));
        q.Enqueue(1, In(InputButtons.Right));
        q.Enqueue(2, In(InputButtons.Jump));

        // Seq 0 is overtaken but its buttons survive; the carry lasts one tick.
        Assert.Equal(InputButtons.Left | InputButtons.Right, q.Next().Buttons);
        Assert.Equal(1, q.LastAppliedSeq); // ack covers the overtaken input too
        Assert.Equal(1, q.PendingCount);
        Assert.Equal(InputButtons.Jump, q.Next().Buttons);
    }

    [Fact]
    public void FireSeq_PointsAtThePressTick_EvenWhenOvertaken()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(0, In(InputButtons.None));
        q.Next();
        Assert.Equal(-1, q.FireSeq);

        // The press tick gets overtaken by the drain; its edge keeps its seq.
        q.Enqueue(1, In(InputButtons.Fire));
        q.Enqueue(2, In(InputButtons.Fire)); // click held into the next tick
        q.Enqueue(3, In(InputButtons.None));
        Assert.True(q.Next().Fire);
        Assert.Equal(1, q.FireSeq);
        Assert.Equal(2, q.LastAppliedSeq);

        // A fresh click later moves it; a plain applied press works too.
        q.Enqueue(4, In(InputButtons.Fire));
        q.Next(); // seq 3, releases
        q.Next(); // seq 4, presses
        Assert.Equal(4, q.FireSeq);
    }
}
