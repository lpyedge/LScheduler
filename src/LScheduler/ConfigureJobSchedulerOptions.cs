using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LScheduler;

internal sealed class ConfigureJobSchedulerOptions : IConfigureOptions<JobSchedulerOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public ConfigureJobSchedulerOptions(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Configure(JobSchedulerOptions options)
    {
        _serviceProvider
            .GetService<IConfiguration>()
            ?.GetSection(JobSchedulerOptions.SectionName)
            .Bind(options);
    }
}
