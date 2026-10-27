# Integration Patterns

The Worker and ASP.NET Core samples keep runtime registration inside a reusable registrar class.

This avoids scattering `AddJobAsync(...)` calls across startup code and makes duplicate checks explicit with `ExistsAsync(...)`.

Persistent jobs are restored from the store before the registrar runs, so an existing job is skipped and keeps its saved schedule and `NextRunTimeUtc`.
To change the schedule or payload of an existing job, remove it and add it again, see [Replacing a Job](jobs-and-schedules.md#replacing-a-job).

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
        if (!await _scheduler.ExistsAsync("daily-digest-job", cancellationToken))
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
