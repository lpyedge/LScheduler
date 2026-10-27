using System.Text;
using System.Text.Json;

namespace LScheduler;

public sealed class FileJobStore : IJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly string _jobsFilePath;
    private readonly string _executionLogFilePath;

    public FileJobStore(string? directoryPath = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(directoryPath)
            ? Directory.GetCurrentDirectory()
            : directoryPath;

        var fullDirectory = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(fullDirectory);

        _jobsFilePath = Path.Combine(fullDirectory, "jobs.json");
        _executionLogFilePath = Path.Combine(fullDirectory, "execution_log.jsonl");
    }

    public async Task<IReadOnlyCollection<JobDefinition>> LoadJobsAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            return await ReadListAsync<JobDefinition>(_jobsFilePath, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveJobAsync(JobDefinition job, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(job, nameof(job));

        await _mutex.WaitAsync(cancellationToken);

        try
        {
            var jobs = await ReadListAsync<JobDefinition>(_jobsFilePath, cancellationToken);
            var existingIndex = jobs.FindIndex(item => string.Equals(item.JobId, job.JobId, StringComparison.Ordinal));
            var persistedJob = job.Clone(includeAction: false);

            if (existingIndex >= 0)
            {
                jobs[existingIndex] = persistedJob;
            }
            else
            {
                jobs.Add(persistedJob);
            }

            await WriteListAsync(_jobsFilePath, jobs, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        await _mutex.WaitAsync(cancellationToken);

        try
        {
            var jobs = await ReadListAsync<JobDefinition>(_jobsFilePath, cancellationToken);
            jobs.RemoveAll(item => string.Equals(item.JobId, jobId, StringComparison.Ordinal));
            await WriteListAsync(_jobsFilePath, jobs, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveExecutionResultAsync(JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(result, nameof(result));

        await _mutex.WaitAsync(cancellationToken);

        try
        {
            using var stream = new FileStream(_executionLogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Utf8WithoutBom);
            await writer.WriteAsync(JsonSerializer.Serialize(result) + "\n");
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task PurgeExecutionResultsAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_executionLogFilePath))
            {
                return;
            }

            var keptLines = new List<string>();
            var hasRemovedLines = false;

            using (var reader = new StreamReader(_executionLogFilePath, Utf8WithoutBom))
            {
                string? line;

                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    if (IsExpired(line, olderThanUtc))
                    {
                        hasRemovedLines = true;
                    }
                    else
                    {
                        keptLines.Add(line);
                    }
                }
            }

            if (!hasRemovedLines)
            {
                return;
            }

            var tempFilePath = _executionLogFilePath + ".tmp";
            File.WriteAllText(tempFilePath, keptLines.Count == 0 ? string.Empty : string.Join("\n", keptLines) + "\n", Utf8WithoutBom);
            ReplaceFile(tempFilePath, _executionLogFilePath);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static bool IsExpired(string line, DateTime olderThanUtc)
    {
        try
        {
            var result = JsonSerializer.Deserialize<JobExecutionResult>(line);
            return result is null || result.EndTimeUtc < olderThanUtc;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static async Task<List<T>> ReadListAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new List<T>();
        }

        using var stream = File.OpenRead(filePath);
        var items = await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken);
        return items ?? new List<T>();
    }

    private static async Task WriteListAsync<T>(string filePath, List<T> items, CancellationToken cancellationToken)
    {
        var tempFilePath = filePath + ".tmp";

        using (var stream = File.Create(tempFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken);
        }

        ReplaceFile(tempFilePath, filePath);
    }

    private static void ReplaceFile(string tempFilePath, string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Replace(tempFilePath, filePath, null);
        }
        else
        {
            File.Move(tempFilePath, filePath);
        }
    }
}
