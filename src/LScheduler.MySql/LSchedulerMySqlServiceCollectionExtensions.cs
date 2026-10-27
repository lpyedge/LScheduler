using LScheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LScheduler.MySql;

public static class LSchedulerMySqlServiceCollectionExtensions
{
    private const string ProviderSectionPath = JobSchedulerOptions.SectionName + ":MySql";

    /// <summary>
    /// Registers the scheduler with a MySql store whose setting is read from
    /// <c>LScheduler:MySql:ConnectionString</c> of the application <see cref="IConfiguration"/>.
    /// </summary>
    public static IServiceCollection AddLSchedulerMySql(this IServiceCollection services)
    {
        Guard.NotNull(services, nameof(services));

        services.AddSingleton<IJobStore>(serviceProvider =>
        {
            var connectionString = serviceProvider.GetService<IConfiguration>()?[$"{ProviderSectionPath}:ConnectionString"];

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"{ProviderSectionPath}:ConnectionString is required.");
            }

            return new MySqlJobStore(connectionString!);
        });
        return services.AddLScheduler();
    }

    public static IServiceCollection AddLSchedulerMySql(this IServiceCollection services, string connectionString)
    {
        Guard.NotNull(services, nameof(services));

        services.AddSingleton<IJobStore>(_ => new MySqlJobStore(connectionString));
        return services.AddLScheduler();
    }

    public static IServiceCollection AddLSchedulerMySql(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));

        var connectionString = configuration[$"{ProviderSectionPath}:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException($"{ProviderSectionPath}:ConnectionString is required.", nameof(configuration));
        }

        services.AddSingleton<IJobStore>(_ => new MySqlJobStore(connectionString));
        return services.AddLScheduler(configuration);
    }

    public static IServiceCollection AddLSchedulerMySql(
        this IServiceCollection services,
        string connectionString,
        Action<JobSchedulerOptions> configure)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configure, nameof(configure));

        services.AddSingleton<IJobStore>(_ => new MySqlJobStore(connectionString));
        return services.AddLScheduler(configure);
    }

    public static IServiceCollection AddLSchedulerMySql(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));

        services.AddSingleton<IJobStore>(_ => new MySqlJobStore(connectionString));
        return services.AddLScheduler(configuration);
    }
}
