# Architecture

## Scope

`LScheduler` is a single-process embedded scheduler.

It provides:

- Cron jobs
- recurring interval jobs
- one-time delay jobs
- retry handling
- execution result persistence
- durable restart restoration for method-based jobs

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
- `RetryPolicy`
- `JobDefinition`
- `JobExecutionResult`
- `IJobStore`
- `FileJobStore`
- `JobExecutor`
- `CronHelper`

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

### `JobExecutor`

`JobExecutor` owns:

- invoking the job action
- writing `JobExecutionResult`
- retry loop
- retry delay calculation

### `IJobStore`

`IJobStore` owns:

- loading persisted `JobDefinition` records
- saving updated `JobDefinition` records
- deleting jobs
- appending execution results

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

Provider registration:

- `AddLSchedulerSqlite(...)`
- `AddLSchedulerMySql(...)`
- `AddLSchedulerSqlServer(...)`
- `AddLSchedulerRedis(...)`

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
- `JobScheduler.RemoveJobAsync(jobId)`

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

## Scheduling Flow

1. Application code resolves `JobScheduler`.
2. A job is registered through `AddJobAsync(...)` or `AddInMemoryJobAsync(...)`.
3. Duplicate active `JobId` values are rejected.
4. Persistent jobs are saved immediately.
5. The background loop scans registered jobs on `ScanInterval`.
6. Due jobs are executed through `JobExecutor`.
7. Recurring jobs persist their new `NextRunTimeUtc`.
8. Successful completion of a one-time persistent job removes it from the store.

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
- `execution_log.json`

### SQLite / MySQL / SQL Server

Jobs table stores:

- schedule
- timing
- retry policy
- method metadata
- payload metadata

Execution log table stores:

- `JobId`
- `StartTimeUtc`
- `EndTimeUtc`
- `Success`
- `Exception`
- `RetryCount`

### Redis

- a hash stores jobs
- a list stores execution log entries

## Locked Decisions

- single-process only
- flat core architecture
- `JobScheduler` coordinates and `JobExecutor` executes
- `IJobStore` is the only persistence abstraction
- persistent jobs are method-based
- file persistence is the default path
