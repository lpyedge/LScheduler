using System.Text.Json;
using LScheduler;
using Microsoft.Data.SqlClient;

namespace LScheduler.SqlServer;

public sealed class SqlServerJobStore : IJobStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public SqlServerJobStore(string connectionString)
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

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT JobId, Cron, IntervalTicks, RunOnce, MethodTypeName, MethodName, MethodIsStatic, PayloadTypeName, PayloadJson, LastRunTimeUtc, NextRunTimeUtc, RetryPolicyJson
            FROM dbo.Jobs;
            """;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

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

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM dbo.Jobs WHERE JobId = @jobId)
            BEGIN
                UPDATE dbo.Jobs
                SET
                    Cron = @cron,
                    IntervalTicks = @intervalTicks,
                    RunOnce = @runOnce,
                    MethodTypeName = @methodTypeName,
                    MethodName = @methodName,
                    MethodIsStatic = @methodIsStatic,
                    PayloadTypeName = @payloadTypeName,
                    PayloadJson = @payloadJson,
                    LastRunTimeUtc = @lastRunTimeUtc,
                    NextRunTimeUtc = @nextRunTimeUtc,
                    RetryPolicyJson = @retryPolicyJson
                WHERE JobId = @jobId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.Jobs (JobId, Cron, IntervalTicks, RunOnce, MethodTypeName, MethodName, MethodIsStatic, PayloadTypeName, PayloadJson, LastRunTimeUtc, NextRunTimeUtc, RetryPolicyJson)
                VALUES (@jobId, @cron, @intervalTicks, @runOnce, @methodTypeName, @methodName, @methodIsStatic, @payloadTypeName, @payloadJson, @lastRunTimeUtc, @nextRunTimeUtc, @retryPolicyJson);
            END
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

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.Jobs WHERE JobId = @jobId;";
        command.Parameters.AddWithValue("@jobId", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveExecutionResultAsync(JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(result, nameof(result));
        await EnsureCreatedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.ExecutionLog (JobId, StartTimeUtc, EndTimeUtc, Success, Exception, RetryCount)
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

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = """
                IF OBJECT_ID(N'dbo.Jobs', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Jobs (
                        JobId NVARCHAR(200) NOT NULL PRIMARY KEY,
                        Cron NVARCHAR(MAX) NOT NULL,
                        IntervalTicks BIGINT NULL,
                        RunOnce BIT NOT NULL,
                        MethodTypeName NVARCHAR(1000) NULL,
                        MethodName NVARCHAR(500) NULL,
                        MethodIsStatic BIT NOT NULL,
                        PayloadTypeName NVARCHAR(1000) NULL,
                        PayloadJson NVARCHAR(MAX) NULL,
                        LastRunTimeUtc DATETIME2 NULL,
                        NextRunTimeUtc DATETIME2 NULL,
                        RetryPolicyJson NVARCHAR(MAX) NOT NULL
                    );
                END;

                IF OBJECT_ID(N'dbo.ExecutionLog', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ExecutionLog (
                        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        JobId NVARCHAR(200) NOT NULL,
                        StartTimeUtc DATETIME2 NOT NULL,
                        EndTimeUtc DATETIME2 NOT NULL,
                        Success BIT NOT NULL,
                        Exception NVARCHAR(MAX) NULL,
                        RetryCount INT NOT NULL
                    );
                END;
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
