using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LScheduler;

public static class LSchedulerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scheduler. Options start from defaults and are overridden by the
    /// <c>LScheduler</c> section of the application <see cref="IConfiguration"/> when one exists.
    /// </summary>
    public static IServiceCollection AddLScheduler(this IServiceCollection services)
    {
        Guard.NotNull(services, nameof(services));

        services.AddOptions();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JobSchedulerOptions>, ConfigureJobSchedulerOptions>());
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<JobSchedulerOptions>>().Value);
        services.TryAddSingleton<IJobStore>(serviceProvider =>
        {
            var schedulerOptions = serviceProvider.GetRequiredService<JobSchedulerOptions>();
            return new FileJobStore(schedulerOptions.DataDirectory);
        });
        services.TryAddSingleton(serviceProvider =>
        {
            var schedulerOptions = serviceProvider.GetRequiredService<JobSchedulerOptions>();
            var store = serviceProvider.GetRequiredService<IJobStore>();
            return new JobScheduler(
                store,
                schedulerOptions.ScanInterval,
                schedulerOptions.RetryPolicy,
                serviceProvider,
                schedulerOptions.ExecutionLog);
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LSchedulerHostedService>());

        return services;
    }

    public static IServiceCollection AddLScheduler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));

        return services.AddLScheduler(configuration.GetSection(JobSchedulerOptions.SectionName));
    }

    public static IServiceCollection AddLScheduler(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        Guard.NotNull(configurationSection, nameof(configurationSection));

        return services
            .AddLScheduler()
            .Configure<JobSchedulerOptions>(options => configurationSection.Bind(options));
    }

    public static IServiceCollection AddLScheduler(
        this IServiceCollection services,
        Action<JobSchedulerOptions> configure)
    {
        Guard.NotNull(configure, nameof(configure));

        return services
            .AddLScheduler()
            .Configure(configure);
    }
}
