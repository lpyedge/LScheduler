using LScheduler;

namespace LScheduler.WebSample;

public sealed class WebJobRegistrar
{
    private readonly JobScheduler _scheduler;
    private readonly RefreshCacheJobs _refreshCacheJobs;

    public WebJobRegistrar(JobScheduler scheduler, RefreshCacheJobs refreshCacheJobs)
    {
        _scheduler = scheduler;
        _refreshCacheJobs = refreshCacheJobs;
    }

    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await _scheduler.GetJobsAsync(cancellationToken);

        if (jobs.All(job => job.JobId != "cache-refresh-job"))
        {
            await _scheduler.AddJobAsync(
                "cache-refresh-job",
                JobSchedule.Every(TimeSpan.FromMinutes(15)),
                _refreshCacheJobs.RefreshAsync,
                new RefreshCacheJobArgs("catalog"),
                cancellationToken: cancellationToken);
        }

        if (jobs.All(job => job.JobId != "startup-warmup-job"))
        {
            await _scheduler.AddInMemoryJobAsync(
                "startup-warmup-job",
                JobSchedule.Delay(TimeSpan.FromSeconds(10)),
                _ =>
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] warming up cache");
                    return Task.CompletedTask;
                },
                cancellationToken: cancellationToken);
        }
    }
}

public sealed record RefreshCacheJobArgs(string CacheName);

public sealed class RefreshCacheJobs
{
    public Task RefreshAsync(RefreshCacheJobArgs payload, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.UtcNow:O}] refreshing cache '{payload.CacheName}'");
        return Task.CompletedTask;
    }
}
