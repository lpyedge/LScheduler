namespace LScheduler;

internal sealed class JobExecutor
{
    private readonly IJobStore _store;
    private readonly ExecutionLogOptions _executionLog;

    public JobExecutor(IJobStore store, ExecutionLogOptions? executionLog = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _executionLog = executionLog ?? new ExecutionLogOptions();
    }

    public async Task<bool> ExecuteAsync(JobDefinition job, CancellationToken cancellationToken)
    {
        Guard.NotNull(job, nameof(job));
        Guard.NotNull(job.Action, nameof(job.Action));
        Guard.NotNull(job.RetryPolicy, nameof(job.RetryPolicy));

        var action = job.Action!;
        var retryPolicy = job.RetryPolicy;

        var retryCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = new JobExecutionResult
            {
                JobId = job.JobId,
                StartTimeUtc = DateTime.UtcNow,
                RetryCount = retryCount,
            };

            try
            {
                await action(cancellationToken);
                result.Success = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                result.Exception = ex.ToString();
            }

            result.EndTimeUtc = DateTime.UtcNow;

            var willRetry = !result.Success && retryCount < retryPolicy.MaxRetries;

            if (ShouldLog(result.Success, willRetry))
            {
                await _store.SaveExecutionResultAsync(result, CancellationToken.None);
            }

            if (!willRetry)
            {
                return true;
            }

            retryCount++;
            await Task.Delay(GetRetryDelay(retryPolicy, retryCount), cancellationToken);
        }

        return false;
    }

    private bool ShouldLog(bool success, bool willRetry)
    {
        if (success)
        {
            return _executionLog.Success;
        }

        return willRetry ? _executionLog.Retry : _executionLog.Failure;
    }

    internal static TimeSpan GetRetryDelay(RetryPolicy retryPolicy, int retryCount)
    {
        Guard.NotNull(retryPolicy, nameof(retryPolicy));

        if (!retryPolicy.UseExponentialBackoff)
        {
            return retryPolicy.RetryInterval;
        }

        var multiplier = Math.Pow(2, retryCount);
        return TimeSpan.FromTicks((long)(retryPolicy.RetryInterval.Ticks * multiplier));
    }
}
