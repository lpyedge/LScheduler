namespace LScheduler;

internal sealed class JobExecutor
{
    private readonly IJobStore _store;

    public JobExecutor(IJobStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
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
                result.EndTimeUtc = DateTime.UtcNow;
                await _store.SaveExecutionResultAsync(result, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Exception = ex.ToString();
                result.EndTimeUtc = DateTime.UtcNow;
                await _store.SaveExecutionResultAsync(result, cancellationToken);

                if (retryCount >= retryPolicy.MaxRetries)
                {
                    return true;
                }

                retryCount++;
                var delay = GetRetryDelay(retryPolicy, retryCount);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return false;
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
