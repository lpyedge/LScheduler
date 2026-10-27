using LScheduler;
using LScheduler.MySql;
using LScheduler.Redis;
using LScheduler.Sqlite;
using LScheduler.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LScheduler.Tests;

public sealed class ServiceRegistrationTests : IDisposable
{
    private readonly List<string> _directoriesToDelete = [];

    [Fact]
    public async Task AddLScheduler_WithoutConfiguration_RegistersDefaultFileStore()
    {
        var services = new ServiceCollection();

        services.AddLScheduler();

        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<JobSchedulerOptions>();
        var store = provider.GetRequiredService<IJobStore>();
        var scheduler = provider.GetRequiredService<JobScheduler>();

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "lscheduler-data"), options.DataDirectory);
        Assert.IsType<FileJobStore>(store);
        Assert.NotNull(scheduler);
    }

    [Fact]
    public async Task AddLScheduler_WithManualOptions_StartsHostedSchedulerAndUsesGlobalRetryPolicy()
    {
        var directory = CreateTempDirectory();
        var builder = Host.CreateApplicationBuilder();
        StaticSignalJobs.Reset();

        builder.Services.AddLScheduler(options =>
        {
            options.DataDirectory = directory;
            options.ScanInterval = TimeSpan.FromMilliseconds(50);
            options.RetryPolicy.MaxRetries = 7;
            options.RetryPolicy.RetryInterval = TimeSpan.FromSeconds(3);
            options.ExecutionLog.Success = true;
        });

        using var host = builder.Build();
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<JobScheduler>();
        await scheduler.AddJobAsync(
            "manual-options-job",
            JobSchedule.Every(TimeSpan.FromMilliseconds(100)),
            StaticSignalJobs.MarkAsync);

        await StaticSignalJobs.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.StopAsync();

        var job = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.Equal(7, job.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(3), job.RetryPolicy.RetryInterval);
        Assert.True(File.Exists(Path.Combine(directory, "jobs.json")));
        Assert.True(File.Exists(Path.Combine(directory, "execution_log.jsonl")));
    }

    [Fact]
    public async Task AddLScheduler_WithConfiguration_BindsAppSettings()
    {
        var directory = CreateTempDirectory();
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LScheduler:DataDirectory"] = directory,
            ["LScheduler:ScanInterval"] = "00:00:00.05",
            ["LScheduler:RetryPolicy:MaxRetries"] = "5",
            ["LScheduler:RetryPolicy:RetryInterval"] = "00:00:02",
        });

        builder.Services.AddLScheduler(builder.Configuration);

        using var host = builder.Build();

        var options = host.Services.GetRequiredService<JobSchedulerOptions>();
        var scheduler = host.Services.GetRequiredService<JobScheduler>();
        await scheduler.AddJobAsync("config-job", JobSchedule.Delay(TimeSpan.FromMinutes(1)), StaticSignalJobs.MarkAsync);

        var job = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.Equal(directory, options.DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(50), options.ScanInterval);
        Assert.Equal(5, options.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryPolicy.RetryInterval);
        Assert.Equal(5, job.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), job.RetryPolicy.RetryInterval);
    }

    [Fact]
    public void AddLScheduler_WithoutArguments_ReadsLSchedulerSectionFromHostConfiguration()
    {
        var directory = CreateTempDirectory();
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LScheduler:DataDirectory"] = directory,
            ["LScheduler:ScanInterval"] = "00:00:00.30",
            ["LScheduler:ExecutionLog:Success"] = "true",
            ["LScheduler:ExecutionLog:Retry"] = "false",
            ["LScheduler:ExecutionLog:Retention"] = "7.00:00:00",
        });

        builder.Services.AddLScheduler();

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<JobSchedulerOptions>();

        Assert.Equal(directory, options.DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(300), options.ScanInterval);
        Assert.True(options.ExecutionLog.Success);
        Assert.True(options.ExecutionLog.Failure);
        Assert.False(options.ExecutionLog.Retry);
        Assert.Equal(TimeSpan.FromDays(7), options.ExecutionLog.Retention);
    }

    [Fact]
    public void AddLScheduler_WithCodeOptions_OverridesLSchedulerSection()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LScheduler:ScanInterval"] = "00:00:00.30",
            ["LScheduler:RetryPolicy:MaxRetries"] = "9",
        });

        builder.Services.AddLScheduler(options => options.ScanInterval = TimeSpan.FromMilliseconds(40));

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<JobSchedulerOptions>();

        Assert.Equal(TimeSpan.FromMilliseconds(40), options.ScanInterval);
        Assert.Equal(9, options.RetryPolicy.MaxRetries);
    }

    [Fact]
    public async Task AddLScheduler_WithoutAnyConfiguration_UsesDefaultExecutionLogOptions()
    {
        var services = new ServiceCollection();

        services.AddLScheduler(options => options.ExecutionLog.Failure = false);

        await using var provider = services.BuildServiceProvider();
        var log = provider.GetRequiredService<JobSchedulerOptions>().ExecutionLog;

        Assert.False(log.Success);
        Assert.False(log.Failure);
        Assert.True(log.Retry);
        Assert.Equal(TimeSpan.FromDays(30), log.Retention);
    }

    [Fact]
    public async Task AddLScheduler_WithConfigurationSection_BindsAppSettings()
    {
        var directory = CreateTempDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:DataDirectory"] = directory,
                ["LScheduler:ScanInterval"] = "00:00:00.15",
                ["LScheduler:RetryPolicy:MaxRetries"] = "4",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLScheduler(configuration.GetSection(JobSchedulerOptions.SectionName));

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<JobSchedulerOptions>();

        Assert.Equal(directory, options.DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(150), options.ScanInterval);
        Assert.Equal(4, options.RetryPolicy.MaxRetries);
    }

    [Fact]
    public async Task AddLScheduler_ZeroConfig_UsesDefaultFilePersistence()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "lscheduler-data");
        StaticSignalJobs.Reset();

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLScheduler();

        using var host = builder.Build();
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<JobScheduler>();
        await scheduler.AddJobAsync(
            "zero-config-job",
            JobSchedule.Every(TimeSpan.FromMilliseconds(100)),
            StaticSignalJobs.MarkAsync);

        await StaticSignalJobs.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.StopAsync();

        Assert.True(File.Exists(Path.Combine(directory, "jobs.json")));
        Assert.False(File.Exists(Path.Combine(directory, "execution_log.jsonl")));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task AddLScheduler_RestoresPersistentMethodJob_OnHostRestart()
    {
        var directory = CreateTempDirectory();

        var firstBuilder = Host.CreateApplicationBuilder();
        firstBuilder.Services.AddSingleton<PersistentExecutionSink>();
        firstBuilder.Services.AddSingleton<PersistentWorkerJobs>();
        firstBuilder.Services.AddLScheduler(options =>
        {
            options.DataDirectory = directory;
            options.ScanInterval = TimeSpan.FromMilliseconds(50);
        });

        using (var firstHost = firstBuilder.Build())
        {
            await firstHost.StartAsync();

            var scheduler = firstHost.Services.GetRequiredService<JobScheduler>();
            var jobs = firstHost.Services.GetRequiredService<PersistentWorkerJobs>();

            await scheduler.AddJobAsync(
                "restore-job",
                JobSchedule.Delay(TimeSpan.FromMilliseconds(500)),
                jobs.RunAsync,
                new PersistentPayload("restored-message"));

            await firstHost.StopAsync();
        }

        var secondBuilder = Host.CreateApplicationBuilder();
        secondBuilder.Services.AddSingleton<PersistentExecutionSink>();
        secondBuilder.Services.AddSingleton<PersistentWorkerJobs>();
        secondBuilder.Services.AddLScheduler(options =>
        {
            options.DataDirectory = directory;
            options.ScanInterval = TimeSpan.FromMilliseconds(50);
        });

        using var secondHost = secondBuilder.Build();
        await secondHost.StartAsync();

        var sink = secondHost.Services.GetRequiredService<PersistentExecutionSink>();
        await sink.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondHost.StopAsync();

        Assert.Equal("restored-message", Assert.Single(sink.Messages));
    }

    [Fact]
    public async Task ProviderRegistrationWithoutArguments_ReadsProviderSettingFromHostConfiguration()
    {
        var directory = CreateTempDirectory();
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LScheduler:ScanInterval"] = "00:00:00.35",
            ["LScheduler:Sqlite:DatabasePath"] = Path.Combine(directory, "sqlite.db"),
            ["LScheduler:MySql:ConnectionString"] = "Server=localhost;Database=lscheduler;User Id=root;Password=test;",
            ["LScheduler:SqlServer:ConnectionString"] = "Server=.;Database=LScheduler;User Id=sa;Password=Password123!;",
            ["LScheduler:Redis:Configuration"] = "localhost:6379",
        });

        var sqliteServices = new ServiceCollection();
        var mySqlServices = new ServiceCollection();
        var sqlServerServices = new ServiceCollection();
        var redisServices = new ServiceCollection();

        foreach (var services in new[] { sqliteServices, mySqlServices, sqlServerServices, redisServices })
        {
            services.AddSingleton<IConfiguration>(builder.Configuration);
        }

        sqliteServices.AddLSchedulerSqlite();
        mySqlServices.AddLSchedulerMySql();
        sqlServerServices.AddLSchedulerSqlServer();
        redisServices.AddLSchedulerRedis();

        await using var sqliteProvider = sqliteServices.BuildServiceProvider();
        await using var mySqlProvider = mySqlServices.BuildServiceProvider();
        await using var sqlServerProvider = sqlServerServices.BuildServiceProvider();
        await using var redisProvider = redisServices.BuildServiceProvider();

        Assert.IsType<SqliteJobStore>(sqliteProvider.GetRequiredService<IJobStore>());
        Assert.IsType<MySqlJobStore>(mySqlProvider.GetRequiredService<IJobStore>());
        Assert.IsType<SqlServerJobStore>(sqlServerProvider.GetRequiredService<IJobStore>());
        Assert.IsType<RedisJobStore>(redisProvider.GetRequiredService<IJobStore>());
        Assert.Equal(TimeSpan.FromMilliseconds(350), sqliteProvider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task ProviderRegistrationWithoutArguments_Throws_WhenProviderSettingIsMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLSchedulerSqlite();

        await using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IJobStore>());
        Assert.Contains("LScheduler:Sqlite:DatabasePath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderRegistrationWithoutArguments_Throws_WhenNoConfigurationIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddLSchedulerRedis();

        await using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerSqlite_RegistersSqliteStore()
    {
        var services = new ServiceCollection();
        var databasePath = Path.Combine(CreateTempDirectory(), "lscheduler.db");

        services.AddLSchedulerSqlite(databasePath);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<SqliteJobStore>(provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerSqlite_WithConfiguration_RegistersSqliteStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:DataDirectory"] = directory,
                ["LScheduler:ScanInterval"] = "00:00:00.25",
                ["LScheduler:Sqlite:DatabasePath"] = Path.Combine(directory, "provider.db"),
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerSqlite(configuration);

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqliteJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(250), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerSqlite_WithManualOptions_RegistersSqliteStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var services = new ServiceCollection();

        services.AddLSchedulerSqlite(Path.Combine(directory, "provider.db"), options =>
        {
            options.DataDirectory = directory;
            options.ScanInterval = TimeSpan.FromMilliseconds(125);
        });

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqliteJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(125), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerSqlite_WithDatabasePathAndConfiguration_RegistersSqliteStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:DataDirectory"] = directory,
                ["LScheduler:ScanInterval"] = "00:00:00.20",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerSqlite(Path.Combine(directory, "provider.db"), configuration);

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqliteJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(200), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerMySql_RegistersMySqlStore()
    {
        var services = new ServiceCollection();

        services.AddLSchedulerMySql("Server=localhost;Database=lscheduler;User Id=root;Password=test;");

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<MySqlJobStore>(provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerMySql_WithConfiguration_RegistersMySqlStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:MySql:ConnectionString"] = "Server=localhost;Database=lscheduler;User Id=root;Password=test;",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerMySql(configuration);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<MySqlJobStore>(provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerMySql_WithManualOptions_RegistersMySqlStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var services = new ServiceCollection();

        services.AddLSchedulerMySql(
            "Server=localhost;Database=lscheduler;User Id=root;Password=test;",
            options =>
            {
                options.DataDirectory = directory;
                options.ScanInterval = TimeSpan.FromMilliseconds(180);
            });

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<MySqlJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(180), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerMySql_WithConnectionStringAndConfiguration_RegistersMySqlStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:DataDirectory"] = directory,
                ["LScheduler:ScanInterval"] = "00:00:00.22",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerMySql(
            "Server=localhost;Database=lscheduler;User Id=root;Password=test;",
            configuration);

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<MySqlJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(220), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerSqlServer_RegistersSqlServerStore()
    {
        var services = new ServiceCollection();

        services.AddLSchedulerSqlServer("Server=localhost;Database=lscheduler;User Id=sa;Password=Password123!;TrustServerCertificate=true;");

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<SqlServerJobStore>(provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerSqlServer_WithConfiguration_RegistersSqlServerStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:SqlServer:ConnectionString"] = "Server=localhost;Database=lscheduler;User Id=sa;Password=Password123!;TrustServerCertificate=true;",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerSqlServer(configuration);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<SqlServerJobStore>(provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerSqlServer_WithManualOptions_RegistersSqlServerStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var services = new ServiceCollection();

        services.AddLSchedulerSqlServer(
            "Server=localhost;Database=lscheduler;User Id=sa;Password=Password123!;TrustServerCertificate=true;",
            options =>
            {
                options.DataDirectory = directory;
                options.ScanInterval = TimeSpan.FromMilliseconds(140);
            });

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqlServerJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(140), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerSqlServer_WithConnectionStringAndConfiguration_RegistersSqlServerStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:DataDirectory"] = directory,
                ["LScheduler:ScanInterval"] = "00:00:00.16",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerSqlServer(
            "Server=localhost;Database=lscheduler;User Id=sa;Password=Password123!;TrustServerCertificate=true;",
            configuration);

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqlServerJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(160), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerRedis_RegistersRedisStore()
    {
        var services = new ServiceCollection();

        services.AddLSchedulerRedis("localhost:6379");

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<RedisJobStore>(provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerRedis_WithConfiguration_RegistersRedisStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:Redis:Configuration"] = "localhost:6379",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerRedis(configuration);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<RedisJobStore>(provider.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task AddLSchedulerRedis_WithManualOptions_RegistersRedisStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var services = new ServiceCollection();

        services.AddLSchedulerRedis("localhost:6379", options =>
        {
            options.DataDirectory = directory;
            options.ScanInterval = TimeSpan.FromMilliseconds(110);
        });

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<RedisJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(110), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    [Fact]
    public async Task AddLSchedulerRedis_WithConfigurationStringAndConfiguration_RegistersRedisStoreAndBindsCoreOptions()
    {
        var directory = CreateTempDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LScheduler:DataDirectory"] = directory,
                ["LScheduler:ScanInterval"] = "00:00:00.09",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLSchedulerRedis("localhost:6379", configuration);

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<RedisJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Equal(directory, provider.GetRequiredService<JobSchedulerOptions>().DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(90), provider.GetRequiredService<JobSchedulerOptions>().ScanInterval);
    }

    public void Dispose()
    {
        foreach (var directory in _directoriesToDelete.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lscheduler-host-tests", Guid.NewGuid().ToString("N"));
        _directoriesToDelete.Add(directory);
        return directory;
    }

    public sealed class PersistentExecutionSink
    {
        public TaskCompletionSource CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Messages { get; } = [];

        public void Record(string message)
        {
            Messages.Add(message);
            CompletionSource.TrySetResult();
        }
    }

    public sealed class PersistentWorkerJobs
    {
        private readonly PersistentExecutionSink _sink;

        public PersistentWorkerJobs(PersistentExecutionSink sink)
        {
            _sink = sink;
        }

        public Task RunAsync(PersistentPayload payload, CancellationToken cancellationToken)
        {
            _sink.Record(payload.Message);
            return Task.CompletedTask;
        }
    }

    public sealed record PersistentPayload(string Message);

    public static class StaticSignalJobs
    {
        public static TaskCompletionSource CompletionSource { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            CompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static Task MarkAsync(CancellationToken cancellationToken)
        {
            CompletionSource.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
