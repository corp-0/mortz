using Mortz.Core.Input;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Input;

/// <summary>Admission has to be exact both ways: never in an honest client's
/// way, and unforgiving to a fabricated rate.</summary>
public class InputAdmissionTests
{
    private static PlayerInput In(InputButtons b) => new(b);

    [Fact]
    public void HonestRate_IsNeverRefused()
    {
        InputQueue q = new InputQueue();
        for (int tick = 0; tick < 600; tick++)
        {
            q.Enqueue(tick, In(InputButtons.NONE));
            q.Next();
            Assert.Equal(tick, q.LastAppliedSeq);
        }
    }

    /// <summary>Every packet re-sends the newest few inputs, so charging for
    /// re-sends would throttle honest play.</summary>
    [Fact]
    public void RedundantResends_CostNothing()
    {
        InputQueue q = new InputQueue();
        for (int tick = 0; tick < 600; tick++)
        {
            for (int seq = Math.Max(0, tick - 3); seq <= tick; seq++)
            {
                q.Enqueue(seq, In(InputButtons.NONE));
            }
            q.Next();
            Assert.Equal(tick, q.LastAppliedSeq);
        }
    }

    /// <summary>Packets that bunched up land together and every sequence in
    /// them is taken.</summary>
    [Fact]
    public void ABunchTheSizeOfTheAllowance_IsAdmittedWhole()
    {
        InputQueue q = new InputQueue();
        for (int seq = 0; seq < InputQueue.BURST_SEQS; seq++)
        {
            q.Enqueue(seq, In(InputButtons.NONE));
        }
        Assert.Equal(InputQueue.BURST_SEQS, q.PendingCount);

        q.Enqueue(InputQueue.BURST_SEQS, In(InputButtons.NONE));
        Assert.Equal(InputQueue.BURST_SEQS, q.PendingCount); // one past: refused
    }

    /// <summary>A refusal is not a loss: it is never acked, so the client
    /// re-sends it and the next tick's token takes it.</summary>
    [Fact]
    public void ARefusedSequence_IsTakenOnItsResend()
    {
        InputQueue q = new InputQueue();
        for (int seq = 0; seq <= InputQueue.BURST_SEQS; seq++)
        {
            q.Enqueue(seq, In(InputButtons.NONE)); // last one refused
        }
        Assert.Equal(InputQueue.BURST_SEQS, q.PendingCount);

        q.Next(); // consumes two, refills one token
        q.Enqueue(InputQueue.BURST_SEQS, In(InputButtons.NONE));
        Assert.Equal(InputQueue.BURST_SEQS - 1, q.PendingCount);
    }

    /// <summary>One admitted sequence a million ahead must not make the
    /// numbers beneath it free.</summary>
    [Fact]
    public void AWatermarkJump_DoesNotMakeBackfillFree()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(1_000_000, In(InputButtons.NONE));
        Assert.Equal(1, q.PendingCount);

        for (int seq = 0; seq < 100; seq++)
        {
            q.Enqueue(seq, In(InputButtons.NONE));
        }
        // The jump spent a token like any other sequence, so the backfill got
        // only the rest of the allowance.
        Assert.Equal(InputQueue.BURST_SEQS, q.PendingCount);
    }

    /// <summary>Starvation repeats drive movement, not the weapon: counting a
    /// silent tick as consumed is free weapon time.</summary>
    [Fact]
    public void AStarvedTick_ConsumesNothing()
    {
        InputQueue q = new InputQueue();
        q.Enqueue(0, In(InputButtons.NONE));
        q.Next();

        q.Next();

        Assert.Empty(q.Consumed);
    }

    /// <summary>Under adversarial pacing, consumption cannot outrun one input
    /// per tick plus the allowance, and the backlog stays shallow.</summary>
    [Fact]
    public void AdversarialPacing_NeverOutrunsOnePerTickPlusTheAllowance()
    {
        InputQueue q = new InputQueue();
        Random rng = new Random(7);
        int seq = 0;
        int consumedTotal = 0;

        for (int tick = 0; tick < 1000; tick++)
        {
            int sent = rng.Next(0, 9);
            for (int i = 0; i < sent; i++)
            {
                q.Enqueue(seq++, In(InputButtons.NONE));
            }
            q.Next();
            consumedTotal += q.Consumed.Count;
            Assert.True(q.PendingCount <= InputQueue.BURST_SEQS + InputQueue.MAX_CONSUMED,
                $"pending {q.PendingCount} at tick {tick}");
        }

        Assert.True(consumedTotal <= 1000 + InputQueue.BURST_SEQS,
            $"consumed {consumedTotal} inputs across 1000 ticks");
    }
}
