using System.Text.Json;
using LScheduler;

namespace LScheduler.Tests;

public sealed class FileJobStoreTests : IDisposable
{
    private readonly string _directoryPath;

    public FileJobStoreTests()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), "lscheduler-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task SaveJobAsync_AndLoadJobsAsync_PersistsMetadata()
    {
        var store = new FileJobStore(_directoryPath);
        var job = new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromSeconds(5),
            RunOnce = true,
            MethodTypeName = "LScheduler.Tests.FileJobStoreTests+PersistentJobTarget, LScheduler.Tests",
            MethodName = "RunAsync",
            MethodIsStatic = true,
            PayloadTypeName = "LScheduler.Tests.FileJobStoreTests+PersistentJobPayload, LScheduler.Tests",
            PayloadJson = "{\"message\":\"hello\"}",
            Action = _ => Task.CompletedTask,
            LastRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            NextRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 05, DateTimeKind.Utc),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 2,
                RetryInterval = TimeSpan.FromSeconds(3),
            },
        };

        await store.SaveJobAsync(job);
        var jobs = await store.LoadJobsAsync();

        var persisted = Assert.Single(jobs);
        Assert.Equal("job-1", persisted.JobId);
        Assert.Equal(TimeSpan.FromSeconds(5), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal(job.MethodTypeName, persisted.MethodTypeName);
        Assert.Equal(job.MethodName, persisted.MethodName);
        Assert.True(persisted.MethodIsStatic);
        Assert.Equal(job.PayloadTypeName, persisted.PayloadTypeName);
        Assert.Equal("{\"message\":\"hello\"}", persisted.PayloadJson);
        Assert.Null(persisted.Action);
        Assert.Equal(job.LastRunTimeUtc, persisted.LastRunTimeUtc);
        Assert.Equal(job.NextRunTimeUtc, persisted.NextRunTimeUtc);
        Assert.Equal(2, persisted.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(3), persisted.RetryPolicy.RetryInterval);
    }

    [Fact]
    public async Task SaveExecutionResultAsync_AppendsToExecutionLogFile()
    {
        var store = new FileJobStore(_directoryPath);

        await store.SaveExecutionResultAsync(new JobExecutionResult
        {
            JobId = "job-1",
            StartTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 03, 23, 10, 00, 01, DateTimeKind.Utc),
            Success = false,
            Exception = "boom",
            RetryCount = 1,
        });

        await store.SaveExecutionResultAsync(new JobExecutionResult
        {
            JobId = "job-1",
            StartTimeUtc = new DateTime(2026, 03, 23, 10, 00, 02, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 03, 23, 10, 00, 03, DateTimeKind.Utc),
            Success = true,
            RetryCount = 2,
        });

        var filePath = Path.Combine(_directoryPath, "execution_log.json");
        using var stream = File.OpenRead(filePath);
        var records = await JsonSerializer.DeserializeAsync<List<JobExecutionResult>>(stream);

        Assert.NotNull(records);
        Assert.Equal(2, records.Count);
        Assert.Equal("boom", records[0].Exception);
        Assert.True(records[1].Success);
    }

    [Fact]
    public async Task SaveJobAsync_ReplacesExistingJob()
    {
        var store = new FileJobStore(_directoryPath);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            Action = _ => Task.CompletedTask,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 1,
            },
        });

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromSeconds(10),
            Action = _ => Task.CompletedTask,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 9,
            },
        });

        var jobs = await store.LoadJobsAsync();

        var persisted = Assert.Single(jobs);
        Assert.Equal(TimeSpan.FromSeconds(10), persisted.Interval);
        Assert.Equal(9, persisted.RetryPolicy.MaxRetries);
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesExistingJob()
    {
        var store = new FileJobStore(_directoryPath);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy(),
        });

        await store.DeleteJobAsync("job-1");

        Assert.Empty(await store.LoadJobsAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
