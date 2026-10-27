namespace LScheduler;

public sealed class ExecutionLogOptions
{
    /// <summary>Log attempts that succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Log attempts that failed with no retry left.</summary>
    public bool Failure { get; set; } = true;

    /// <summary>Log failed attempts that will be retried.</summary>
    public bool Retry { get; set; } = true;

    /// <summary>Execution results older than this are purged. <see cref="TimeSpan.Zero"/> keeps them forever.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);
}
