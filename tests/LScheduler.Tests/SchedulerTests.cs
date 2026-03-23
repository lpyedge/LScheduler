using LScheduler;
using Microsoft.Extensions.DependencyInjection;

namespace LScheduler.Tests;

public sealed class JobSchedulerTests
{
    [Fact]
    public async Task AddJobAsync_MergesPersistedScheduleMetadata()
    {
        var store = new FakeJobStore();
        var persistedNextRunUtc = new DateTime(2026, 03, 23, 10, 00, 05, DateTimeKind.Utc);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            LastRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            NextRunTimeUtc = persistedNextRunUtc,
            RetryPolicy = new RetryPolicy(),
        });

        var scheduler = new JobScheduler(store);

        await scheduler.AddJobAsync("job-1", "*/5 * * * * *", StaticPersistentJobs.NoopAsync);

        var activeJob = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.Equal(persistedNextRunUtc, activeJob.NextRunTimeUtc);
        Assert.NotNull(activeJob.Action);
    }

    [Fact]
    public async Task AddJobAsync_Throws_WhenJobIdAlreadyRegistered()
    {
        var scheduler = new JobScheduler(new FakeJobStore());

        await scheduler.AddJobAsync("job-1", JobSchedule.Every(TimeSpan.FromSeconds(5)), StaticPersistentJobs.NoopAsync);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scheduler.AddJobAsync("job-1", JobSchedule.Every(TimeSpan.FromSeconds(10)), StaticPersistentJobs.NoopAsync));

        Assert.Equal("Job 'job-1' is already registered.", exception.Message);
    }

    [Fact]
    public async Task AddJobAsync_UsesGlobalRetryPolicy_WhenJobPolicyIsMissing()
    {
        var scheduler = new JobScheduler(
            new FakeJobStore(),
            retryPolicy: new RetryPolicy
            {
                MaxRetries = 9,
                RetryInterval = TimeSpan.FromSeconds(4),
                UseExponentialBackoff = true,
            });

        await scheduler.AddJobAsync("job-1", JobSchedule.Every(TimeSpan.FromSeconds(5)), StaticPersistentJobs.NoopAsync);

        var activeJob = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.Equal(TimeSpan.FromSeconds(5), activeJob.Interval);
        Assert.Equal(9, activeJob.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(4), activeJob.RetryPolicy.RetryInterval);
        Assert.True(activeJob.RetryPolicy.UseExponentialBackoff);
    }

    [Fact]
    public async Task AddJobAsync_UsesPerJobRetryPolicyOverride()
    {
        var scheduler = new JobScheduler(
            new FakeJobStore(),
            retryPolicy: new RetryPolicy
            {
                MaxRetries = 9,
                RetryInterval = TimeSpan.FromSeconds(4),
            });

        await scheduler.AddJobAsync(
            "job-1",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            StaticPersistentJobs.NoopAsync,
            new RetryPolicy
            {
                MaxRetries = 1,
                RetryInterval = TimeSpan.Zero,
            });

        var activeJob = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.Equal(1, activeJob.RetryPolicy.MaxRetries);
        Assert.Equal(TimeSpan.Zero, activeJob.RetryPolicy.RetryInterval);
    }

    [Fact]
    public async Task AddJobAsync_PersistsMethodMetadataAndPayload()
    {
        using var serviceProvider = new ServiceCollection()
            .AddSingleton<PersistentExecutionSink>()
            .AddTransient<PersistentServiceJobTarget>()
            .BuildServiceProvider();
        var store = new FakeJobStore();
        var scheduler = new JobScheduler(store, serviceProvider: serviceProvider);
        var target = serviceProvider.GetRequiredService<PersistentServiceJobTarget>();

        await scheduler.AddJobAsync(
            "job-1",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            target.RunWithPayloadAsync,
            new PersistentPayload("hello"));

        var activeJob = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.Equal(typeof(PersistentServiceJobTarget).AssemblyQualifiedName, activeJob.MethodTypeName);
        Assert.Equal(nameof(PersistentServiceJobTarget.RunWithPayloadAsync), activeJob.MethodName);
        Assert.False(activeJob.MethodIsStatic);
        Assert.Equal(typeof(PersistentPayload).AssemblyQualifiedName, activeJob.PayloadTypeName);
        Assert.Contains("hello", activeJob.PayloadJson, StringComparison.Ordinal);
        Assert.NotNull(activeJob.Action);

        var savedJob = Assert.Single(store.SavedJobs.Values);
        Assert.Equal(activeJob.MethodTypeName, savedJob.MethodTypeName);
        Assert.Equal(activeJob.MethodName, savedJob.MethodName);
        Assert.Equal(activeJob.PayloadTypeName, savedJob.PayloadTypeName);
        Assert.Equal(activeJob.PayloadJson, savedJob.PayloadJson);
    }

    [Fact]
    public async Task AddJobAsync_RecalculatesNextRun_WhenPersistedScheduleChanges()
    {
        var store = new FakeJobStore();
        var persistedNextRunUtc = new DateTime(2026, 03, 23, 10, 00, 05, DateTimeKind.Utc);
        var persistedLastRunUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            LastRunTimeUtc = persistedLastRunUtc,
            NextRunTimeUtc = persistedNextRunUtc,
            RetryPolicy = new RetryPolicy(),
        });

        var beforeRegistrationUtc = DateTime.UtcNow;
        var scheduler = new JobScheduler(store);

        await scheduler.AddJobAsync("job-1", JobSchedule.Every(TimeSpan.FromMinutes(1)), StaticPersistentJobs.NoopAsync);

        var activeJob = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.Equal(persistedLastRunUtc, activeJob.LastRunTimeUtc);
        Assert.NotEqual(persistedNextRunUtc, activeJob.NextRunTimeUtc);
        Assert.InRange(
            activeJob.NextRunTimeUtc!.Value,
            beforeRegistrationUtc.AddSeconds(55),
            beforeRegistrationUtc.AddMinutes(1).AddSeconds(5));
    }

    [Fact]
    public async Task AddJobAsync_RejectsLambdaClosuresForPersistentJobs()
    {
        var scheduler = new JobScheduler(new FakeJobStore());
        var capturedValue = "hello";

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.AddJobAsync(
                "job-1",
                JobSchedule.Every(TimeSpan.FromSeconds(5)),
                cancellationToken =>
                {
                    _ = capturedValue;
                    return Task.CompletedTask;
                }));

        Assert.Contains("AddInMemoryJobAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddInMemoryJobAsync_DoesNotPersistJobDefinition()
    {
        var store = new FakeJobStore();
        var scheduler = new JobScheduler(store);

        await scheduler.AddInMemoryJobAsync(
            "job-1",
            JobSchedule.Every(TimeSpan.FromSeconds(5)),
            _ => Task.CompletedTask);

        var activeJob = Assert.Single(scheduler.GetRegisteredJobs());
        Assert.False(activeJob.IsPersistent);
        Assert.Empty(store.SavedJobs);
    }

    [Fact]
    public async Task GetJobsAsync_ReturnsPersistedAndInMemoryMetadataWithoutAction()
    {
        var store = new FakeJobStore();

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "persisted-job",
            Cron = "0 0 12 * * *",
            NextRunTimeUtc = new DateTime(2026, 03, 24, 12, 00, 00, DateTimeKind.Utc),
            RetryPolicy = new RetryPolicy(),
        });

        var scheduler = new JobScheduler(store);
        await scheduler.AddInMemoryJobAsync("runtime-job", JobSchedule.Every(TimeSpan.FromMinutes(5)), _ => Task.CompletedTask);

        var jobs = await scheduler.GetJobsAsync();

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, job => Assert.Null(job.Action));
        Assert.Contains(jobs, job => job.JobId == "persisted-job");
        Assert.Contains(jobs, job => job.JobId == "runtime-job");
    }

    [Fact]
    public async Task RemoveJobAsync_DeletesPersistedJobAndRemovesInMemoryJob()
    {
        var store = new FakeJobStore();
        var scheduler = new JobScheduler(store);

        await scheduler.AddJobAsync("persistent-job", JobSchedule.Every(TimeSpan.FromSeconds(5)), StaticPersistentJobs.NoopAsync);
        await scheduler.AddInMemoryJobAsync("in-memory-job", JobSchedule.Every(TimeSpan.FromSeconds(5)), _ => Task.CompletedTask);

        var removedPersisted = await scheduler.RemoveJobAsync("persistent-job");
        var removedInMemory = await scheduler.RemoveJobAsync("in-memory-job");
        var removedMissing = await scheduler.RemoveJobAsync("missing-job");

        Assert.True(removedPersisted);
        Assert.True(removedInMemory);
        Assert.False(removedMissing);
        Assert.Empty(await scheduler.GetJobsAsync());
        Assert.Empty(store.SavedJobs);
    }

    [Fact]
    public async Task ProcessDueJobsAsync_ExecutesRecurringPersistentIntervalJobAndPersistsNextRun()
    {
        var store = new FakeJobStore();
        var executed = false;
        var scheduler = new JobScheduler(store);
        var dueTime = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromSeconds(5),
            NextRunTimeUtc = dueTime,
            IsPersistent = true,
            Action = _ =>
            {
                executed = true;
                return Task.CompletedTask;
            },
            RetryPolicy = new RetryPolicy(),
        });

        await scheduler.ProcessDueJobsAsync(dueTime, waitForJobs: true);

        Assert.True(executed);
        var savedJob = Assert.Single(store.SavedJobs.Values);
        Assert.Equal(dueTime.AddSeconds(5), savedJob.NextRunTimeUtc);

        var execution = Assert.Single(store.ExecutionResults);
        Assert.True(execution.Success);
        Assert.Equal(0, execution.RetryCount);
    }

    [Fact]
    public async Task ProcessDueJobsAsync_RemovesPersistentOneTimeDelayJobAfterExecution()
    {
        var store = new FakeJobStore();
        var executed = false;
        var scheduler = new JobScheduler(store);
        var dueTime = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc);

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromSeconds(5),
            RunOnce = true,
            NextRunTimeUtc = dueTime,
            IsPersistent = true,
            Action = _ =>
            {
                executed = true;
                return Task.CompletedTask;
            },
            RetryPolicy = new RetryPolicy(),
        });

        await scheduler.ProcessDueJobsAsync(dueTime, waitForJobs: true);

        Assert.True(executed);
        Assert.Empty(store.SavedJobs);
        Assert.Empty(await scheduler.GetJobsAsync());
        Assert.Empty(scheduler.GetRegisteredJobs());
        Assert.Single(store.ExecutionResults);
    }

    [Fact]
    public async Task ProcessDueJobsAsync_RetriesUntilSuccess()
    {
        var store = new FakeJobStore();
        var attempts = 0;
        var scheduler = new JobScheduler(store);

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Cron = "*/5 * * * * *",
            NextRunTimeUtc = new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            IsPersistent = true,
            Action = _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("boom");
                }

                return Task.CompletedTask;
            },
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 2,
                RetryInterval = TimeSpan.Zero,
            },
        });

        await scheduler.ProcessDueJobsAsync(
            new DateTime(2026, 03, 23, 10, 00, 00, DateTimeKind.Utc),
            waitForJobs: true);

        Assert.Equal(3, attempts);
        Assert.Equal(3, store.ExecutionResults.Count);
        Assert.False(store.ExecutionResults[0].Success);
        Assert.False(store.ExecutionResults[1].Success);
        Assert.True(store.ExecutionResults[2].Success);
        Assert.Equal(0, store.ExecutionResults[0].RetryCount);
        Assert.Equal(1, store.ExecutionResults[1].RetryCount);
        Assert.Equal(2, store.ExecutionResults[2].RetryCount);
    }

    [Fact]
    public async Task StartAsync_ProcessesJobsOnBackgroundLoop()
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

        await scheduler.StartAsync();
        await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync();

        Assert.Single(store.ExecutionResults);
        Assert.True(store.ExecutionResults[0].Success);
    }

    [Fact]
    public async Task StartAsync_IsIdempotent_WhenCalledTwice()
    {
        var store = new FakeJobStore();
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromMilliseconds(50),
            RunOnce = true,
            NextRunTimeUtc = DateTime.UtcNow,
            IsPersistent = true,
            Action = _ =>
            {
                completionSource.TrySetResult();
                return Task.CompletedTask;
            },
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 0,
            },
        });

        await scheduler.StartAsync();
        await scheduler.StartAsync();
        await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync();

        Assert.Single(store.ExecutionResults);
        Assert.True(store.ExecutionResults[0].Success);
    }

    [Fact]
    public async Task StartAsync_CanRecoverAfterPreCancelledStartToken()
    {
        var store = new FakeJobStore();
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));
        using var cancellationTokenSource = new CancellationTokenSource();

        await scheduler.AddJobAsync(new JobDefinition
        {
            JobId = "job-1",
            Interval = TimeSpan.FromMilliseconds(50),
            RunOnce = true,
            NextRunTimeUtc = DateTime.UtcNow,
            IsPersistent = true,
            Action = _ =>
            {
                completionSource.TrySetResult();
                return Task.CompletedTask;
            },
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 0,
            },
        });

        cancellationTokenSource.Cancel();

        await scheduler.StartAsync(cancellationTokenSource.Token);
        await scheduler.StopAsync();

        await scheduler.StartAsync();
        await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync();

        Assert.Single(store.ExecutionResults);
        Assert.True(store.ExecutionResults[0].Success);
    }

    [Fact]
    public async Task StartAsync_RestoresPersistentJobWithPayloadAfterRestart()
    {
        var store = new FakeJobStore();

        using (var firstProvider = new ServiceCollection()
            .AddSingleton<PersistentExecutionSink>()
            .AddTransient<PersistentServiceJobTarget>()
            .BuildServiceProvider())
        {
            var scheduler = new JobScheduler(store, serviceProvider: firstProvider);
            var target = firstProvider.GetRequiredService<PersistentServiceJobTarget>();

            await scheduler.AddJobAsync(
                "restore-job",
                JobSchedule.Delay(TimeSpan.FromMilliseconds(250)),
                target.RunWithPayloadAsync,
                new PersistentPayload("restored"));
        }

        using var secondProvider = new ServiceCollection()
            .AddSingleton<PersistentExecutionSink>()
            .AddTransient<PersistentServiceJobTarget>()
            .BuildServiceProvider();
        var sink = secondProvider.GetRequiredService<PersistentExecutionSink>();
        await using var restoredScheduler = new JobScheduler(
            store,
            TimeSpan.FromMilliseconds(20),
            serviceProvider: secondProvider);

        await restoredScheduler.StartAsync();
        await sink.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await restoredScheduler.StopAsync();

        Assert.Equal("restored", Assert.Single(sink.Messages));
    }

    [Fact]
    public async Task StartAsync_RestoresPersistentConstructibleInstanceMethodWithoutServiceProvider()
    {
        ConstructiblePersistentJobTarget.Reset();
        var store = new FakeJobStore();

        {
            var scheduler = new JobScheduler(store);
            await scheduler.AddJobAsync(
                "constructible-job",
                JobSchedule.Delay(TimeSpan.FromMilliseconds(250)),
                new ConstructiblePersistentJobTarget().RunAsync);
        }

        await using var restoredScheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));

        await restoredScheduler.StartAsync();
        await ConstructiblePersistentJobTarget.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await restoredScheduler.StopAsync();

        Assert.Equal("constructed", ConstructiblePersistentJobTarget.LastMessage);
    }

    [Fact]
    public async Task StartAsync_PreservesCancelledOneTimePersistentJob_AndRunsItAfterRestart()
    {
        CancelablePersistentJobs.Reset();
        var store = new FakeJobStore();

        await using (var scheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20)))
        {
            await scheduler.AddJobAsync(
                "cancelled-job",
                JobSchedule.Delay(TimeSpan.FromMilliseconds(50)),
                CancelablePersistentJobs.RunAsync,
                new RetryPolicy
                {
                    MaxRetries = 0,
                });

            await scheduler.StartAsync();
            await CancelablePersistentJobs.AttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await scheduler.StopAsync();
        }

        Assert.Equal(1, CancelablePersistentJobs.AttemptCount);
        Assert.Contains("cancelled-job", store.SavedJobs.Keys);
        Assert.Empty(store.ExecutionResults);

        CancelablePersistentJobs.AllowCompletion();

        await using (var restoredScheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20)))
        {
            await restoredScheduler.StartAsync();
            await CancelablePersistentJobs.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await restoredScheduler.StopAsync();
        }

        Assert.Equal(2, CancelablePersistentJobs.AttemptCount);
        Assert.DoesNotContain("cancelled-job", store.SavedJobs.Keys);
        Assert.Single(store.ExecutionResults);
        Assert.True(store.ExecutionResults[0].Success);
    }

    [Fact]
    public async Task StartAsync_SavesFailure_WhenPersistentInstanceMethodCannotBeConstructedAfterRestart()
    {
        var store = new FakeJobStore();

        {
            var scheduler = new JobScheduler(store);
            var target = new NonConstructiblePersistentJobTarget("value");

            await scheduler.AddJobAsync(
                "non-constructible-job",
                JobSchedule.Delay(TimeSpan.FromMilliseconds(100)),
                target.RunAsync,
                new RetryPolicy
                {
                    MaxRetries = 0,
                });
        }

        await using var restoredScheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));
        await restoredScheduler.StartAsync();
        await WaitForExecutionResultsAsync(store, expectedCount: 1, TimeSpan.FromSeconds(5));
        await restoredScheduler.StopAsync();

        var result = Assert.Single(store.ExecutionResults);
        Assert.False(result.Success);
        Assert.Contains("constructible", result.Exception, StringComparison.Ordinal);
        Assert.Empty(store.SavedJobs);
    }

    [Fact]
    public async Task StartAsync_RestoresPersistentValueTypePayload_WhenPersistedPayloadJsonIsNull()
    {
        ValueTypePayloadJobs.Reset();
        var store = new FakeJobStore();

        await store.SaveJobAsync(new JobDefinition
        {
            JobId = "value-type-payload-job",
            Interval = TimeSpan.FromMilliseconds(50),
            RunOnce = true,
            IsPersistent = true,
            MethodTypeName = typeof(ValueTypePayloadJobs).AssemblyQualifiedName,
            MethodName = nameof(ValueTypePayloadJobs.RunAsync),
            MethodIsStatic = true,
            PayloadTypeName = typeof(int).AssemblyQualifiedName,
            PayloadJson = null,
            NextRunTimeUtc = DateTime.UtcNow.AddMilliseconds(50),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 0,
            },
        });

        await using var scheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));
        await scheduler.StartAsync();
        await ValueTypePayloadJobs.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync();

        Assert.Equal(0, ValueTypePayloadJobs.LastPayload);
        Assert.Single(store.ExecutionResults);
        Assert.True(store.ExecutionResults[0].Success);
        Assert.Empty(store.SavedJobs);
    }

    [Fact]
    public async Task StartAsync_DoesNotRestoreInMemoryJobsAfterRestart()
    {
        var store = new FakeJobStore();

        {
            var scheduler = new JobScheduler(store);
            await scheduler.AddInMemoryJobAsync(
                "in-memory-job",
                JobSchedule.Delay(TimeSpan.FromMilliseconds(100)),
                _ => Task.CompletedTask);
        }

        await using var restoredScheduler = new JobScheduler(store, TimeSpan.FromMilliseconds(20));

        await restoredScheduler.StartAsync();
        await Task.Delay(250);
        await restoredScheduler.StopAsync();

        Assert.Empty(store.SavedJobs);
        Assert.Empty(store.ExecutionResults);
        Assert.Empty(await restoredScheduler.GetJobsAsync());
    }

    public sealed record PersistentPayload(string Message);

    public sealed class PersistentExecutionSink
    {
        public TaskCompletionSource CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Messages { get; } = [];

        public void Record(string message)
        {
            Messages.Add(message);
            CompletionSource.TrySetResult();
        }
    }

    public sealed class PersistentServiceJobTarget
    {
        private readonly PersistentExecutionSink _sink;

        public PersistentServiceJobTarget(PersistentExecutionSink sink)
        {
            _sink = sink;
        }

        public Task RunWithPayloadAsync(PersistentPayload payload, CancellationToken cancellationToken)
        {
            _sink.Record(payload.Message);
            return Task.CompletedTask;
        }
    }

    public sealed class ConstructiblePersistentJobTarget
    {
        public static TaskCompletionSource CompletionSource { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static string? LastMessage { get; private set; }

        public static void Reset()
        {
            LastMessage = null;
            CompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            LastMessage = "constructed";
            CompletionSource.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public static class StaticPersistentJobs
    {
        public static Task NoopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public static class CancelablePersistentJobs
    {
        public static TaskCompletionSource AttemptStarted { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TaskCompletionSource CompletionSource { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static bool ShouldBlockUntilCancelled { get; private set; } = true;

        public static int AttemptCount { get; private set; }

        public static void Reset()
        {
            AttemptCount = 0;
            ShouldBlockUntilCancelled = true;
            AttemptStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void AllowCompletion()
        {
            ShouldBlockUntilCancelled = false;
            CompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static async Task RunAsync(CancellationToken cancellationToken)
        {
            AttemptCount++;
            AttemptStarted.TrySetResult();

            if (ShouldBlockUntilCancelled)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return;
            }

            CompletionSource.TrySetResult();
        }
    }

    public sealed class NonConstructiblePersistentJobTarget
    {
        public NonConstructiblePersistentJobTarget(string value)
        {
        }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public static class ValueTypePayloadJobs
    {
        public static TaskCompletionSource CompletionSource { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static int LastPayload { get; private set; }

        public static void Reset()
        {
            LastPayload = int.MinValue;
            CompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static Task RunAsync(int payload, CancellationToken cancellationToken)
        {
            LastPayload = payload;
            CompletionSource.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private static async Task WaitForExecutionResultsAsync(FakeJobStore store, int expectedCount, TimeSpan timeout)
    {
        var startedUtc = DateTime.UtcNow;

        while (DateTime.UtcNow - startedUtc < timeout)
        {
            if (store.ExecutionResults.Count >= expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out while waiting for job execution results.");
    }
}
