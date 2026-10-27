# Jobs and Schedules

Resolve `JobScheduler` from DI and register jobs at runtime:

```csharp
var scheduler = app.Services.GetRequiredService<JobScheduler>();
```

## Persistent Jobs

`AddJobAsync(...)` is the main path.

It persists:

- `JobId`
- schedule
- retry policy
- `LastRunTimeUtc`
- `NextRunTimeUtc`
- method type
- method name
- static / instance flag
- payload type
- payload JSON

Supported persistent signatures:

- `Task Method(CancellationToken cancellationToken)`
- `Task Method(TPayload payload, CancellationToken cancellationToken)`

Example with a static method:

```csharp
await scheduler.AddJobAsync(
    "cleanup",
    JobSchedule.DailyAt(2, 0),
    MaintenanceJobs.CleanupAsync);
```

Example with payload and a DI service method:

```csharp
builder.Services.AddSingleton<EmailJobs>();

var app = builder.Build();
var scheduler = app.Services.GetRequiredService<JobScheduler>();
var emailJobs = app.Services.GetRequiredService<EmailJobs>();

await scheduler.AddJobAsync(
    "daily-digest",
    JobSchedule.DailyAt(8, 30),
    emailJobs.SendDigestAsync,
    new DigestEmailArgs("ops@example.com", "daily-digest"));

public sealed class EmailJobs
{
    public Task SendDigestAsync(DigestEmailArgs payload, CancellationToken cancellationToken)
    {
        Console.WriteLine($"send {payload.Template} to {payload.Recipient}");
        return Task.CompletedTask;
    }
}

public sealed record DigestEmailArgs(string Recipient, string Template);
```

## In-Memory Jobs

`AddInMemoryJobAsync(...)` is for temporary runtime work that should not survive restart.

Use it for:

- lambdas
- closures
- local functions
- one-off runtime logic

```csharp
await scheduler.AddInMemoryJobAsync(
    "warmup",
    JobSchedule.Delay(TimeSpan.FromSeconds(10)),
    cancellationToken =>
    {
        Console.WriteLine("warming up");
        return Task.CompletedTask;
    });
```

## JobSchedule

```csharp
JobSchedule.Cron("*/5 * * * * *");
JobSchedule.Every(TimeSpan.FromMinutes(5));
JobSchedule.Delay(TimeSpan.FromSeconds(30));
JobSchedule.DailyAt(8, 30);
JobSchedule.WeeklyAt(9, 0, DayOfWeek.Monday, DayOfWeek.Friday);
JobSchedule.MonthlyAt(12, 0, 5, 15, 25);
JobSchedule.YearlyAt(12, 25, 8, 30);
```

Notes:

- `Cron(...)` accepts 5-field and 6-field expressions (the 6-field form starts with seconds), evaluated in UTC
- `Every(...)` is recurring
- `Delay(...)` runs once; a persistent one-time job is removed from the store when its run completes
- the daily / weekly / monthly / yearly builders produce 6-field cron expressions

## Execution Rules

- A job never runs concurrently with itself. If a scan finds a job still running (including its retries), that run is deferred until the previous one finishes.
- A failed store write does not stop the scan loop; the error is written to `Console.Error` and the next scan continues.
- `StopAsync` cancels running jobs and waits for them to finish. Jobs should honor the `CancellationToken`.

## Retry Policy

The global retry policy comes from `JobSchedulerOptions`.

- `MaxRetries` is the number of retries after the first attempt, so a job runs at most `MaxRetries + 1` times per due time.
- `RetryInterval` is the wait before a retry. With `UseExponentialBackoff` the wait is `RetryInterval * 2^n` for retry number `n`, so the first retry waits twice `RetryInterval`.
- A job whose last retry fails is not run again until its next scheduled time (a one-time job is removed).
- Which attempts are written to the execution log is controlled by `ExecutionLog`, see [Configuration](configuration.md#execution-log).

Per-job retry policy overrides the global default:

```csharp
await scheduler.AddJobAsync(
    "report-job",
    JobSchedule.Every(TimeSpan.FromMinutes(5)),
    ReportJobs.RunAsync,
    retryPolicy: new RetryPolicy
    {
        MaxRetries = 5,
        RetryInterval = TimeSpan.FromSeconds(2),
        UseExponentialBackoff = true
    });
```

## Querying and Removing Jobs

```csharp
var jobs = await scheduler.GetJobsAsync();
var exists = await scheduler.ExistsAsync("daily-digest");
var removed = await scheduler.RemoveJobAsync("daily-digest");
```

`ExistsAsync(jobId)` covers persistent and in-memory jobs. `RemoveJobAsync(jobId)` returns `false` when the job does not exist; it does not cancel a run that is already in progress.

## Replacing a Job

Registering an existing `JobId` throws. To overwrite, remove it first:

```csharp
if (await scheduler.ExistsAsync("daily-digest"))
{
    await scheduler.RemoveJobAsync("daily-digest");
}

await scheduler.AddJobAsync("daily-digest", JobSchedule.DailyAt(9, 0), EmailJobs.SendDigestAsync);
```

A replaced persistent job starts with a new `NextRunTimeUtc` calculated from its new schedule.

`GetJobsAsync()` returns persisted metadata only. `Action` is not exposed.
