using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace LScheduler;

public sealed class JobScheduler : IAsyncDisposable
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IJobStore _store;
    private readonly JobExecutor _jobExecutor;
    private readonly TimeSpan _scanInterval;
    private readonly RetryPolicy _defaultRetryPolicy;
    private readonly IServiceProvider? _serviceProvider;
    private readonly object _jobsLock = new();
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly Dictionary<string, JobDefinition> _persistedJobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JobDefinition> _jobs = new(StringComparer.Ordinal);

    private bool _initialized;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public JobScheduler(
        IJobStore store,
        TimeSpan? scanInterval = null,
        RetryPolicy? retryPolicy = null,
        IServiceProvider? serviceProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _jobExecutor = new JobExecutor(_store);
        _scanInterval = scanInterval ?? TimeSpan.FromSeconds(1);
        _defaultRetryPolicy = retryPolicy?.Clone() ?? new RetryPolicy();
        _serviceProvider = serviceProvider;
    }

    public Task AddJobAsync(
        string jobId,
        JobSchedule schedule,
        Func<CancellationToken, Task> method,
        RetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        Guard.NotNull(method, nameof(method));

        return AddJobAsync(
            CreatePersistentJobDefinition(
                jobId,
                schedule,
                method,
                retryPolicy,
                payloadType: null,
                payloadJson: null),
            cancellationToken);
    }

    public Task AddJobAsync<TPayload>(
        string jobId,
        JobSchedule schedule,
        Func<TPayload, CancellationToken, Task> method,
        TPayload payload,
        RetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        Guard.NotNull(method, nameof(method));

        return AddJobAsync(
            CreatePersistentJobDefinition(
                jobId,
                schedule,
                method,
                retryPolicy,
                typeof(TPayload),
                JsonSerializer.Serialize(payload, PayloadJsonOptions)),
            cancellationToken);
    }

    public Task AddInMemoryJobAsync(
        string jobId,
        JobSchedule schedule,
        Func<CancellationToken, Task> action,
        RetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        Guard.NotNull(action, nameof(action));

        return AddJobAsync(new JobDefinition
        {
            JobId = jobId,
            Cron = schedule.CronExpression ?? string.Empty,
            Interval = schedule.Interval,
            RunOnce = schedule.IsOneTime,
            Action = action,
            IsPersistent = false,
            RetryPolicy = retryPolicy?.Clone() ?? _defaultRetryPolicy.Clone(),
        }, cancellationToken);
    }

    internal async Task AddJobAsync(JobDefinition job, CancellationToken cancellationToken = default)
    {
        if (job.Action is null && HasPersistentMethodBinding(job))
        {
            job.Action = CreatePersistedAction(job);
        }

        ValidateJob(job);
        await EnsureInitializedAsync(cancellationToken);

        JobDefinition merged;
        var utcNow = DateTime.UtcNow;

        lock (_jobsLock)
        {
            if (_jobs.ContainsKey(job.JobId))
            {
                throw new InvalidOperationException($"Job '{job.JobId}' is already registered.");
            }

            _persistedJobs.TryGetValue(job.JobId, out var persistedJob);
            merged = MergeJob(job, persistedJob, utcNow);
            _jobs[merged.JobId] = merged;

            if (merged.IsPersistent)
            {
                _persistedJobs[merged.JobId] = merged.Clone(includeAction: false);
            }
        }

        if (merged.IsPersistent)
        {
            await _store.SaveJobAsync(merged, cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<JobDefinition>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        lock (_jobsLock)
        {
            var jobs = new Dictionary<string, JobDefinition>(StringComparer.Ordinal);

            foreach (var job in _persistedJobs.Values)
            {
                jobs[job.JobId] = job.Clone(includeAction: false);
            }

            foreach (var job in _jobs.Values.Where(item => !item.IsPersistent))
            {
                jobs[job.JobId] = job.Clone(includeAction: false);
            }

            return jobs.Values
                .OrderBy(job => job.JobId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public async Task<bool> RemoveJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        await EnsureInitializedAsync(cancellationToken);

        var removedActive = false;
        var removedPersisted = false;

        lock (_jobsLock)
        {
            removedActive = _jobs.Remove(jobId);
            removedPersisted = _persistedJobs.Remove(jobId);
        }

        if (!removedActive && !removedPersisted)
        {
            return false;
        }

        if (removedPersisted)
        {
            await _store.DeleteJobAsync(jobId, cancellationToken);
        }

        return true;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (_loopTask is not null)
        {
            return;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_loopCts.Token);
    }

    public async Task StopAsync()
    {
        if (_loopCts is null || _loopTask is null)
        {
            return;
        }

        _loopCts.Cancel();

        try
        {
            await _loopTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _loopTask = null;
            _loopCts.Dispose();
            _loopCts = null;
        }
    }

    internal IReadOnlyCollection<JobDefinition> GetRegisteredJobs()
    {
        lock (_jobsLock)
        {
            return _jobs.Values
                .OrderBy(job => job.JobId, StringComparer.Ordinal)
                .Select(job => job.Clone(includeAction: true))
                .ToArray();
        }
    }

    internal async Task ProcessDueJobsAsync(
        DateTime utcNow,
        bool waitForJobs,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        List<JobDefinition> dueJobs;

        lock (_jobsLock)
        {
            dueJobs = _jobs.Values
                .Where(job => job.NextRunTimeUtc.HasValue && job.NextRunTimeUtc.Value <= utcNow)
                .Select(job => job.Clone(includeAction: true))
                .ToList();
        }

        if (dueJobs.Count == 0)
        {
            return;
        }

        var executionTasks = new List<Task>(dueJobs.Count);

        foreach (var dueJob in dueJobs)
        {
            dueJob.LastRunTimeUtc = utcNow;
            var removeAfterRun = dueJob.RunOnce;

            if (removeAfterRun)
            {
                dueJob.NextRunTimeUtc = null;

                lock (_jobsLock)
                {
                    _jobs.Remove(dueJob.JobId);
                }
            }
            else
            {
                dueJob.NextRunTimeUtc = GetNextRunTimeUtc(dueJob, utcNow);

                lock (_jobsLock)
                {
                    _jobs[dueJob.JobId] = dueJob;

                    if (dueJob.IsPersistent)
                    {
                        _persistedJobs[dueJob.JobId] = dueJob.Clone(includeAction: false);
                    }
                }

                if (dueJob.IsPersistent)
                {
                    await _store.SaveJobAsync(dueJob, cancellationToken);
                }
            }

            var executionTask = ExecuteJobAsync(dueJob, removeAfterRun, cancellationToken);

            if (waitForJobs)
            {
                executionTasks.Add(executionTask);
            }
            else
            {
                _ = ObserveExecutionAsync(executionTask);
            }
        }

        if (waitForJobs)
        {
            await Task.WhenAll(executionTasks);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _initializeLock.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await ProcessDueJobsAsync(DateTime.UtcNow, waitForJobs: false, cancellationToken);
            await Task.Delay(_scanInterval, cancellationToken);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            var jobs = await _store.LoadJobsAsync(cancellationToken);

            lock (_jobsLock)
            {
                foreach (var job in jobs)
                {
                    job.IsPersistent = true;
                    _persistedJobs[job.JobId] = job.Clone(includeAction: false);

                    if (TryBindPersistedJob(job, out var activeJob))
                    {
                        _jobs[activeJob.JobId] = activeJob;
                    }
                }
            }

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task ExecuteJobAsync(JobDefinition job, bool removeAfterRun, CancellationToken cancellationToken)
    {
        var completed = await _jobExecutor.ExecuteAsync(job, cancellationToken);

        if (!removeAfterRun || !completed || !job.IsPersistent)
        {
            return;
        }

        lock (_jobsLock)
        {
            _persistedJobs.Remove(job.JobId);
        }

        await _store.DeleteJobAsync(job.JobId, cancellationToken);
    }

    private JobDefinition MergeJob(JobDefinition job, JobDefinition? persistedJob, DateTime utcNow)
    {
        var merged = job.Clone(includeAction: true);
        merged.RetryPolicy = job.RetryPolicy?.Clone() ?? _defaultRetryPolicy.Clone();

        if (!merged.IsPersistent || persistedJob is null)
        {
            merged.NextRunTimeUtc ??= GetNextRunTimeUtc(merged, utcNow);
            return merged;
        }

        merged.LastRunTimeUtc = persistedJob.LastRunTimeUtc;

        if (HasSameSchedule(job, persistedJob))
        {
            merged.NextRunTimeUtc = persistedJob.NextRunTimeUtc ?? merged.NextRunTimeUtc ?? GetNextRunTimeUtc(merged, utcNow);
            return merged;
        }

        merged.NextRunTimeUtc ??= GetNextRunTimeUtc(merged, utcNow);
        return merged;
    }

    private JobDefinition CreatePersistentJobDefinition(
        string jobId,
        JobSchedule schedule,
        Delegate method,
        RetryPolicy? retryPolicy,
        Type? payloadType,
        string? payloadJson)
    {
        var methodMetadata = CreateMethodMetadata(method, payloadType);

        var job = new JobDefinition
        {
            JobId = jobId,
            Cron = schedule.CronExpression ?? string.Empty,
            Interval = schedule.Interval,
            RunOnce = schedule.IsOneTime,
            IsPersistent = true,
            MethodTypeName = methodMetadata.MethodTypeName,
            MethodName = methodMetadata.MethodName,
            MethodIsStatic = methodMetadata.MethodIsStatic,
            PayloadTypeName = payloadType?.AssemblyQualifiedName,
            PayloadJson = payloadJson,
            RetryPolicy = retryPolicy?.Clone() ?? _defaultRetryPolicy.Clone(),
        };

        job.Action = CreatePersistedAction(job);
        return job;
    }

    private bool TryBindPersistedJob(JobDefinition job, out JobDefinition activeJob)
    {
        activeJob = job.Clone(includeAction: false);

        if (!HasPersistentMethodBinding(job))
        {
            return false;
        }

        activeJob.Action = CreatePersistedAction(activeJob);
        return true;
    }

    private Func<CancellationToken, Task> CreatePersistedAction(JobDefinition job)
    {
        return cancellationToken => InvokePersistedMethodAsync(job, cancellationToken);
    }

    private Task InvokePersistedMethodAsync(JobDefinition job, CancellationToken cancellationToken)
    {
        var methodType = ResolveType(job.MethodTypeName, nameof(job.MethodTypeName));
        var payloadType = string.IsNullOrWhiteSpace(job.PayloadTypeName)
            ? null
            : ResolveType(job.PayloadTypeName, nameof(job.PayloadTypeName));
        var method = FindPersistedMethod(methodType, job.MethodName, payloadType, job.MethodIsStatic);
        var target = job.MethodIsStatic ? null : CreateInvocationTarget(methodType);
        object?[] arguments = payloadType is null
            ? new object?[] { cancellationToken }
            : new object?[] { DeserializePayload(payloadType, job.PayloadJson), cancellationToken };
        var task = method.Invoke(target, arguments) as Task;

        return task ?? throw new InvalidOperationException(
            $"Method '{method.Name}' on '{methodType.FullName}' did not return a Task.");
    }

    private object CreateInvocationTarget(Type methodType)
    {
        if (_serviceProvider is not null)
        {
            return ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, methodType);
        }

        try
        {
            return Activator.CreateInstance(methodType)
                ?? throw new InvalidOperationException(
                    $"Could not create an instance of '{methodType.FullName}' for a persistent job.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Persistent instance method jobs require '{methodType.FullName}' to be constructible when no IServiceProvider is configured.",
                ex);
        }
    }

    private static MethodInfo FindPersistedMethod(
        Type methodType,
        string? methodName,
        Type? payloadType,
        bool isStatic)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new InvalidOperationException("Persistent job metadata is missing the method name.");
        }

        var bindingFlags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        Type[] parameterTypes = payloadType is null
            ? new[] { typeof(CancellationToken) }
            : new[] { payloadType, typeof(CancellationToken) };

        return methodType.GetMethod(methodName, bindingFlags, binder: null, parameterTypes, modifiers: null)
            ?? throw new InvalidOperationException(
                $"Could not find method '{methodName}' on '{methodType.FullName}' with the expected persistent job signature.");
    }

    private static object? DeserializePayload(Type payloadType, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || string.Equals(payloadJson, "null", StringComparison.Ordinal))
        {
            return payloadType.IsValueType ? Activator.CreateInstance(payloadType) : null;
        }

        return JsonSerializer.Deserialize(payloadJson!, payloadType, PayloadJsonOptions);
    }

    private static Type ResolveType(string? typeName, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new InvalidOperationException($"Persistent job metadata is missing '{propertyName}'.");
        }

        return Type.GetType(typeName, throwOnError: true)
            ?? throw new InvalidOperationException($"Could not resolve type '{typeName}'.");
    }

    private static PersistentMethodMetadata CreateMethodMetadata(Delegate method, Type? payloadType)
    {
        ValidatePersistentDelegate(method, payloadType);

        var methodType = method.Method.IsStatic
            ? method.Method.DeclaringType
            : method.Target?.GetType();

        if (methodType is null)
        {
            throw new ArgumentException("Persistent jobs require a resolvable method target type.", nameof(method));
        }

        return new PersistentMethodMetadata(
            methodType.AssemblyQualifiedName ?? throw new ArgumentException("Method type name is required.", nameof(method)),
            method.Method.Name,
            method.Method.IsStatic);
    }

    private static void ValidatePersistentDelegate(Delegate method, Type? payloadType)
    {
        Guard.NotNull(method, nameof(method));

        if (IsCompilerGenerated(method.Method)
            || IsCompilerGenerated(method.Method.DeclaringType)
            || (method.Target is not null && IsCompilerGenerated(method.Target.GetType())))
        {
            throw new ArgumentException(
                "Persistent jobs require a direct method group on a named type. Use AddInMemoryJobAsync for lambdas, local functions, or closures.",
                nameof(method));
        }

        if (method.Method.ReturnType != typeof(Task))
        {
            throw new ArgumentException("Persistent jobs require methods that return Task.", nameof(method));
        }

        if (!method.Method.IsPublic)
        {
            throw new ArgumentException("Persistent jobs require public methods.", nameof(method));
        }

        if (method.Method.ContainsGenericParameters)
        {
            throw new ArgumentException("Persistent jobs do not support open generic methods.", nameof(method));
        }

        var parameters = method.Method.GetParameters();

        if (payloadType is null)
        {
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(CancellationToken))
            {
                throw new ArgumentException(
                    "Persistent jobs without payload must use the signature Task Method(CancellationToken cancellationToken).",
                    nameof(method));
            }

            return;
        }

        if (parameters.Length != 2
            || parameters[0].ParameterType != payloadType
            || parameters[1].ParameterType != typeof(CancellationToken))
        {
            throw new ArgumentException(
                $"Persistent jobs with payload '{payloadType.FullName}' must use the signature Task Method({payloadType.Name} payload, CancellationToken cancellationToken).",
                nameof(method));
        }
    }

    private static bool IsCompilerGenerated(MemberInfo? memberInfo)
    {
        return memberInfo?.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) == true;
    }

    private static DateTime? GetNextRunTimeUtc(JobDefinition job, DateTime baseTimeUtc)
    {
        if (job.Interval.HasValue)
        {
            return baseTimeUtc + job.Interval.Value;
        }

        return CronHelper.GetNextOccurrence(job.Cron, baseTimeUtc);
    }

    private static bool HasSameSchedule(JobDefinition left, JobDefinition right)
    {
        return string.Equals(left.Cron, right.Cron, StringComparison.Ordinal)
            && left.Interval == right.Interval
            && left.RunOnce == right.RunOnce;
    }

    private static bool HasPersistentMethodBinding(JobDefinition job)
    {
        return !string.IsNullOrWhiteSpace(job.MethodTypeName)
            && !string.IsNullOrWhiteSpace(job.MethodName);
    }

    private void ValidateJob(JobDefinition job)
    {
        Guard.NotNull(job, nameof(job));

        if (string.IsNullOrWhiteSpace(job.JobId))
        {
            throw new ArgumentException("JobId is required.", nameof(job));
        }

        if (string.IsNullOrWhiteSpace(job.Cron) && !job.Interval.HasValue)
        {
            throw new ArgumentException("Schedule is required.", nameof(job));
        }

        if (job.Interval.HasValue && job.Interval.Value <= TimeSpan.Zero)
        {
            throw new ArgumentException("Interval must be greater than zero.", nameof(job));
        }

        if (!string.IsNullOrWhiteSpace(job.Cron))
        {
            _ = CronHelper.GetNextOccurrence(job.Cron, DateTime.UtcNow);
        }

        if (job.Action is null)
        {
            throw new ArgumentException("Action is required.", nameof(job));
        }

        job.RetryPolicy = job.RetryPolicy?.Clone() ?? _defaultRetryPolicy.Clone();
    }

    private static async Task ObserveExecutionAsync(Task executionTask)
    {
        try
        {
            await executionTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class PersistentMethodMetadata
    {
        public PersistentMethodMetadata(string methodTypeName, string methodName, bool methodIsStatic)
        {
            MethodTypeName = methodTypeName;
            MethodName = methodName;
            MethodIsStatic = methodIsStatic;
        }

        public string MethodTypeName { get; }

        public string MethodName { get; }

        public bool MethodIsStatic { get; }
    }
}
