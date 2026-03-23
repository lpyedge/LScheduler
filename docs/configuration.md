# Configuration

`LScheduler` supports three core registration styles:

- zero configuration with the default `FileJobStore`
- `appsettings.json` / `IConfiguration`
- manual `JobSchedulerOptions`

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
builder.Services.AddLScheduler(builder.Configuration);
```

You can also bind from the section directly:

```csharp
builder.Services.AddLScheduler(
    builder.Configuration.GetSection(JobSchedulerOptions.SectionName));
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

## Full Multi-Provider appsettings.json

All core settings and provider settings live under the same `LScheduler` root section.

Pick one provider registration call for each application:

- file: `builder.Services.AddLScheduler(builder.Configuration);`
- SQLite: `builder.Services.AddLSchedulerSqlite(builder.Configuration);`
- MySQL: `builder.Services.AddLSchedulerMySql(builder.Configuration);`
- SQL Server: `builder.Services.AddLSchedulerSqlServer(builder.Configuration);`
- Redis: `builder.Services.AddLSchedulerRedis(builder.Configuration);`

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
