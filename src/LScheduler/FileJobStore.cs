using System.Text.Json;

namespace LScheduler;

public sealed class FileJobStore : IJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

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
        _executionLogFilePath = Path.Combine(fullDirectory, "execution_log.json");
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
            var items = await ReadListAsync<JobExecutionResult>(_executionLogFilePath, cancellationToken);
            items.Add(result);
            await WriteListAsync(_executionLogFilePath, items, cancellationToken);
        }
        finally
        {
            _mutex.Release();
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
        using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken);
    }
}
