# Storage Providers

The runtime API stays the same for every provider. Only `IJobStore` changes.

Every provider can be registered these ways:

- `AddLSchedulerXxx()` reads the provider setting from the application configuration automatically
- `AddLSchedulerXxx(configuration)` reads it from the `IConfiguration` you pass
- `AddLSchedulerXxx(value)` takes the setting directly
- `AddLSchedulerXxx(value, options => ...)` and `AddLSchedulerXxx(value, configuration)` take the setting directly and also configure the core options

The parameterless form throws `InvalidOperationException` when the provider key is missing.
Core settings under the `LScheduler` section are always read automatically, see [Configuration](configuration.md).

## File

Included in `LScheduler`.

```csharp
builder.Services.AddLScheduler();
```

## SQLite

Package:

- `LScheduler.Sqlite`

Registration:

```csharp
using LScheduler.Sqlite;

builder.Services.AddLSchedulerSqlite("data/lscheduler.db");
```

Configuration-based registration:

```csharp
builder.Services.AddLSchedulerSqlite();                      // reads LScheduler:Sqlite:DatabasePath
builder.Services.AddLSchedulerSqlite(builder.Configuration); // same key, explicit IConfiguration
```

Required configuration key:

- `LScheduler:Sqlite:DatabasePath`

## MySQL

Package:

- `LScheduler.MySql`

Registration:

```csharp
using LScheduler.MySql;

builder.Services.AddLSchedulerMySql(
    "Server=localhost;Database=lscheduler;User Id=root;Password=test;");
```

Configuration-based registration:

```csharp
builder.Services.AddLSchedulerMySql();                      // reads LScheduler:MySql:ConnectionString
builder.Services.AddLSchedulerMySql(builder.Configuration); // same key, explicit IConfiguration
```

Required configuration key:

- `LScheduler:MySql:ConnectionString`

## SQL Server

Package:

- `LScheduler.SqlServer`

Registration:

```csharp
using LScheduler.SqlServer;

builder.Services.AddLSchedulerSqlServer(
    "Server=.;Database=LScheduler;User Id=sa;Password=Password123!;TrustServerCertificate=true;");
```

Configuration-based registration:

```csharp
builder.Services.AddLSchedulerSqlServer();                      // reads LScheduler:SqlServer:ConnectionString
builder.Services.AddLSchedulerSqlServer(builder.Configuration); // same key, explicit IConfiguration
```

Required configuration key:

- `LScheduler:SqlServer:ConnectionString`

## Redis

Package:

- `LScheduler.Redis`

Registration:

```csharp
using LScheduler.Redis;

builder.Services.AddLSchedulerRedis("localhost:6379");
```

Configuration-based registration:

```csharp
builder.Services.AddLSchedulerRedis();                      // reads LScheduler:Redis:Configuration
builder.Services.AddLSchedulerRedis(builder.Configuration); // same key, explicit IConfiguration
```

Required configuration key:

- `LScheduler:Redis:Configuration`
