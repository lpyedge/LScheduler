# Architecture

## Scope

`LScheduler` is a single-process embedded scheduler.

It provides:

- Cron jobs
- recurring interval jobs
- one-time delay jobs
- retry handling
- execution result persistence with per-outcome switches and retention
- durable restart restoration for method-based jobs
- zero-config startup, `LScheduler` section binding, and code-only configuration

It does not provide:

- distributed coordination
- multi-node locking
- dashboard UI

## Modules

### `src/LScheduler`

Core runtime and default file-backed implementation.

Main types:

- `JobScheduler`
- `JobSchedule`
- `JobSchedulerOptions`
- `ExecutionLogOptions`
- `RetryPolicy`
- `JobDefinition`
- `JobExecutionResult`
- `IJobStore`
- `FileJobStore`
- `JobExecutor`
- `CronHelper`
- `ConfigureJobSchedulerOptions` (internal, binds the `LScheduler` section)

### `src/LScheduler.Sqlite`

SQLite-backed `IJobStore` plus `AddLSchedulerSqlite(...)`.

### `src/LScheduler.MySql`

MySQL-backed `IJobStore` plus `AddLSchedulerMySql(...)`.

### `src/LScheduler.SqlServer`

SQL Server-backed `IJobStore` plus `AddLSchedulerSqlServer(...)`.

### `src/LScheduler.Redis`

Redis-backed `IJobStore` plus `AddLSchedulerRedis(...)`.

## Responsibilities

### `JobScheduler`

`JobScheduler` owns:

- runtime registration
- duplicate `JobId` rejection
- loading persisted jobs from `IJobStore`
- rebuilding durable actions from saved method metadata
- active in-memory job registry
- scan loop
- next run calculation
- persistent job removal
- waiting for in-flight executions on `StopAsync`
- purging expired execution results (at start, then at most once per hour)

### `JobExecutor`

`JobExecutor` owns:

- invoking the job action
- retry loop
- retry delay calculation
- writing the `JobExecutionResult` of attempts selected by `ExecutionLogOptions`, even if shutdown was requested after the attempt finished

### `IJobStore`

`IJobStore` owns:

- loading persisted `JobDefinition` records
- saving updated `JobDefinition` records
- deleting jobs
- appending execution results
- purging execution results older than a cutoff

## Dependency Shape

- `JobScheduler` depends on `IJobStore`
- `JobScheduler` depends on `JobExecutor`
- providers implement `IJobStore`
- provider packages depend on `LScheduler`

There is one persistence abstraction and one scheduler execution path.

## Public Registration Surface

Core registration:

- `AddLScheduler()`
- `AddLScheduler(configuration)`
- `AddLScheduler(configurationSection)`
- `AddLScheduler(options => ...)`

Options are resolved in this order, later steps override earlier ones:

1. built-in defaults
2. the `LScheduler` section of the application `IConfiguration`, bound automatically by every overload when one is registered
3. the configuration or delegate passed to the overload

Provider registration:

- `AddLSchedulerSqlite(...)`
- `AddLSchedulerMySql(...)`
- `AddLSchedulerSqlServer(...)`
- `AddLSchedulerRedis(...)`

Each provider has a parameterless overload that reads its key from the application `IConfiguration`, an overload taking an `IConfiguration`, and overloads taking the setting directly (optionally with an options delegate or configuration).

All registrations produce the same singleton `JobScheduler`.

Provider configuration from `IConfiguration` uses these keys:

- `LScheduler:Sqlite:DatabasePath`
- `LScheduler:MySql:ConnectionString`
- `LScheduler:SqlServer:ConnectionString`
- `LScheduler:Redis:Configuration`

## Public Runtime Surface

### Persistent jobs

- `JobScheduler.AddJobAsync(string jobId, JobSchedule schedule, Func<CancellationToken, Task> method, ...)`
- `JobScheduler.AddJobAsync<TPayload>(string jobId, JobSchedule schedule, Func<TPayload, CancellationToken, Task> method, TPayload payload, ...)`

Persistent jobs are the primary path.

They persist:

- schedule
- retry policy
- last/next run timestamps
- method type
- method name
- static/instance flag
- payload type
- payload JSON

### In-memory jobs

- `JobScheduler.AddInMemoryJobAsync(...)`

In-memory jobs are not saved to `IJobStore` and do not survive restart.

### Query and removal

