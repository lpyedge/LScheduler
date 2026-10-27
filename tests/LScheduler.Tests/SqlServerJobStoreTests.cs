using LScheduler;
using LScheduler.SqlServer;
using Microsoft.Data.SqlClient;

namespace LScheduler.Tests;

public sealed class SqlServerJobStoreTests : IClassFixture<SqlServerDockerFixture>
{
    private readonly SqlServerDockerFixture _fixture;

    public SqlServerJobStoreTests(SqlServerDockerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveJobAsync_AndLoadJobsAsync_PersistsMetadata()
    {
        var store = new SqlServerJobStore(_fixture.ConnectionString);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Interval = TimeSpan.FromMinutes(1),
            MethodTypeName = "LScheduler.Tests.SqlServerJobStoreTests+PersistentJobTarget, LScheduler.Tests",
            MethodName = "RunAsync",
            MethodIsStatic = true,
            PayloadTypeName = "LScheduler.Tests.SqlServerJobStoreTests+PersistentJobPayload, LScheduler.Tests",
            PayloadJson = "{\"message\":\"sqlserver\"}",
            LastRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            NextRunTimeUtc = new DateTime(2026, 03, 23, 10, 01, 00, DateTimeKind.Utc),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 6,
                RetryInterval = TimeSpan.FromSeconds(4),
                UseExponentialBackoff = true,
            },
        });

        var jobs = await store.LoadJobsAsync();
        var persisted = Assert.Single(jobs, job => job.JobId == jobId);

        Assert.Equal(TimeSpan.FromMinutes(1), persisted.Interval);
        Assert.False(persisted.RunOnce);
        Assert.Equal("LScheduler.Tests.SqlServerJobStoreTests+PersistentJobTarget, LScheduler.Tests", persisted.MethodTypeName);
        Assert.Equal("RunAsync", persisted.MethodName);
        Assert.True(persisted.MethodIsStatic);
        Assert.Equal("LScheduler.Tests.SqlServerJobStoreTests+PersistentJobPayload, LScheduler.Tests", persisted.PayloadTypeName);
        Assert.Equal("{\"message\":\"sqlserver\"}", persisted.PayloadJson);
        Assert.Equal(6, persisted.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(4), persisted.RetryPolicy.RetryInterval);
        Assert.True(persisted.RetryPolicy.UseExponentialBackoff);
    }

    [Fact]
    public async Task PurgeExecutionResultsAsync_RemovesOnlyOlderEntries()
    {
        var store = new SqlServerJobStore(_fixture.ConnectionString);
        var oldJobId = $"old-{Guid.NewGuid():N}";
        var newJobId = $"new-{Guid.NewGuid():N}";
        var cutoff = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await store.SaveExecutionResultAsync(new JobExecutionResult { JobId = oldJobId, StartTimeUtc = cutoff, EndTimeUtc = cutoff.AddSeconds(-1) });
        await store.SaveExecutionResultAsync(new JobExecutionResult { JobId = newJobId, StartTimeUtc = cutoff, EndTimeUtc = cutoff });

        await store.PurgeExecutionResultsAsync(cutoff);

        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.ExecutionLog WHERE JobId IN (@oldJobId, @newJobId);";
        command.Parameters.AddWithValue("@oldJobId", oldJobId);
        command.Parameters.AddWithValue("@newJobId", newJobId);

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SaveExecutionResultAsync_WritesExecutionLog()
    {
        var store = new SqlServerJobStore(_fixture.ConnectionString);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveExecutionResultAsync(new JobExecutionResult
        {
            JobId = jobId,
            StartTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 03, 23, 10, 00, 01, DateTimeKind.Utc),
            Success = false,
            Exception = "boom",
            RetryCount = 2,
        });

        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.ExecutionLog WHERE JobId = @jobId;";
        command.Parameters.AddWithValue("@jobId", jobId);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync());
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesStoredJob()
    {
        var store = new SqlServerJobStore(_fixture.ConnectionString);
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
        var store = new SqlServerJobStore(_fixture.ConnectionString);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 2,
            },
        });

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Interval = TimeSpan.FromSeconds(25),
            RunOnce = true,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 7,
            },
        });

        var persisted = Assert.Single(await store.LoadJobsAsync(), job => job.JobId == jobId);
        Assert.Equal(TimeSpan.FromSeconds(25), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal(7, persisted.RetryPolicy.MaxRetries);
    }
}
