using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LScheduler;

public static class LSchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddLScheduler(this IServiceCollection services)
    {
        return services.AddLScheduler(_ => { });
    }

    public static IServiceCollection AddLScheduler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));

        var options = configuration.GetSection(JobSchedulerOptions.SectionName).Get<JobSchedulerOptions>() ?? new JobSchedulerOptions();
        return services.AddLScheduler(options);
    }

    public static IServiceCollection AddLScheduler(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configurationSection, nameof(configurationSection));

        var options = configurationSection.Get<JobSchedulerOptions>() ?? new JobSchedulerOptions();
        return services.AddLScheduler(options);
    }

    public static IServiceCollection AddLScheduler(
        this IServiceCollection services,
        Action<JobSchedulerOptions> configure)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configure, nameof(configure));

        var options = new JobSchedulerOptions();
        configure(options);
        return services.AddLScheduler(options);
    }

    private static IServiceCollection AddLScheduler(
        this IServiceCollection services,
        JobSchedulerOptions options)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(options, nameof(options));

        services.TryAddSingleton(options);
        services.TryAddSingleton<IJobStore>(serviceProvider =>
        {
            var schedulerOptions = serviceProvider.GetRequiredService<JobSchedulerOptions>();
            return new FileJobStore(schedulerOptions.DataDirectory);
        });
        services.TryAddSingleton(serviceProvider =>
        {
            var schedulerOptions = serviceProvider.GetRequiredService<JobSchedulerOptions>();
            var store = serviceProvider.GetRequiredService<IJobStore>();
            return new JobScheduler(store, schedulerOptions.ScanInterval, schedulerOptions.RetryPolicy, serviceProvider);
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LSchedulerHostedService>());

        return services;
    }
}
