namespace LScheduler;

public sealed class JobExecutionResult
{
    public string JobId { get; set; } = string.Empty;

    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    public bool Success { get; set; }

    public string? Exception { get; set; }

    public int RetryCount { get; set; }
}
