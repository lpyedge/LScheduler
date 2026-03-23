using System.Text.Json;
using LScheduler;
using StackExchange.Redis;

namespace LScheduler.Redis;

public sealed class RedisJobStore : IJobStore, IAsyncDisposable
{
    private readonly string _jobsKey;
    private readonly string _executionLogKey;
    private readonly string _configuration;
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
        var payload = JsonSerializer.Serialize(new JobDefinition
        {
            JobId = job.JobId,
            Cron = job.Cron,
            Interval = job.Interval,
            RunOnce = job.RunOnce,
            IsPersistent = job.IsPersistent,
            MethodTypeName = job.MethodTypeName,
            MethodName = job.MethodName,
            MethodIsStatic = job.MethodIsStatic,
            PayloadTypeName = job.PayloadTypeName,
            PayloadJson = job.PayloadJson,
            LastRunTimeUtc = job.LastRunTimeUtc,
            NextRunTimeUtc = job.NextRunTimeUtc,
            RetryPolicy = job.RetryPolicy,
        });

        await database.HashSetAsync(_jobsKey, job.JobId, payload);
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

    public async ValueTask DisposeAsync()
    {
        if (_connectionTask is null)
        {
            return;
        }

        if (_connectionTask.Status == TaskStatus.RanToCompletion)
        {
            await _connectionTask.Result.DisposeAsync();
            return;
        }

        var connection = await _connectionTask;
        await connection.DisposeAsync();
    }

    private async Task<IDatabase> GetDatabaseAsync()
    {
        _connectionTask ??= ConnectionMultiplexer.ConnectAsync(_configuration);
        var connection = await _connectionTask;
        return connection.GetDatabase();
    }
}
