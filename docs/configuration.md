# Configuration

`LScheduler` supports three core registration styles:

- zero configuration with the default `FileJobStore`
- `appsettings.json` / `IConfiguration`
- manual `JobSchedulerOptions`

Options are resolved in this order, later steps override earlier ones:

1. built-in defaults
2. the `LScheduler` section of the application `IConfiguration`, read automatically by every `AddLScheduler(...)` overload when an `IConfiguration` is registered (the default for `Host` / `WebApplication` builders)
3. the `Action<JobSchedulerOptions>` passed in code

No configuration file is required: `AddLScheduler()` works with defaults only, and everything can be set from code with `AddLScheduler(options => ...)`.

## Zero Configuration

```csharp
builder.Services.AddLScheduler();
```

Default values:

- `FileJobStore`
- `AppContext.BaseDirectory/lscheduler-data`
- `ScanInterval = 1 second`
- `MaxRetries = 3`
- `RetryInterval = 10 seconds`
- `ExecutionLog.Success = false`, `Failure = true`, `Retry = true`, `Retention = 30 days`

## Execution Log

Every execution attempt has one outcome, and each outcome can be switched on or off:

| Option | Default | Logs |
|---|---|---|
| `ExecutionLog.Success` | `false` | an attempt that succeeded |
| `ExecutionLog.Failure` | `true` | an attempt that failed with no retry left |
| `ExecutionLog.Retry` | `true` | an attempt that failed and will be retried |
| `ExecutionLog.Retention` | `30.00:00:00` | results older than this are purged; `00:00:00` keeps them forever |

Purging runs when the scheduler starts and then at most once per hour.

```csharp
builder.Services.AddLScheduler(options =>
{
    options.ExecutionLog.Success = true;
    options.ExecutionLog.Retention = TimeSpan.FromDays(7);
});
```

```json
{
  "LScheduler": {
    "ExecutionLog": {
      "Success": true,
      "Failure": true,
      "Retry": false,
      "Retention": "7.00:00:00"
    }
  }
}
```

The file store writes `execution_log.jsonl` (one JSON object per line, append-only). The previous `execution_log.json` is no longer read or written and can be deleted.

## Basic appsettings.json

```json
{
  "LScheduler": {
    "DataDirectory": "data/lscheduler",
    "ScanInterval": "00:00:00.50",
    "RetryPolicy": {
      "MaxRetries": 5,
      "RetryInterval": "00:00:02",
      "UseExponentialBackoff": true
    }
  }
}
```

```csharp
builder.Services.AddLScheduler();
```

`AddLScheduler()` already reads the `LScheduler` section of the application configuration. Pass a configuration only when it is not the application's registered `IConfiguration`:

```csharp
builder.Services.AddLScheduler(myConfiguration);
builder.Services.AddLScheduler(myConfiguration.GetSection(JobSchedulerOptions.SectionName));
```

## Manual Options

```csharp
builder.Services.AddLScheduler(options =>
{
    options.DataDirectory = "data/lscheduler";
    options.ScanInterval = TimeSpan.FromMilliseconds(250);
    options.RetryPolicy.MaxRetries = 5;
    options.RetryPolicy.RetryInterval = TimeSpan.FromSeconds(2);
    options.RetryPolicy.UseExponentialBackoff = true;
});
```

`DataDirectory` is only used by the default file store.

## Full Multi-Provider appsettings.json

All core settings and provider settings live under the same `LScheduler` root section.

Pick one provider registration call for each application:

- file: `builder.Services.AddLScheduler();`
- SQLite: `builder.Services.AddLSchedulerSqlite();`
- MySQL: `builder.Services.AddLSchedulerMySql();`
- SQL Server: `builder.Services.AddLSchedulerSqlServer();`
- Redis: `builder.Services.AddLSchedulerRedis();`

The provider calls throw `InvalidOperationException` when their key (for example `LScheduler:Sqlite:DatabasePath`) is missing.

```json
{
  "LScheduler": {
    "DataDirectory": "data/lscheduler-file",
    "ScanInterval": "00:00:00.50",
    "RetryPolicy": {
      "MaxRetries": 5,
      "RetryInterval": "00:00:02",
      "UseExponentialBackoff": true
    },
    "ExecutionLog": {
      "Success": false,
      "Failure": true,
      "Retry": true,
      "Retention": "30.00:00:00"
    },
    "Sqlite": {
      "DatabasePath": "data/lscheduler.db"
    },
    "MySql": {
      "ConnectionString": "Server=localhost;Database=lscheduler;User Id=root;Password=test;"
    },
    "SqlServer": {
      "ConnectionString": "Server=.;Database=LScheduler;User Id=sa;Password=Password123!;TrustServerCertificate=true;"
    },
    "Redis": {
      "Configuration": "localhost:6379"
    }
  }
}
```
