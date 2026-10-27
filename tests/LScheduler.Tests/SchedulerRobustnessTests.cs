using LScheduler;

namespace LScheduler.Tests;

public sealed class SchedulerRobustnessTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_WhenScanIntervalIsNotPositive(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JobScheduler(new FakeJobStore(), TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRerunSucceededAction_WhenSavingResultFails()
    {
        var store = new FakeJobStore
        {
            OnSaveExecutionResult = _ => throw new IOException("log store down"),
        };
        var attempts = 0;
        var executor = new JobExecutor(store, TestExecutionLog.All);

        await Assert.ThrowsAsync<IOException>(() => executor.ExecuteAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            Action = _ =>
            {
                attempts++;
                return Task.CompletedTask;
            },
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 3,
                RetryInterval = TimeSpan.Zero,
            },
        }, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task AddJobAsync_RollsBackRegistration_WhenStoreSaveFails()
    {
        var store = new FakeJobStore
        {
            OnSaveJob = _ => throw new IOException("store down"),
        };
        var scheduler = new JobScheduler(store);

        await Assert.ThrowsAsync<IOException>(() => scheduler.AddJobAsync(
            "job-1",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            JobSchedulerTests.StaticPersistentJobs.NoopAsync));

        Assert.Empty(scheduler.GetRegisteredJobs());
        Assert.Empty(await scheduler.GetJobsAsync());

        store.OnSaveJob = null;

        await scheduler.AddJobAsync(
            "job-1",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            JobSchedulerTests.StaticPersistentJobs.NoopAsync);

        Assert.Single(scheduler.GetRegisteredJobs());
    }

    [Fact]
    public async Task ProcessDueJobsAsync_DoesNotRunJobAgain_WhilePreviousRunIsInProgress()
    {
        var store = new FakeJobStore();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var scheduler = new JobScheduler(store);
        var dueTime = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "slow-job",
            Interval = TimeSpan.FromSeconds(1),
            NextRunTimeUtc = dueTime,
            IsPersistent = false,
            Action = async _ =>
            {
                Interlocked.Increment(ref started);
                await gate.Task;
            },
            RetryPolicy = new RetryPolicy(),
        });

        var later = dueTime.AddSeconds(10);

        await scheduler.ProcessDueJobsAsync(dueTime, waitForJobs: false);
        await scheduler.ProcessDueJobsAsync(later, waitForJobs: false);

        Assert.Equal(1, Volatile.Read(ref started));

        gate.SetResult();
        await WaitUntilAsync(async () =>
        {
            await scheduler.ProcessDueJobsAsync(later, waitForJobs: false);
            return Volatile.Read(ref started) == 2;
        });

        Assert.Equal(2, Volatile.Read(ref started));
    }

    [Fact]
    public async Task ProcessDueJobsAsync_DoesNotResurrectJobRemovedWhileScanIsSaving()
    {
        var store = new FakeJobStore();
        var scheduler = new JobScheduler(store);
        var dueTime = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);
        var executed = new List<string>();
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        foreach (var jobId in new[] { "a", "b" })
        {
            await scheduler.AddJobAsync(new JobDefinition
            {
                JobId = jobId,
                Interval = TimeSpan.FromSeconds(5),
                NextRunTimeUtc = dueTime,
                IsPersistent = true,
                Action = _ =>
                {
                    lock (executed)
                    {
                        executed.Add(jobId);
                    }

                    return Task.CompletedTask;
                },
                RetryPolicy = new RetryPolicy(),
            });
        }

        store.OnSaveJob = async job =>
        {
            if (job.JobId == "a")
            {
                saveStarted.TrySetResult();
                await saveGate.Task;
            }
        };

        var processing = scheduler.ProcessDueJobsAsync(dueTime, waitForJobs: true);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(await scheduler.RemoveJobAsync("b"));
        saveGate.SetResult();
        await processing;

        Assert.Equal(new[] { "a" }, executed);
        Assert.DoesNotContain("b", store.SavedJobs.Keys);
        Assert.Equal("a", Assert.Single(scheduler.GetRegisteredJobs()).JobId);
    }

    [Fact]
    public async Task ExistsAsync_ReportsPersistedAndInMemoryJobs()
    {
        var scheduler = new JobScheduler(new FakeJobStore());

        await scheduler.AddJobAsync(
            "persistent",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            JobSchedulerTests.StaticPersistentJobs.NoopAsync);
        await scheduler.AddInMemoryJobAsync(
            "memory",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            _ => Task.CompletedTask);

        Assert.True(await scheduler.ExistsAsync("persistent"));
        Assert.True(await scheduler.ExistsAsync("memory"));
        Assert.False(await scheduler.ExistsAsync("missing"));

        await scheduler.RemoveJobAsync("persistent");

        Assert.False(await scheduler.ExistsAsync("persistent"));
        await Assert.ThrowsAsync<ArgumentException>(() => scheduler.ExistsAsync(" "));
    }

    [Fact]
    public async Task RemoveThenAddAsync_ReplacesRegisteredJob()
    {
        var store = new FakeJobStore();
        var scheduler = new JobScheduler(store);

        await scheduler.AddJobAsync(
            "job-1",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            JobSchedulerTests.StaticPersistentJobs.NoopAsync);

        if (await scheduler.ExistsAsync("job-1"))
        {
            await scheduler.RemoveJobAsync("job-1");
        }

        await scheduler.AddJobAsync(
            "job-1",
            JobSchedule.Every(TimeSpan.FromSeconds(30)),
            JobSchedulerTests.StaticPersistentJobs.NoopAsync);

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(store.SavedJobs.Values).Interval);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(scheduler.GetRegisteredJobs()).Interval);
    }

    [Fact]
    public async Task ReplacingRunningOneTimeJob_DoesNotDeleteTheNewRegistration()
    {
        var store = new FakeJobStore();
        var scheduler = new JobScheduler(store);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dueTime = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromSeconds(5),
            RunOnce = true,
            NextRunTimeUtc = dueTime,
            IsPersistent = true,
            Action = async _ =>
            {
                started.TrySetResult();
                await gate.Task;
            },
            RetryPolicy = new RetryPolicy(),
        });

        var processing = scheduler.ProcessDueJobsAsync(dueTime, waitForJobs: true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await scheduler.RemoveJobAsync("job-1");
        await scheduler.AddJobAsync(
            "job-1",
            JobSchedule.Delay(TimeSpan.FromHours(1)),
            JobSchedulerTests.StaticPersistentJobs.NoopAsync);

        gate.SetResult();
        await processing;

        Assert.True(await scheduler.ExistsAsync("job-1"));
        Assert.Contains("job-1", store.SavedJobs.Keys);
    }

    [Fact]
    public async Task StartAsync_KeepsRunning_WhenStartTokenIsCancelledLater()
    {
        var store = new FakeJobStore();
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));
        using var startTokenSource = new CancellationTokenSource();

        await scheduler.StartAsync(startTokenSource.Token);
        startTokenSource.Cancel();

        await scheduler.AddInMemoryJobAsync(
            "job-1",
            JobSchedule.Delay(TimeSpan.FromMilliseconds(50)),
            _ =>
            {
                completionSource.TrySetResult();
                return Task.CompletedTask;
            });

        await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task StartAsync_KeepsScanning_WhenStoreSaveFailsOnce()
    {
        var store = new FakeJobStore();
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromMilliseconds(50),
            NextRunTimeUtc = DateTime.UtcNow,
            IsPersistent = true,
            Action = _ =>
            {
                completionSource.TrySetResult();
                return Task.CompletedTask;
            },
            RetryPolicy = new RetryPolicy(),
        });

        var failures = 0;
        store.OnSaveJob = _ => Interlocked.Increment(ref failures) == 1
            ? throw new IOException("store down")
            : Task.CompletedTask;

        await scheduler.StartAsync();
        await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync();

        Assert.True(failures >= 2);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }
}
