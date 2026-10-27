namespace LScheduler;

public interface IJobStore
{
    Task<IReadOnlyCollection<JobDefinition>> LoadJobsAsync(CancellationToken cancellationToken = default);

    Task SaveJobAsync(JobDefinition job, CancellationToken cancellationToken = default);

    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);

    Task SaveExecutionResultAsync(JobExecutionResult result, CancellationToken cancellationToken = default);

    Task PurgeExecutionResultsAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
}
