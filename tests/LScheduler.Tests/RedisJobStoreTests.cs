using System.Text.Json;
using LScheduler;
using LScheduler.Redis;
using StackExchange.Redis;

namespace LScheduler.Tests;

public sealed class RedisJobStoreTests : IClassFixture<RedisDockerFixture>
{
    private readonly RedisDockerFixture _fixture;

    public RedisJobStoreTests(RedisDockerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveJobAsync_AndLoadJobsAsync_PersistsMetadata()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        await using var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);
        var jobId = $"job-{Guid.NewGuid():N}";

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Interval = TimeSpan.FromSeconds(20),
            RunOnce = true,
            MethodTypeName = "LScheduler.Tests.RedisJobStoreTests+PersistentJobTarget, LScheduler.Tests",
            MethodName = "RunAsync",
            MethodIsStatic = true,
            PayloadTypeName = "LScheduler.Tests.RedisJobStoreTests+PersistentJobPayload, LScheduler.Tests",
            PayloadJson = "{\"message\":\"redis\"}",
            LastRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            NextRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 20, DateTimeKind.Utc),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 8,
                RetryInterval = TimeSpan.FromSeconds(5),
            },
        });

        var jobs = await store.LoadJobsAsync();
        var persisted = Assert.Single(jobs);

        Assert.Equal(jobId, persisted.JobId);
        Assert.Equal(TimeSpan.FromSeconds(20), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal("LScheduler.Tests.RedisJobStoreTests+PersistentJobTarget, LScheduler.Tests", persisted.MethodTypeName);
        Assert.Equal("RunAsync", persisted.MethodName);
        Assert.True(persisted.MethodIsStatic);
        Assert.Equal("LScheduler.Tests.RedisJobStoreTests+PersistentJobPayload, LScheduler.Tests", persisted.PayloadTypeName);
        Assert.Equal("{\"message\":\"redis\"}", persisted.PayloadJson);
        Assert.Equal(8, persisted.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(5), persisted.RetryPolicy.RetryInterval);
    }

    [Fact]
    public async Task SaveJobAsync_StoresMetadataWithoutActionPayload()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        var jobId = $"job-{Guid.NewGuid():N}";
        await using var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(_fixture.Configuration);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Cron = "*/5 * * * * *",
            Action = _ => Task.CompletedTask,
            MethodTypeName = typeof(RedisJobStoreTests).AssemblyQualifiedName,
            MethodName = nameof(SaveJobAsync_StoresMetadataWithoutActionPayload),
            MethodIsStatic = true,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 2,
            },
        });

        var rawPayload = await connection.GetDatabase().HashGetAsync(jobsKey, jobId);
        var persisted = JsonSerializer.Deserialize<JobDefinition>(rawPayload.ToString());

        Assert.NotNull(persisted);
        Assert.Null(persisted.Action);
        Assert.Equal(jobId, persisted.JobId);
        Assert.Equal("*/5 * * * * *", persisted.Cron);
        Assert.Equal(2, persisted.RetryPolicy.MaxRetries);
    }

    [Fact]
    public async Task SaveExecutionResultAsync_PushesExecutionLog()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        var jobId = $"job-{Guid.NewGuid():N}";
        await using var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(_fixture.Configuration);

        await store.SaveExecutionResultAsync(new JobExecutionResult
        {
            JobId = jobId,
            StartTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 03, 23, 10, 00, 01, DateTimeKind.Utc),
            Success = true,
            RetryCount = 0,
        });

        var database = connection.GetDatabase();
        var count = await database.ListLengthAsync(logsKey);
        var payload = await database.ListGetByIndexAsync(logsKey, 0);
        var record = JsonSerializer.Deserialize<JobExecutionResult>(payload.ToString());

        Assert.Equal(1L, count);
        Assert.NotNull(record);
        Assert.Equal(jobId, record.JobId);
        Assert.True(record.Success);
    }

    [Fact]
    public async Task LoadJobsAsync_ReturnsEmpty_WhenNoJobsSaved()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        await using var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);

        Assert.Empty(await store.LoadJobsAsync());
    }

    [Fact]
    public async Task SaveJobAsync_ReplacesExistingJob()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        var jobId = $"job-{Guid.NewGuid():N}";
        await using var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);

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
            Interval = TimeSpan.FromSeconds(18),
            RunOnce = true,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 6,
            },
        });

        var persisted = Assert.Single(await store.LoadJobsAsync());
        Assert.Equal(TimeSpan.FromSeconds(18), persisted.Interval);
        Assert.True(persisted.RunOnce);
        Assert.Equal(6, persisted.RetryPolicy.MaxRetries);
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesStoredJob()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        var jobId = $"job-{Guid.NewGuid():N}";
        await using var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = jobId,
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy(),
        });

        await store.DeleteJobAsync(jobId);

        Assert.Empty(await store.LoadJobsAsync());
    }

    [Fact]
    public async Task DisposeAsync_DoesNothing_WhenConnectionWasNeverOpened()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);

        await store.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_AllowsMultipleDisposals_AfterConnectionWasOpened()
    {
        var jobsKey = $"lscheduler:jobs:{Guid.NewGuid():N}";
        var logsKey = $"lscheduler:logs:{Guid.NewGuid():N}";
        var store = new RedisJobStore(_fixture.Configuration, jobsKey, logsKey);

        await store.LoadJobsAsync();
        await store.DisposeAsync();
        await store.DisposeAsync();
    }
}
