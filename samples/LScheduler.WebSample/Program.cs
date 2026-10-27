using LScheduler;
using LScheduler.WebSample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLScheduler(options =>
{
    options.DataDirectory = Path.Combine(AppContext.BaseDirectory, "data", "lscheduler-web");
    options.ScanInterval = TimeSpan.FromMilliseconds(250);
});
builder.Services.AddSingleton<RefreshCacheJobs>();
builder.Services.AddSingleton<WebJobRegistrar>();

var app = builder.Build();

await app.Services.GetRequiredService<WebJobRegistrar>().RegisterAsync();

app.MapGet("/jobs", async (JobScheduler scheduler, CancellationToken cancellationToken) =>
{
    var jobs = await scheduler.GetJobsAsync(cancellationToken);

    return jobs.Select(job => new
    {
        job.JobId,
        job.Cron,
        job.Interval,
        job.RunOnce,
        job.MethodTypeName,
        job.MethodName,
        job.NextRunTimeUtc,
    });
});

await app.RunAsync();
