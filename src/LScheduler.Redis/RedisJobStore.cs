using System.Text.Json;
using LScheduler;
using StackExchange.Redis;

namespace LScheduler.Redis;

public sealed class RedisJobStore : IJobStore, IAsyncDisposable
{
    private readonly string _jobsKey;
    private readonly string _executionLogKey;
    private readonly string _configuration;
    private readonly object _connectionLock = new();
    private Task<ConnectionMultiplexer>? _connectionTask;

    public RedisJobStore(
        string configuration,
        string jobsKey = "lscheduler:jobs",
        string executionLogKey = "lscheduler:execution-log")
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new ArgumentException("Redis configuration is required.", nameof(configuration));
        }

        _configuration = configuration;
        _jobsKey = jobsKey;
        _executionLogKey = executionLogKey;
    }

    public async Task<IReadOnlyCollection<JobDefinition>> LoadJobsAsync(CancellationToken cancellationToken = default)
    {
        var database = await GetDatabaseAsync();
        var entries = await database.HashGetAllAsync(_jobsKey);

        return entries
            .Where(entry => entry.Value.HasValue)
            .Select(entry => JsonSerializer.Deserialize<JobDefinition>(entry.Value.ToString()) ?? new JobDefinition())
            .ToArray();
    }

    public async Task SaveJobAsync(JobDefinition job, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(job, nameof(job));

        var database = await GetDatabaseAsync();
        await database.HashSetAsync(_jobsKey, job.JobId, JsonSerializer.Serialize(job));
    }

    public async Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        var database = await GetDatabaseAsync();
        await database.HashDeleteAsync(_jobsKey, jobId);
    }

    public async Task SaveExecutionResultAsync(JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(result, nameof(result));

        var database = await GetDatabaseAsync();
        var payload = JsonSerializer.Serialize(result);
        await database.ListRightPushAsync(_executionLogKey, payload);
    }

    public async Task PurgeExecutionResultsAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        const int batchSize = 1000;

        var database = await GetDatabaseAsync();

        while (true)
        {
            var batch = await database.ListRangeAsync(_executionLogKey, 0, batchSize - 1);
            var expiredCount = 0;

            foreach (var entry in batch)
            {
                var result = JsonSerializer.Deserialize<JobExecutionResult>(entry.ToString());

                if (result is not null && result.EndTimeUtc >= olderThanUtc)
                {
                    break;
                }

                expiredCount++;
            }

            if (expiredCount == 0)
            {
                return;
            }

            await database.ListTrimAsync(_executionLogKey, expiredCount, -1);

            if (expiredCount < batch.Length)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<ConnectionMultiplexer>? connectionTask;

        lock (_connectionLock)
        {
            connectionTask = _connectionTask;
            _connectionTask = null;
        }

        if (connectionTask is null)
        {
            return;
        }

        ConnectionMultiplexer connection;

        try
        {
            connection = await connectionTask;
        }
        catch
        {
            return;
        }

        await connection.DisposeAsync();
    }

    private async Task<IDatabase> GetDatabaseAsync()
    {
        Task<ConnectionMultiplexer> connectionTask;

        lock (_connectionLock)
        {
            connectionTask = _connectionTask ??= ConnectionMultiplexer.ConnectAsync(_configuration);
        }

        try
        {
            var connection = await connectionTask;
            return connection.GetDatabase();
        }
        catch
        {
            lock (_connectionLock)
            {
                if (ReferenceEquals(_connectionTask, connectionTask))
                {
                    _connectionTask = null;
                }
            }

            throw;
        }
    }
}