- `JobScheduler.GetJobsAsync()`
- `JobScheduler.ExistsAsync(jobId)`
- `JobScheduler.RemoveJobAsync(jobId)`

Registering an existing `JobId` is rejected. A job is replaced by removing it and adding it again.

## Durable Job Model

Durable restoration is method-based.

There is no separate handler registry.

Supported persistent method shapes:

- `Task Method(CancellationToken cancellationToken)`
- `Task Method(TPayload payload, CancellationToken cancellationToken)`

Supported targets:

- public static methods
- public instance methods on DI-resolvable types
- public instance methods on constructible types

Rejected for persistent jobs:

- lambdas
- local functions
- closures
- compiler-generated delegates

Those cases must use `AddInMemoryJobAsync(...)`.

## Restore Flow

1. `LSchedulerHostedService` starts the singleton `JobScheduler`.
2. `JobScheduler` loads persisted jobs from `IJobStore`.
3. For each job with method metadata, the scheduler resolves the saved type and method.
4. For instance methods:
   - `ActivatorUtilities.GetServiceOrCreateInstance(...)` is used when DI is available.
   - `Activator.CreateInstance(...)` is used otherwise.
5. Payload JSON is deserialized when needed.
6. The rebuilt action is added back into the active in-memory registry.

Restored jobs keep their stored `LastRunTimeUtc` and `NextRunTimeUtc`; a run that came due while the application was down executes once on start.

## Scheduling Flow

1. Application code resolves `JobScheduler`.
2. A job is registered through `AddJobAsync(...)` or `AddInMemoryJobAsync(...)`.
3. Duplicate active `JobId` values are rejected.
4. Persistent jobs are saved immediately.
5. The background loop scans registered jobs on `ScanInterval`. A failed scan is logged to `Console.Error` and does not stop the loop.
6. Due jobs are executed through `JobExecutor`. A job that is still running is not started again until it finishes.
   Jobs removed while a scan is in progress are skipped.
7. Recurring jobs persist their new `NextRunTimeUtc`.
8. A one-time persistent job is removed from the store when its run completes, whether it succeeded or its last retry failed. A run cancelled by shutdown leaves the job in the store.

## Execution Flow

1. `JobExecutor` invokes the job action. An attempt either succeeds or fails.
2. A failed attempt is retried up to `RetryPolicy.MaxRetries` times, waiting `RetryInterval` (multiplied by `2^n` for retry number `n` with exponential backoff).
3. Each attempt has one outcome, and `ExecutionLogOptions` decides which outcomes are written through `IJobStore`:
   - `Success`: the attempt succeeded (default off)
   - `Retry`: the attempt failed and another attempt follows (default on)
   - `Failure`: the attempt failed and no retry is left (default on)
4. `JobScheduler` purges results older than `ExecutionLogOptions.Retention` when it starts and then at most once per hour.

## Persistence Shape

Persisted `JobDefinition` data includes:

- `JobId`
- `Cron`
- `Interval`
- `RunOnce`
- `MethodTypeName`
- `MethodName`
- `MethodIsStatic`
- `PayloadTypeName`
- `PayloadJson`
- `LastRunTimeUtc`
- `NextRunTimeUtc`
- `RetryPolicy`

`Action` is runtime-only and is never stored.

## Providers

### File

Files:

- `jobs.json`
- `execution_log.jsonl`

`jobs.json` is written to a `.tmp` file first and then swapped in, so a crash cannot leave a truncated file.
`execution_log.jsonl` is append-only, one JSON object per line; purging rewrites it the same way.

### SQLite / MySQL / SQL Server

Jobs table stores:

- schedule
- timing
- retry policy
- method metadata
- payload metadata

Purging deletes rows whose `EndTimeUtc` is older than the cutoff.

Execution log table stores:

- `JobId`
- `StartTimeUtc`
- `EndTimeUtc`
- `Success`
- `Exception`
- `RetryCount`

### Redis

- a hash stores jobs
- a list stores execution log entries; purging trims expired entries from the head

## Locked Decisions

- single-process only
- flat core architecture
- `JobScheduler` coordinates and `JobExecutor` executes
- `IJobStore` is the only persistence abstraction
- persistent jobs are method-based
- file persistence is the default path
- option precedence is defaults, then the `LScheduler` configuration section, then code
- the execution log records failures and retries by default; success logging is opt-in
