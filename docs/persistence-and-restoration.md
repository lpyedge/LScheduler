# Persistence and Restart Restoration

Persistent jobs are restored automatically after restart.

`LScheduler` reloads the saved job definition from the configured store and rebuilds the executable action by:

- resolving the saved method type
- locating the saved public method
- creating the target instance
  - through DI when a root `IServiceProvider` is available
  - through `Activator.CreateInstance(...)` otherwise
- deserializing the saved payload JSON when needed

There is no separate handler registration API.

## What Persistent Jobs Require

Persistent jobs must use a direct method group on a stable named type.

Supported:

- public static methods
- public instance methods on DI-resolvable services
- public instance methods on constructible types

Not supported for persistent jobs:

- anonymous lambdas
- closures
- local functions
- compiler-generated delegates

Those should use `AddInMemoryJobAsync(...)`.

## Restoration Semantics

On start, every persisted job that has method metadata is loaded and becomes active:

- the saved schedule, retry policy, and payload are used as stored
- `LastRunTimeUtc` and `NextRunTimeUtc` are restored
- a run that came due while the application was down is executed once on start, then the next run is calculated from the current time

Because restored jobs are already active, `AddJobAsync(...)` with the same `JobId` throws `InvalidOperationException`.
Check `ExistsAsync(jobId)` before registering, or remove the job first to replace it.
A replaced job gets a new `NextRunTimeUtc` calculated from its new schedule.

One-time persistent jobs are removed from the store when their run completes, either successfully or after the last retry has failed.

If a one-time persistent job is cancelled during shutdown before it completes, the persisted job remains in the store and runs again on the next start.

## Execution Log Storage

Execution results are written to the same store as the jobs, according to the `ExecutionLog` options (see [Configuration](configuration.md#execution-log)):

| Provider | Storage |
|---|---|
| File | `execution_log.jsonl`, append-only, one JSON object per line |
| SQLite / MySQL / SQL Server | `ExecutionLog` table |
| Redis | list (`lscheduler:execution-log`) |

Results older than `ExecutionLog.Retention` are purged when the scheduler starts and then at most once per hour.
