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

- `Cron(...)` accepts 5-field and 6-field expressions
- `Every(...)` is recurring
- `Delay(...)` runs once

## Retry Policy

The global retry policy comes from `JobSchedulerOptions`.

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
var removed = await scheduler.RemoveJobAsync("daily-digest");
```

`GetJobsAsync()` returns persisted metadata only. `Action` is not exposed.
