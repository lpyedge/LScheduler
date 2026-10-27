using LScheduler;

namespace LScheduler.Tests;

public sealed class ModelTests
{
    [Fact]
    public void JobDefinition_Clone_OmitsAction_WhenRequested()
    {
        var job = new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            Interval = TimeSpan.FromSeconds(5),
            RunOnce = true,
            Action = _ => Task.CompletedTask,
            IsPersistent = false,
            MethodTypeName = "Namespace.JobRunner, TestAssembly",
            MethodName = "RunAsync",
            MethodIsStatic = true,
            PayloadTypeName = "Namespace.JobPayload, TestAssembly",
            PayloadJson = "{\"message\":\"hello\"}",
            LastRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            NextRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 05, DateTimeKind.Utc),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 7,
                RetryInterval = TimeSpan.FromSeconds(4),
                UseExponentialBackoff = true,
            },
        };

        var clone = job.Clone(includeAction: false);

        Assert.Equal(job.JobId, clone.JobId);
        Assert.Equal(job.Cron, clone.Cron);
        Assert.Equal(job.Interval, clone.Interval);
        Assert.Equal(job.RunOnce, clone.RunOnce);
        Assert.Equal(job.IsPersistent, clone.IsPersistent);
        Assert.Equal(job.MethodTypeName, clone.MethodTypeName);
        Assert.Equal(job.MethodName, clone.MethodName);
        Assert.Equal(job.MethodIsStatic, clone.MethodIsStatic);
        Assert.Equal(job.PayloadTypeName, clone.PayloadTypeName);
        Assert.Equal(job.PayloadJson, clone.PayloadJson);
        Assert.Equal(job.LastRunTimeUtc, clone.LastRunTimeUtc);
        Assert.Equal(job.NextRunTimeUtc, clone.NextRunTimeUtc);
        Assert.Null(clone.Action);
        Assert.NotSame(job.RetryPolicy, clone.RetryPolicy);
        Assert.Equal(7, clone.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(4), clone.RetryPolicy.RetryInterval);
        Assert.True(clone.RetryPolicy.UseExponentialBackoff);
    }

    [Fact]
    public void RetryPolicy_Clone_CreatesDistinctCopy()
    {
        var policy = new RetryPolicy
        {
            MaxRetries = 2,
            RetryInterval = TimeSpan.FromSeconds(8),
            UseExponentialBackoff = true,
        };

        var clone = policy.Clone();

        Assert.NotSame(policy, clone);
        Assert.Equal(2, clone.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(8), clone.RetryInterval);
        Assert.True(clone.UseExponentialBackoff);
    }
}
