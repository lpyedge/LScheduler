# Storage Providers

The runtime API stays the same for every provider. Only `IJobStore` changes.

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
builder.Services.AddLSchedulerSqlite(builder.Configuration);
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
builder.Services.AddLSchedulerMySql(builder.Configuration);
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
builder.Services.AddLSchedulerSqlServer(builder.Configuration);
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
builder.Services.AddLSchedulerRedis(builder.Configuration);
```

Required configuration key:

- `LScheduler:Redis:Configuration`
