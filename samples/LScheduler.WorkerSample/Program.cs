using LScheduler;
using LScheduler.Sqlite;
using LScheduler.WorkerSample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLSchedulerSqlite(
    Path.Combine(AppContext.BaseDirectory, "data", "lscheduler-worker.db"),
    options =>
    {
        options.ScanInterval = TimeSpan.FromMilliseconds(250);
        options.RetryPolicy.MaxRetries = 3;
        options.RetryPolicy.RetryInterval = TimeSpan.FromSeconds(2);
    });
builder.Services.AddSingleton<DailyDigestJobs>();
builder.Services.AddSingleton<WorkerJobRegistrar>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();

internal sealed class Worker : BackgroundService
{
    private readonly WorkerJobRegistrar _jobRegistrar;

    public Worker(WorkerJobRegistrar jobRegistrar)
    {
        _jobRegistrar = jobRegistrar;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _jobRegistrar.RegisterAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
