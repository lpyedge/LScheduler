using LScheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LScheduler.Redis;

public static class LSchedulerRedisServiceCollectionExtensions
{
    private const string ProviderSectionPath = JobSchedulerOptions.SectionName + ":Redis";

    /// <summary>
    /// Registers the scheduler with a Redis store whose setting is read from
    /// <c>LScheduler:Redis:Configuration</c> of the application <see cref="IConfiguration"/>.
    /// </summary>
    public static IServiceCollection AddLSchedulerRedis(this IServiceCollection services)
    {
        Guard.NotNull(services, nameof(services));

        services.AddSingleton<IJobStore>(serviceProvider =>
        {
            var redisConfiguration = serviceProvider.GetService<IConfiguration>()?[$"{ProviderSectionPath}:Configuration"];

            if (string.IsNullOrWhiteSpace(redisConfiguration))
            {
                throw new InvalidOperationException($"{ProviderSectionPath}:Configuration is required.");
            }

            return new RedisJobStore(redisConfiguration!);
        });
        return services.AddLScheduler();
    }

    public static IServiceCollection AddLSchedulerRedis(this IServiceCollection services, string configuration)
    {
        Guard.NotNull(services, nameof(services));

        services.AddSingleton<IJobStore>(_ => new RedisJobStore(configuration));
        return services.AddLScheduler();
    }

    public static IServiceCollection AddLSchedulerRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));

        var redisConfiguration = configuration[$"{ProviderSectionPath}:Configuration"];

        if (string.IsNullOrWhiteSpace(redisConfiguration))
        {
            throw new ArgumentException($"{ProviderSectionPath}:Configuration is required.", nameof(configuration));
        }

        services.AddSingleton<IJobStore>(_ => new RedisJobStore(redisConfiguration));
        return services.AddLScheduler(configuration);
    }

    public static IServiceCollection AddLSchedulerRedis(
        this IServiceCollection services,
        string configuration,
        Action<JobSchedulerOptions> configure)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configure, nameof(configure));

        services.AddSingleton<IJobStore>(_ => new RedisJobStore(configuration));
        return services.AddLScheduler(configure);
    }

    public static IServiceCollection AddLSchedulerRedis(
        this IServiceCollection services,
        string configuration,
        IConfiguration appConfiguration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(appConfiguration, nameof(appConfiguration));

        services.AddSingleton<IJobStore>(_ => new RedisJobStore(configuration));
        return services.AddLScheduler(appConfiguration);
    }
}
