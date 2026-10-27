# LScheduler

`LScheduler` is a lightweight embedded .NET job scheduler for single-process applications.

It supports:

- Cron schedules
- recurring interval schedules
- one-time delay schedules
- retry policies
- file persistence by default
- zero-config startup, automatic `LScheduler` section binding, or code-only configuration
- execution log with per-outcome switches and automatic retention
- optional SQLite / MySQL / SQL Server / Redis stores
- persistent jobs that survive process restarts
- in-memory jobs for temporary runtime work

## Quick Start

Default registration uses file persistence.

```csharp
using LScheduler;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLScheduler();

var app = builder.Build();

var scheduler = app.Services.GetRequiredService<JobScheduler>();

if (!await scheduler.ExistsAsync("heartbeat"))
{
    await scheduler.AddJobAsync(
        "heartbeat",
        JobSchedule.Every(TimeSpan.FromSeconds(30)),
        DemoJobs.HeartbeatAsync);
}

await app.RunAsync();

public static class DemoJobs
{
    public static Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.UtcNow:O}] heartbeat");
        return Task.CompletedTask;
    }
}
```

Persistent jobs are restored from the store on restart, and registering an existing `JobId` throws, so check `ExistsAsync(jobId)` first as shown above.
To change a job, `RemoveJobAsync(jobId)` it and add it again.

What this gives you:

- a singleton `JobScheduler`
- hosted startup and shutdown
- file persistence in `AppContext.BaseDirectory/lscheduler-data`
- persisted schedule metadata, retry policy, next run time, method identity, and optional payload
- an execution log that records failures and retries by default, with 30-day retention

Settings come from built-in defaults, then the `LScheduler` section of `appsettings.json` (read automatically), then code. See [Configuration](docs/configuration.md).

## Packages

- `LScheduler`
- `LScheduler.Sqlite`
- `LScheduler.MySql`
- `LScheduler.SqlServer`
- `LScheduler.Redis`

Each provider package adds one `AddLSchedulerXxx()` registration that reads its connection setting from the `LScheduler:Xxx` configuration section. See [Storage providers](docs/providers.md).

## Core API

Resolve `JobScheduler` from DI and register jobs at runtime:

```csharp
var scheduler = app.Services.GetRequiredService<JobScheduler>();
```

Use `AddJobAsync(...)` for persistent jobs.

Use `AddInMemoryJobAsync(...)` for temporary runtime work such as lambdas and closures.

Use `ExistsAsync(jobId)`, `GetJobsAsync()` and `RemoveJobAsync(jobId)` to inspect and manage registered jobs.

`JobSchedule` supports:

- Cron
- recurring intervals
- one-time delay
- daily / weekly / monthly / yearly builder-style schedules

## Documentation

- [Configuration and appsettings](docs/configuration.md)
- [Storage providers](docs/providers.md)
- [Jobs, schedules, retries, and runtime APIs](docs/jobs-and-schedules.md)
- [Persistence and restart restoration](docs/persistence-and-restoration.md)
- [Worker / ASP.NET Core integration patterns](docs/integration-patterns.md)
- [Releasing packages](docs/releasing.md)

## Key Rules

Persistent jobs require a direct public method group on a stable named type.

Good fits:

- public static methods
- public instance methods on DI services
- public instance methods on constructible types

Use `AddInMemoryJobAsync(...)` for:

- lambdas
- closures
- local functions
- compiler-generated delegates
