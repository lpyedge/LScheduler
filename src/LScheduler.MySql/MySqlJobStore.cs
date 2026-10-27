using System.Text.Json;
using LScheduler;
using MySqlConnector;

namespace LScheduler.MySql;

public sealed class MySqlJobStore : IJobStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public MySqlJobStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyCollection<JobDefinition>> LoadJobsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        var jobs = new List<JobDefinition>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT JobId, Cron, IntervalTicks, RunOnce, MethodTypeName, MethodName, MethodIsStatic, PayloadTypeName, PayloadJson, LastRunTimeUtc, NextRunTimeUtc, RetryPolicyJson
            FROM Jobs;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new JobDefinition
            {
                JobId = reader.GetString(0),
                Cron = reader.GetString(1),
                Interval = reader.IsDBNull(2) ? null : TimeSpan.FromTicks(reader.GetInt64(2)),
                RunOnce = !reader.IsDBNull(3) && reader.GetBoolean(3),
                MethodTypeName = reader.IsDBNull(4) ? null : reader.GetString(4),
                MethodName = reader.IsDBNull(5) ? null : reader.GetString(5),
                MethodIsStatic = !reader.IsDBNull(6) && reader.GetBoolean(6),
                PayloadTypeName = reader.IsDBNull(7) ? null : reader.GetString(7),
                PayloadJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastRunTimeUtc = reader.IsDBNull(9) ? null : DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc),
                NextRunTimeUtc = reader.IsDBNull(10) ? null : DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc),
                RetryPolicy = DeserializeRetryPolicy(reader.GetString(11)),
            });
        }

        return jobs;
    }

    public async Task SaveJobAsync(JobDefinition job, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(job, nameof(job));
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Jobs (JobId, Cron, IntervalTicks, RunOnce, MethodTypeName, MethodName, MethodIsStatic, PayloadTypeName, PayloadJson, LastRunTimeUtc, NextRunTimeUtc, RetryPolicyJson)
            VALUES (@jobId, @cron, @intervalTicks, @runOnce, @methodTypeName, @methodName, @methodIsStatic, @payloadTypeName, @payloadJson, @lastRunTimeUtc, @nextRunTimeUtc, @retryPolicyJson)
            ON DUPLICATE KEY UPDATE
                Cron = VALUES(Cron),
                IntervalTicks = VALUES(IntervalTicks),
                RunOnce = VALUES(RunOnce),
                MethodTypeName = VALUES(MethodTypeName),
                MethodName = VALUES(MethodName),
                MethodIsStatic = VALUES(MethodIsStatic),
                PayloadTypeName = VALUES(PayloadTypeName),
                PayloadJson = VALUES(PayloadJson),
                LastRunTimeUtc = VALUES(LastRunTimeUtc),
                NextRunTimeUtc = VALUES(NextRunTimeUtc),
                RetryPolicyJson = VALUES(RetryPolicyJson);
            """;

        command.Parameters.AddWithValue("@jobId", job.JobId);
        command.Parameters.AddWithValue("@cron", job.Cron);
        command.Parameters.AddWithValue("@intervalTicks", (object?)job.Interval?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("@runOnce", job.RunOnce);
        command.Parameters.AddWithValue("@methodTypeName", (object?)job.MethodTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("@methodName", (object?)job.MethodName ?? DBNull.Value);
        command.Parameters.AddWithValue("@methodIsStatic", job.MethodIsStatic);
        command.Parameters.AddWithValue("@payloadTypeName", (object?)job.PayloadTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("@payloadJson", (object?)job.PayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastRunTimeUtc", (object?)job.LastRunTimeUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("@nextRunTimeUtc", (object?)job.NextRunTimeUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("@retryPolicyJson", JsonSerializer.Serialize(job.RetryPolicy));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Jobs WHERE JobId = @jobId;";
        command.Parameters.AddWithValue("@jobId", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveExecutionResultAsync(JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(result, nameof(result));
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExecutionLog (JobId, StartTimeUtc, EndTimeUtc, Success, Exception, RetryCount)
            VALUES (@jobId, @startTimeUtc, @endTimeUtc, @success, @exception, @retryCount);
            """;

        command.Parameters.AddWithValue("@jobId", result.JobId);
        command.Parameters.AddWithValue("@startTimeUtc", result.StartTimeUtc);
        command.Parameters.AddWithValue("@endTimeUtc", result.EndTimeUtc);
        command.Parameters.AddWithValue("@success", result.Success);
        command.Parameters.AddWithValue("@exception", (object?)result.Exception ?? DBNull.Value);
        command.Parameters.AddWithValue("@retryCount", result.RetryCount);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PurgeExecutionResultsAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ExecutionLog WHERE EndTimeUtc < @olderThanUtc;";
        command.Parameters.AddWithValue("@olderThanUtc", olderThanUtc);
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

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Jobs (
                    JobId VARCHAR(200) NOT NULL PRIMARY KEY,
                    Cron TEXT NOT NULL,
                    IntervalTicks BIGINT NULL,
                    RunOnce TINYINT(1) NOT NULL,
                    MethodTypeName VARCHAR(1000) NULL,
                    MethodName VARCHAR(500) NULL,
                    MethodIsStatic TINYINT(1) NOT NULL,
                    PayloadTypeName VARCHAR(1000) NULL,
                    PayloadJson LONGTEXT NULL,
                    LastRunTimeUtc DATETIME(6) NULL,
                    NextRunTimeUtc DATETIME(6) NULL,
                    RetryPolicyJson LONGTEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ExecutionLog (
                    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    JobId VARCHAR(200) NOT NULL,
                    StartTimeUtc DATETIME(6) NOT NULL,
                    EndTimeUtc DATETIME(6) NOT NULL,
                    Success TINYINT(1) NOT NULL,
                    Exception LONGTEXT NULL,
                    RetryCount INT NOT NULL
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

    private static RetryPolicy DeserializeRetryPolicy(string json)
    {
        return JsonSerializer.Deserialize<RetryPolicy>(json) ?? new RetryPolicy();
    }
}
