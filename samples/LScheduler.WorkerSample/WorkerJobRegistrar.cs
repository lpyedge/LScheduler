using LScheduler;

namespace LScheduler.WorkerSample;

public sealed class WorkerJobRegistrar
{
    private readonly JobScheduler _scheduler;
    private readonly DailyDigestJobs _dailyDigestJobs;

    public WorkerJobRegistrar(JobScheduler scheduler, DailyDigestJobs dailyDigestJobs)
    {
        _scheduler = scheduler;
        _dailyDigestJobs = dailyDigestJobs;
    }

    public async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var jobs = await _scheduler.GetJobsAsync(cancellationToken);

        if (jobs.All(job => job.JobId != "heartbeat-job"))
        {
            await _scheduler.AddInMemoryJobAsync(
                "heartbeat-job",
                JobSchedule.Every(TimeSpan.FromSeconds(30)),
                _ =>
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] heartbeat");
                    return Task.CompletedTask;
                },
                cancellationToken: cancellationToken);
        }

        if (jobs.All(job => job.JobId != "daily-digest-job"))
        {
            await _scheduler.AddJobAsync(
                "daily-digest-job",
                JobSchedule.DailyAt(8, 30),
                _dailyDigestJobs.SendDailyDigestAsync,
                new DailyDigestJobArgs("ops@example.com", "daily-digest"),
                cancellationToken: cancellationToken);
        }
    }
}

public sealed record DailyDigestJobArgs(string RecipientEmail, string TemplateName);

public sealed class DailyDigestJobs
{
    public Task SendDailyDigestAsync(DailyDigestJobArgs payload, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[{DateTime.UtcNow:O}] sending {payload.TemplateName} to {payload.RecipientEmail}");
        return Task.CompletedTask;
    }
}
