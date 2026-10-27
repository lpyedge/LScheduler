using System.Globalization;
using System.Text.Json;
using LScheduler;
using Microsoft.Data.Sqlite;

namespace LScheduler.Sqlite;

public sealed class SqliteJobStore : IJobStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public SqliteJobStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
        }.ToString();
    }

    public async Task<IReadOnlyCollection<JobDefinition>> LoadJobsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        var jobs = new List<JobDefinition>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT JobId, Cron, IntervalTicks, RunOnce, MethodTypeName, MethodName, MethodIsStatic, PayloadTypeName, PayloadJson, LastRunTimeUtc, NextRunTimeUtc, RetryPolicyJson
            FROM Jobs;
            """;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new JobDefinition
            {
                JobId = reader.GetString(0),
                Cron = reader.GetString(1),
                Interval = reader.IsDBNull(2) ? null : TimeSpan.FromTicks(reader.GetInt64(2)),
                RunOnce = !reader.IsDBNull(3) && reader.GetInt64(3) == 1,
                MethodTypeName = reader.IsDBNull(4) ? null : reader.GetString(4),
                MethodName = reader.IsDBNull(5) ? null : reader.GetString(5),
                MethodIsStatic = !reader.IsDBNull(6) && reader.GetInt64(6) == 1,
                PayloadTypeName = reader.IsDBNull(7) ? null : reader.GetString(7),
                PayloadJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastRunTimeUtc = reader.IsDBNull(9) ? null : ParseUtc(reader.GetString(9)),
                NextRunTimeUtc = reader.IsDBNull(10) ? null : ParseUtc(reader.GetString(10)),
                RetryPolicy = DeserializeRetryPolicy(reader.GetString(11)),
            });
        }

        return jobs;
    }

    public async Task SaveJobAsync(JobDefinition job, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(job, nameof(job));
        await EnsureCreatedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Jobs (JobId, Cron, IntervalTicks, RunOnce, MethodTypeName, MethodName, MethodIsStatic, PayloadTypeName, PayloadJson, LastRunTimeUtc, NextRunTimeUtc, RetryPolicyJson)
            VALUES ($jobId, $cron, $intervalTicks, $runOnce, $methodTypeName, $methodName, $methodIsStatic, $payloadTypeName, $payloadJson, $lastRunTimeUtc, $nextRunTimeUtc, $retryPolicyJson)
            ON CONFLICT(JobId) DO UPDATE SET
                Cron = excluded.Cron,
                IntervalTicks = excluded.IntervalTicks,
                RunOnce = excluded.RunOnce,
                MethodTypeName = excluded.MethodTypeName,
                MethodName = excluded.MethodName,
                MethodIsStatic = excluded.MethodIsStatic,
                PayloadTypeName = excluded.PayloadTypeName,
                PayloadJson = excluded.PayloadJson,
                LastRunTimeUtc = excluded.LastRunTimeUtc,
                NextRunTimeUtc = excluded.NextRunTimeUtc,
                RetryPolicyJson = excluded.RetryPolicyJson;
            """;

        command.Parameters.AddWithValue("$jobId", job.JobId);
        command.Parameters.AddWithValue("$cron", job.Cron);
        command.Parameters.AddWithValue("$intervalTicks", job.Interval.HasValue ? job.Interval.Value.Ticks : DBNull.Value);
        command.Parameters.AddWithValue("$runOnce", job.RunOnce ? 1 : 0);
        command.Parameters.AddWithValue("$methodTypeName", (object?)job.MethodTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("$methodName", (object?)job.MethodName ?? DBNull.Value);
        command.Parameters.AddWithValue("$methodIsStatic", job.MethodIsStatic ? 1 : 0);
        command.Parameters.AddWithValue("$payloadTypeName", (object?)job.PayloadTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("$payloadJson", (object?)job.PayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastRunTimeUtc", ToDbValue(job.LastRunTimeUtc));
        command.Parameters.AddWithValue("$nextRunTimeUtc", ToDbValue(job.NextRunTimeUtc));
        command.Parameters.AddWithValue("$retryPolicyJson", JsonSerializer.Serialize(job.RetryPolicy));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        await EnsureCreatedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Jobs WHERE JobId = $jobId;";
        command.Parameters.AddWithValue("$jobId", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveExecutionResultAsync(JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(result, nameof(result));
        await EnsureCreatedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExecutionLog (JobId, StartTimeUtc, EndTimeUtc, Success, Exception, RetryCount)
            VALUES ($jobId, $startTimeUtc, $endTimeUtc, $success, $exception, $retryCount);
            """;

        command.Parameters.AddWithValue("$jobId", result.JobId);
        command.Parameters.AddWithValue("$startTimeUtc", result.StartTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$endTimeUtc", result.EndTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$success", result.Success ? 1 : 0);
        command.Parameters.AddWithValue("$exception", (object?)result.Exception ?? DBNull.Value);
        command.Parameters.AddWithValue("$retryCount", result.RetryCount);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PurgeExecutionResultsAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ExecutionLog WHERE EndTimeUtc < $olderThanUtc;";
        command.Parameters.AddWithValue("$olderThanUtc", olderThanUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Jobs (
                    JobId TEXT PRIMARY KEY,
                    Cron TEXT NOT NULL,
                    IntervalTicks INTEGER NULL,
                    RunOnce INTEGER NOT NULL,
                    MethodTypeName TEXT NULL,
                    MethodName TEXT NULL,
                    MethodIsStatic INTEGER NOT NULL,
                    PayloadTypeName TEXT NULL,
                    PayloadJson TEXT NULL,
                    LastRunTimeUtc TEXT NULL,
                    NextRunTimeUtc TEXT NULL,
                    RetryPolicyJson TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ExecutionLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobId TEXT NOT NULL,
                    StartTimeUtc TEXT NOT NULL,
                    EndTimeUtc TEXT NOT NULL,
                    Success INTEGER NOT NULL,
                    Exception TEXT NULL,
                    RetryCount INTEGER NOT NULL
                );
                """;

            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static DateTime ParseUtc(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object ToDbValue(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value;
    }

    private static RetryPolicy DeserializeRetryPolicy(string json)
    {
        return JsonSerializer.Deserialize<RetryPolicy>(json) ?? new RetryPolicy();
    }
}
