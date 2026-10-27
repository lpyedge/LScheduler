using LScheduler;
using LScheduler.MySql;
using MySqlConnector;

namespace LScheduler.Tests;

public sealed class MySqlJobStoreTests : IClassFixture<MySqlDockerFixture>
{
    private readonly MySqlDockerFixture _fixture;

    public MySqlJobStoreTests(MySqlDockerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveJobAsync_AndLoadJobsAsync_PersistsMetadata()
    {
        var store = new MySqlJobStore(_fixture.ConnectionString);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Interval = TimeSpan.FromSeconds(15),
            RunOnce = true,
            MethodTypeName = "LScheduler.Tests.MySqlJobStoreTests+PersistentJobTarget, LScheduler.Tests",
            MethodName = "RunAsync",
            MethodIsStatic = false,
            PayloadTypeName = "LScheduler.Tests.MySqlJobStoreTests+PersistentJobPayload, LScheduler.Tests",
            PayloadJson = "{\"message\":\"mysql\"}",
            LastRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            NextRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 15, DateTimeKind.Utc),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 5,
                RetryInterval = TimeSpan.FromSeconds(3),
            },
        });

        var jobs = await store.LoadJobsAsync();
        var persisted = Assert.Single(jobs, job => job.JobId == jobId);

        Assert.Equal(TimeSpan.FromSeconds(15), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal("LScheduler.Tests.MySqlJobStoreTests+PersistentJobTarget, LScheduler.Tests", persisted.MethodTypeName);
        Assert.Equal("RunAsync", persisted.MethodName);
        Assert.False(persisted.MethodIsStatic);
        Assert.Equal("LScheduler.Tests.MySqlJobStoreTests+PersistentJobPayload, LScheduler.Tests", persisted.PayloadTypeName);
        Assert.Equal("{\"message\":\"mysql\"}", persisted.PayloadJson);
        Assert.Equal(5, persisted.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(3), persisted.RetryPolicy.RetryInterval);
    }

    [Fact]
    public async Task PurgeExecutionResultsAsync_RemovesOnlyOlderEntries()
    {
        var store = new MySqlJobStore(_fixture.ConnectionString);
        var oldJobId = $"old-{Guid.NewGuid():N}";
        var newJobId = $"new-{Guid.NewGuid():N}";
        var cutoff = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await store.SaveExecutionResultAsync(new JobExecutionResult { JobId = oldJobId, StartTimeUtc = cutoff, EndTimeUtc = cutoff.AddSeconds(-1) });
        await store.SaveExecutionResultAsync(new JobExecutionResult { JobId = newJobId, StartTimeUtc = cutoff, EndTimeUtc = cutoff });

        await store.PurgeExecutionResultsAsync(cutoff);

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ExecutionLog WHERE JobId IN (@oldJobId, @newJobId);";
        command.Parameters.AddWithValue("@oldJobId", oldJobId);
        command.Parameters.AddWithValue("@newJobId", newJobId);

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SaveExecutionResultAsync_WritesExecutionLog()
    {
        var store = new MySqlJobStore(_fixture.ConnectionString);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveExecutionResultAsync(new JobExecutionResult
        {
            JobId = jobId,
            StartTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 03, 23, 10, 00, 01, DateTimeKind.Utc),
            Success = true,
            RetryCount = 0,
        });

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ExecutionLog WHERE JobId = @jobId;";
        command.Parameters.AddWithValue("@jobId", jobId);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync());
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesStoredJob()
    {
        var store = new MySqlJobStore(_fixture.ConnectionString);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy(),
        });

        await store.DeleteJobAsync(jobId);

        var jobs = await store.LoadJobsAsync();
        Assert.DoesNotContain(jobs, job => job.JobId == jobId);
    }

    [Fact]
    public async Task SaveJobAsync_ReplacesExistingJob()
    {
        var store = new MySqlJobStore(_fixture.ConnectionString);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 1,
            },
        });

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Interval = TimeSpan.FromSeconds(45),
            RunOnce = true,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 9,
            },
        });

        var persisted = Assert.Single(await store.LoadJobsAsync(), job => job.JobId == jobId);
        Assert.Equal(TimeSpan.FromSeconds(45), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal(9, persisted.RetryPolicy.MaxRetries);
    }
}
