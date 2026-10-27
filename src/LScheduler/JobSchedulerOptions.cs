namespace LScheduler;

public sealed class JobSchedulerOptions
{
    public const string SectionName = "LScheduler";

    public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "lscheduler-data");

    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(1);

    public RetryPolicy RetryPolicy { get; set; } = new();

    public ExecutionLogOptions ExecutionLog { get; set; } = new();
}
