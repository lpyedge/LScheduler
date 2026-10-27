using LScheduler;
using LScheduler.MySql;
using LScheduler.Redis;
using LScheduler.Sqlite;
using LScheduler.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LScheduler.Tests;

public sealed class GuardClauseTests
{
    [Fact]
    public async Task FileJobStore_LoadJobsAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), "lscheduler-empty-store", Guid.NewGuid().ToString("N"));
        var store = new FileJobStore(path);

        var jobs = await store.LoadJobsAsync();

        Assert.Empty(jobs);
        Directory.Delete(path, recursive: true);
    }

    [Fact]
    public async Task JobScheduler_AddJobAsync_Throws_WhenActionIsMissing()
    {
        var scheduler = new JobScheduler(new FakeJobStore());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            RetryPolicy = new RetryPolicy(),
        }));

        Assert.Equal("job", exception.ParamName, ignoreCase: true);
    }

    [Fact]
    public async Task JobScheduler_AddJobAsync_Throws_WhenJobIdIsBlank()
    {
        var scheduler = new JobScheduler(new FakeJobStore());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.AddJobAsync(" ", JobSchedule.Every(TimeSpan.FromSeconds(1)), PublicNoopJobs.RunAsync));

        Assert.Equal("jobId", exception.ParamName, ignoreCase: true);
    }

    [Fact]
    public async Task JobScheduler_AddInMemoryJobAsync_Throws_WhenJobIdIsBlank()
    {
        var scheduler = new JobScheduler(new FakeJobStore());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.AddInMemoryJobAsync(" ", JobSchedule.Every(TimeSpan.FromSeconds(1)), _ => Task.CompletedTask));

        Assert.Equal("jobId", exception.ParamName, ignoreCase: true);
    }

    [Fact]
    public async Task JobScheduler_AddJobAsyncWithPayload_Throws_WhenJobIdIsBlank()
    {
        var scheduler = new JobScheduler(new FakeJobStore());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.AddJobAsync(" ", JobSchedule.Every(TimeSpan.FromSeconds(1)), PayloadJobTargets.RunAsync, "payload"));

        Assert.Equal("jobId", exception.ParamName, ignoreCase: true);
    }

    [Fact]
    public async Task JobScheduler_AddJobAsync_Throws_WhenPersistentMethodIsPrivate()
    {
        var scheduler = new JobScheduler(new FakeJobStore());
        var target = new NonPublicJobTarget();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.AddJobAsync("job-1", JobSchedule.Every(TimeSpan.FromSeconds(1)), target.CreateDelegate()));

        Assert.Equal("method", exception.ParamName, ignoreCase: true);
        Assert.Contains("public methods", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobScheduler_AddJobAsync_Throws_WhenPersistentDelegateIsLambda()
    {
        var scheduler = new JobScheduler(new FakeJobStore());
        var value = "captured";

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.AddJobAsync(
                "job-1",
                JobSchedule.Every(TimeSpan.FromSeconds(1)),
                cancellationToken =>
                {
                    _ = value;
                    return Task.CompletedTask;
                }));

        Assert.Equal("method", exception.ParamName, ignoreCase: true);
        Assert.Contains("AddInMemoryJobAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobScheduler_AddJobAsync_Throws_WhenPayloadMethodSignatureDoesNotMatchPayloadType()
    {
        var scheduler = new JobScheduler(new FakeJobStore());
        Func<object, CancellationToken, Task> method = PayloadJobTargets.RunObjectAsync;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.AddJobAsync<string>("job-1", JobSchedule.Every(TimeSpan.FromSeconds(1)), method, "payload"));

        Assert.Equal("method", exception.ParamName, ignoreCase: true);
        Assert.Contains("Persistent jobs with payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobScheduler_RemoveJobAsync_Throws_WhenJobIdIsBlank()
    {
        var scheduler = new JobScheduler(new FakeJobStore());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => scheduler.RemoveJobAsync(" "));

        Assert.Equal("jobId", exception.ParamName, ignoreCase: true);
    }

    [Fact]
    public async Task FileJobStore_DeleteJobAsync_Throws_WhenJobIdIsBlank()
    {
        var path = Path.Combine(Path.GetTempPath(), "lscheduler-empty-store", Guid.NewGuid().ToString("N"));
        var store = new FileJobStore(path);

        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteJobAsync(" "));

        Directory.Delete(path, recursive: true);
    }

    [Fact]
    public void SqliteJobStore_Throws_WhenPathIsBlank()
    {
        Assert.Throws<ArgumentException>(() => new SqliteJobStore(" "));
    }

    [Fact]
    public void MySqlJobStore_Throws_WhenConnectionStringIsBlank()
    {
        Assert.Throws<ArgumentException>(() => new MySqlJobStore(" "));
    }

    [Fact]
    public void SqlServerJobStore_Throws_WhenConnectionStringIsBlank()
    {
        Assert.Throws<ArgumentException>(() => new SqlServerJobStore(" "));
    }

    [Fact]
    public void RedisJobStore_Throws_WhenConfigurationIsBlank()
    {
        Assert.Throws<ArgumentException>(() => new RedisJobStore(" "));
    }

    [Fact]
    public void CronHelper_Throws_WhenCronSegmentCountIsInvalid()
    {
        var exception = Assert.Throws<ArgumentException>(() => CronHelper.GetNextOccurrence("* * * *", DateTime.UtcNow));

        Assert.Equal("cron", exception.ParamName, ignoreCase: true);
    }

    [Fact]
    public void AddLSchedulerSqlite_WithConfiguration_Throws_WhenDatabasePathIsMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddLSchedulerSqlite(configuration);
        });

        Assert.Equal("configuration", exception.ParamName, ignoreCase: true);
        Assert.Contains("LScheduler:Sqlite:DatabasePath is required.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddLSchedulerMySql_WithConfiguration_Throws_WhenConnectionStringIsMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddLSchedulerMySql(configuration);
        });

        Assert.Equal("configuration", exception.ParamName, ignoreCase: true);
        Assert.Contains("LScheduler:MySql:ConnectionString is required.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddLSchedulerSqlServer_WithConfiguration_Throws_WhenConnectionStringIsMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddLSchedulerSqlServer(configuration);
        });

        Assert.Equal("configuration", exception.ParamName, ignoreCase: true);
        Assert.Contains("LScheduler:SqlServer:ConnectionString is required.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddLSchedulerRedis_WithConfiguration_Throws_WhenRedisConfigurationIsMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddLSchedulerRedis(configuration);
        });

        Assert.Equal("configuration", exception.ParamName, ignoreCase: true);
        Assert.Contains("LScheduler:Redis:Configuration is required.", exception.Message, StringComparison.Ordinal);
    }

    public static class PublicNoopJobs
    {
        public static Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NonPublicJobTarget
    {
        public Func<CancellationToken, Task> CreateDelegate()
        {
            return HiddenAsync;
        }

        private Task HiddenAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private static class PayloadJobTargets
    {
        public static Task RunAsync(string payload, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public static Task RunObjectAsync(object payload, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
