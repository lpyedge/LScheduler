using LScheduler;

namespace LScheduler.Tests;

public sealed class ExecutionLogTests
{
    [Fact]
    public void Defaults_LogFailuresAndRetriesButNotSuccess()
    {
        var options = new ExecutionLogOptions();

        Assert.False(options.Success);
        Assert.True(options.Failure);
        Assert.True(options.Retry);
        Assert.Equal(TimeSpan.FromDays(30), options.Retention);
    }

    [Fact]
    public async Task ExecuteAsync_WithDefaults_LogsRetriesAndFinalFailureOnly()
    {
        var store = new FakeJobStore();

        await new JobExecutor(store).ExecuteAsync(CreateJob(failuresBeforeSuccess: int.MaxValue, maxRetries: 2), CancellationToken.None);

        Assert.Equal(new[] { 0, 1, 2 }, store.ExecutionResults.Select(result => result.RetryCount));
        Assert.All(store.ExecutionResults, result => Assert.False(result.Success));
    }

    [Fact]
    public async Task ExecuteAsync_WithDefaults_DoesNotLogSuccess()
    {
        var store = new FakeJobStore();

        await new JobExecutor(store).ExecuteAsync(CreateJob(failuresBeforeSuccess: 0, maxRetries: 2), CancellationToken.None);

        Assert.Empty(store.ExecutionResults);
    }

    [Fact]
    public async Task ExecuteAsync_WithDefaults_LogsRetriesButNotEventualSuccess()
    {
        var store = new FakeJobStore();

        await new JobExecutor(store).ExecuteAsync(CreateJob(failuresBeforeSuccess: 2, maxRetries: 3), CancellationToken.None);

        Assert.Equal(new[] { 0, 1 }, store.ExecutionResults.Select(result => result.RetryCount));
    }

    [Fact]
    public async Task ExecuteAsync_WithRetryDisabled_LogsOnlyFinalFailure()
    {
        var store = new FakeJobStore();
        var executor = new JobExecutor(store, new ExecutionLogOptions { Retry = false });

        await executor.ExecuteAsync(CreateJob(failuresBeforeSuccess: int.MaxValue, maxRetries: 2), CancellationToken.None);

        Assert.Equal(2, Assert.Single(store.ExecutionResults).RetryCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithEverythingDisabled_LogsNothing()
    {
        var store = new FakeJobStore();
        var executor = new JobExecutor(store, new ExecutionLogOptions { Failure = false, Retry = false });

        await executor.ExecuteAsync(CreateJob(failuresBeforeSuccess: int.MaxValue, maxRetries: 2), CancellationToken.None);
        await executor.ExecuteAsync(CreateJob(failuresBeforeSuccess: 0, maxRetries: 2), CancellationToken.None);

        Assert.Empty(store.ExecutionResults);
    }

    [Fact]
    public async Task PurgeExecutionLogAsync_RemovesResultsOlderThanRetention_AtMostOncePerHour()
    {
        var now = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);
        var store = new FakeJobStore();
        store.ExecutionResults.Add(new JobExecutionResult { JobId = "old", EndTimeUtc = now.AddDays(-2) });
        store.ExecutionResults.Add(new JobExecutionResult { JobId = "recent", EndTimeUtc = now.AddHours(-1) });
        var scheduler = new JobScheduler(store, executionLog: new ExecutionLogOptions { Retention = TimeSpan.FromDays(1) });

        await scheduler.PurgeExecutionLogAsync(now, CancellationToken.None);

        Assert.Equal("recent", Assert.Single(store.ExecutionResults).JobId);

        store.ExecutionResults.Add(new JobExecutionResult { JobId = "old-again", EndTimeUtc = now.AddDays(-2) });
        await scheduler.PurgeExecutionLogAsync(now.AddMinutes(30), CancellationToken.None);
        Assert.Equal(2, store.ExecutionResults.Count);

        await scheduler.PurgeExecutionLogAsync(now.AddMinutes(61), CancellationToken.None);
        Assert.Equal("recent", Assert.Single(store.ExecutionResults).JobId);
    }

    [Fact]
    public async Task PurgeExecutionLogAsync_KeepsEverything_WhenRetentionIsZero()
    {
        var now = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);
        var store = new FakeJobStore();
        store.ExecutionResults.Add(new JobExecutionResult { JobId = "old", EndTimeUtc = now.AddYears(-5) });
        var scheduler = new JobScheduler(store, executionLog: new ExecutionLogOptions { Retention = TimeSpan.Zero });

        await scheduler.PurgeExecutionLogAsync(now, CancellationToken.None);

        Assert.Single(store.ExecutionResults);
    }

    [Fact]
    public void Constructor_Throws_WhenRetentionIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JobScheduler(new FakeJobStore(), executionLog: new ExecutionLogOptions { Retention = TimeSpan.FromSeconds(-1) }));
    }

    private static JobDefinition CreateJob(int failuresBeforeSuccess, int maxRetries)
    {
        var attempts = 0;

        return new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            Action = _ => ++attempts <= failuresBeforeSuccess
                ? throw new InvalidOperationException("boom")
                : Task.CompletedTask,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = maxRetries,
                RetryInterval = TimeSpan.Zero,
            },
        };
    }
}
