using Microsoft.Extensions.Hosting;

namespace LScheduler;

internal sealed class LSchedulerHostedService : IHostedService
{
    private readonly JobScheduler _scheduler;

    public LSchedulerHostedService(JobScheduler scheduler)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _scheduler.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _scheduler.StopAsync();
    }
}
