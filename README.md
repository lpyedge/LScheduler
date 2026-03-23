# LScheduler

`LScheduler` is a lightweight embedded .NET job scheduler for single-process applications.

It supports:

- Cron schedules
- recurring interval schedules
- one-time delay schedules
- retry policies
- file persistence by default
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

await scheduler.AddJobAsync(
    "heartbeat",
    JobSchedule.Every(TimeSpan.FromSeconds(30)),
    DemoJobs.HeartbeatAsync);

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

What this gives you:

- a singleton `JobScheduler`
- hosted startup and shutdown
- file persistence in `AppContext.BaseDirectory/lscheduler-data`
- persisted schedule metadata, retry policy, next run time, method identity, and optional payload

## Packages

- `LScheduler`
- `LScheduler.Sqlite`
- `LScheduler.MySql`
- `LScheduler.SqlServer`
- `LScheduler.Redis`

## Core API

Resolve `JobScheduler` from DI and register jobs at runtime:

```csharp
var scheduler = app.Services.GetRequiredService<JobScheduler>();
```

Use `AddJobAsync(...)` for persistent jobs.

Use `AddInMemoryJobAsync(...)` for temporary runtime work such as lambdas and closures.

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
