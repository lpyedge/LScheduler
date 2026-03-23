using LScheduler;

namespace LScheduler.Tests;

public sealed class JobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_SavesSuccessfulExecution()
    {
        var store = new FakeJobStore();
        var executor = new JobExecutor(store);

        var completed = await executor.ExecuteAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            Action = _ => Task.CompletedTask,
            RetryPolicy = new RetryPolicy(),
        }, CancellationToken.None);

        var result = Assert.Single(store.ExecutionResults);
        Assert.True(completed);
        Assert.True(result.Success);
        Assert.Equal(0, result.RetryCount);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAfterMaxRetries()
    {
        var store = new FakeJobStore();
        var attempts = 0;
        var executor = new JobExecutor(store);

        var completed = await executor.ExecuteAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            Action = _ =>
            {
                attempts++;
                throw new InvalidOperationException("boom");
            },
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 1,
                RetryInterval = TimeSpan.Zero,
            },
        }, CancellationToken.None);

        Assert.True(completed);
        Assert.Equal(2, attempts);
        Assert.Equal(2, store.ExecutionResults.Count);
        Assert.False(store.ExecutionResults[0].Success);
        Assert.False(store.ExecutionResults[1].Success);
        Assert.Equal(1, store.ExecutionResults[1].RetryCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFalseOnCancellation()
    {
        var store = new FakeJobStore();
        var executor = new JobExecutor(store);
        using var cancellationTokenSource = new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        var completed = await executor.ExecuteAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            Action = cancellationToken => Task.FromCanceled(cancellationToken),
            RetryPolicy = new RetryPolicy(),
        }, cancellationTokenSource.Token);

        Assert.False(completed);
        Assert.Empty(store.ExecutionResults);
    }

    [Fact]
    public void GetRetryDelay_ReturnsFixedInterval_WhenBackoffDisabled()
    {
        var delay = JobExecutor.GetRetryDelay(new RetryPolicy
        {
            RetryInterval = TimeSpan.FromSeconds(3),
        }, retryCount: 2);

        Assert.Equal(TimeSpan.FromSeconds(3), delay);
    }

    [Fact]
    public void GetRetryDelay_ReturnsExponentialInterval_WhenBackoffEnabled()
    {
        var delay = JobExecutor.GetRetryDelay(new RetryPolicy
        {
            RetryInterval = TimeSpan.FromSeconds(2),
            UseExponentialBackoff = true,
        }, retryCount: 3);

        Assert.Equal(TimeSpan.FromSeconds(16), delay);
    }
}
