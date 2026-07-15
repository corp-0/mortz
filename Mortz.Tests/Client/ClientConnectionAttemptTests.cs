using Mortz.Client;
using Xunit;

namespace Mortz.Tests.Client;

public class ClientConnectionAttemptTests
{
    [Fact]
    public void DelayedRetryCannotRunAfterANewerAttemptStarts()
    {
        ClientConnectionAttempt attempt = new(maxRetries: 2);
        attempt.Start("old", 1, "player");
        ConnectionFailure oldFailure = attempt.Failed();

        attempt.Start("new", 2, "player");

        Assert.False(attempt.BeginScheduledRetry(oldFailure.Generation));
        Assert.Equal("new", attempt.Address);
    }

    [Fact]
    public void DuplicateFailureDoesNotScheduleParallelRetries()
    {
        ClientConnectionAttempt attempt = new(maxRetries: 2);
        attempt.Start("server", 1, "player");

        ConnectionFailure first = attempt.Failed();
        ConnectionFailure duplicate = attempt.Failed();

        Assert.Equal(ConnectionFailureAction.Retry, first.Action);
        Assert.Equal(ConnectionFailureAction.Ignore, duplicate.Action);
    }

    [Fact]
    public void AttemptFailsAfterItsRetryBudgetIsSpent()
    {
        ClientConnectionAttempt attempt = new(maxRetries: 1);
        attempt.Start("server", 1, "player");
        ConnectionFailure retry = attempt.Failed();
        Assert.True(attempt.BeginScheduledRetry(retry.Generation));

        ConnectionFailure exhausted = attempt.Failed();

        Assert.Equal(ConnectionFailureAction.Failed, exhausted.Action);
    }
}
