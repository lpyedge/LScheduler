# Integration Patterns

The Worker and ASP.NET Core samples keep runtime registration inside a reusable registrar class.

This avoids scattering `AddJobAsync(...)` calls across startup code and makes duplicate checks explicit.

## Example Registrar

```csharp
public sealed class WorkerJobRegistrar
{
    private readonly JobScheduler _scheduler;
    private readonly DailyDigestJobs _dailyDigestJobs;

    public WorkerJobRegistrar(JobScheduler scheduler, DailyDigestJobs dailyDigestJobs)
    {
        _scheduler = scheduler;
        _dailyDigestJobs = dailyDigestJobs;
    }

    public async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var jobs = await _scheduler.GetJobsAsync(cancellationToken);

        if (jobs.All(job => job.JobId != "daily-digest-job"))
        {
            await _scheduler.AddJobAsync(
                "daily-digest-job",
                JobSchedule.DailyAt(8, 30),
                _dailyDigestJobs.SendDailyDigestAsync,
                new DailyDigestJobArgs("ops@example.com", "daily-digest"),
                cancellationToken: cancellationToken);
        }
    }
}
```

## Sample Projects

- `samples/LScheduler.WorkerSample`
- `samples/LScheduler.WebSample`
