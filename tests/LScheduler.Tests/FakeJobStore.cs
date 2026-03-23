using LScheduler;

namespace LScheduler.Tests;

internal sealed class FakeJobStore : IJobStore
{
    public Dictionary<string, JobDefinition> SavedJobs { get; } = new(StringComparer.Ordinal);

    public List<JobExecutionResult> ExecutionResults { get; } = [];

    public Task<IReadOnlyCollection<JobDefinition>> LoadJobsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<JobDefinition> jobs = SavedJobs.Values
            .Select(CloneJob)
            .ToArray();

        return Task.FromResult(jobs);
    }

    public Task SaveJobAsync(JobDefinition job, CancellationToken cancellationToken = default)
    {
        SavedJobs[job.JobId] = CloneJob(job);
        return Task.CompletedTask;
    }

    public Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        SavedJobs.Remove(jobId);
        return Task.CompletedTask;
    }

    public Task SaveExecutionResultAsync(JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        ExecutionResults.Add(new JobExecutionResult
        {
            JobId = result.JobId,
            StartTimeUtc = result.StartTimeUtc,
            EndTimeUtc = result.EndTimeUtc,
            Success = result.Success,
            Exception = result.Exception,
            RetryCount = result.RetryCount,
        });

        return Task.CompletedTask;
    }

    private static JobDefinition CloneJob(JobDefinition source)
    {
        return new JobDefinition
        {
            JobId = source.JobId,
            Cron = source.Cron,
            Interval = source.Interval,
            RunOnce = source.RunOnce,
            IsPersistent = source.IsPersistent,
            MethodTypeName = source.MethodTypeName,
            MethodName = source.MethodName,
            MethodIsStatic = source.MethodIsStatic,
            PayloadTypeName = source.PayloadTypeName,
            PayloadJson = source.PayloadJson,
            LastRunTimeUtc = source.LastRunTimeUtc,
            NextRunTimeUtc = source.NextRunTimeUtc,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = source.RetryPolicy.MaxRetries,
                RetryInterval = source.RetryPolicy.RetryInterval,
                UseExponentialBackoff = source.RetryPolicy.UseExponentialBackoff,
            },
        };
    }
}
