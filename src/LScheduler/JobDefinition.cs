using System.Text.Json.Serialization;

namespace LScheduler;

public sealed class JobDefinition
{
    public string JobId { get; set; } = string.Empty;

    public string Cron { get; set; } = string.Empty;

    public TimeSpan? Interval { get; set; }

    public bool RunOnce { get; set; }

    [JsonIgnore]
    public Func<CancellationToken, Task>? Action { get; set; }

    public bool IsPersistent { get; set; } = true;

    public string? MethodTypeName { get; set; }

    public string? MethodName { get; set; }

    public bool MethodIsStatic { get; set; }

    public string? PayloadTypeName { get; set; }

    public string? PayloadJson { get; set; }

    public DateTime? LastRunTimeUtc { get; set; }

    public DateTime? NextRunTimeUtc { get; set; }

    public RetryPolicy RetryPolicy { get; set; } = new();

    internal JobDefinition Clone(bool includeAction)
    {
        return new JobDefinition
        {
            JobId = JobId,
            Cron = Cron,
            Interval = Interval,
            RunOnce = RunOnce,
            Action = includeAction ? Action : null,
            IsPersistent = IsPersistent,
            MethodTypeName = MethodTypeName,
            MethodName = MethodName,
            MethodIsStatic = MethodIsStatic,
            PayloadTypeName = PayloadTypeName,
            PayloadJson = PayloadJson,
            LastRunTimeUtc = LastRunTimeUtc,
            NextRunTimeUtc = NextRunTimeUtc,
            RetryPolicy = RetryPolicy.Clone(),
        };
    }
}
