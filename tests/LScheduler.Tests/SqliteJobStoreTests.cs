using LScheduler;
using LScheduler.Sqlite;
using Microsoft.Data.Sqlite;

namespace LScheduler.Tests;

public sealed class SqliteJobStoreTests : IDisposable
{
    private readonly string _directoryPath;
    private readonly string _databasePath;

    public SqliteJobStoreTests()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), "lscheduler-sqlite-tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_directoryPath, "lscheduler.db");
    }

    [Fact]
    public async Task SaveJobAsync_AndLoadJobsAsync_PersistsMetadata()
    {
        var store = new SqliteJobStore(_databasePath);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Interval = TimeSpan.FromSeconds(15),
            RunOnce = true,
            MethodTypeName = "LScheduler.Tests.SqliteJobStoreTests+PersistentJobTarget, LScheduler.Tests",
            MethodName = "RunAsync",
            MethodIsStatic = true,
            PayloadTypeName = "LScheduler.Tests.SqliteJobStoreTests+PersistentJobPayload, LScheduler.Tests",
            PayloadJson = "{\"message\":\"sqlite\"}",
            LastRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            NextRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 15, DateTimeKind.Utc),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 4,
                RetryInterval = TimeSpan.FromSeconds(2),
                UseExponentialBackoff = true,
            },
        });

        var jobs = await store.LoadJobsAsync();

        var persisted = Assert.Single(jobs);
        Assert.Equal(jobId, persisted.JobId);
        Assert.Equal(TimeSpan.FromSeconds(15), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal("LScheduler.Tests.SqliteJobStoreTests+PersistentJobTarget, LScheduler.Tests", persisted.MethodTypeName);
        Assert.Equal("RunAsync", persisted.MethodName);
        Assert.True(persisted.MethodIsStatic);
        Assert.Equal("LScheduler.Tests.SqliteJobStoreTests+PersistentJobPayload, LScheduler.Tests", persisted.PayloadTypeName);
        Assert.Equal("{\"message\":\"sqlite\"}", persisted.PayloadJson);
        Assert.Equal(4, persisted.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), persisted.RetryPolicy.RetryInterval);
        Assert.True(persisted.RetryPolicy.UseExponentialBackoff);
    }

    [Fact]
    public async Task SaveExecutionResultAsync_WritesExecutionLog()
    {
        var store = new SqliteJobStore(_databasePath);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveExecutionResultAsync(new JobExecutionResult
        {
            JobId = jobId,
            StartTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 03, 23, 10, 00, 01, DateTimeKind.Utc),
            Success = false,
            Exception = "boom",
            RetryCount = 1,
        });

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ExecutionLog WHERE JobId = $jobId;";
        command.Parameters.AddWithValue("$jobId", jobId);

        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesStoredJob()
    {
        var store = new SqliteJobStore(_databasePath);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy(),
        });

        await store.DeleteJobAsync("job-1");

        Assert.Empty(await store.LoadJobsAsync());
    }

    [Fact]
    public async Task PurgeExecutionResultsAsync_RemovesOnlyOlderEntries()
    {
        var store = new SqliteJobStore(_databasePath);
        var cutoff = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await store.SaveExecutionResultAsync(new JobExecutionResult { JobId = "old", StartTimeUtc = cutoff, EndTimeUtc = cutoff.AddSeconds(-1) });
        await store.SaveExecutionResultAsync(new JobExecutionResult { JobId = "new", StartTimeUtc = cutoff, EndTimeUtc = cutoff });

        await store.PurgeExecutionResultsAsync(cutoff);

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId FROM ExecutionLog;";
        Assert.Equal("new", Assert.IsType<string>(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SaveJobAsync_ReplacesExistingJob()
    {
        var store = new SqliteJobStore(_databasePath);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 1,
            },
        });

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromSeconds(30),
            RunOnce = true,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 8,
            },
        });

        var persisted = Assert.Single(await store.LoadJobsAsync());
        Assert.Equal(TimeSpan.FromSeconds(30), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal(8, persisted.RetryPolicy.MaxRetries);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
