using LScheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LScheduler.Sqlite;

public static class LSchedulerSqliteServiceCollectionExtensions
{
    private const string ProviderSectionPath = JobSchedulerOptions.SectionName + ":Sqlite";

    public static IServiceCollection AddLSchedulerSqlite(this IServiceCollection services, string databasePath)
    {
        Guard.NotNull(services, nameof(services));

        services.AddSingleton<IJobStore>(_ => new SqliteJobStore(databasePath));
        return services.AddLScheduler();
    }

    public static IServiceCollection AddLSchedulerSqlite(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));

        var databasePath = configuration[$"{ProviderSectionPath}:DatabasePath"];

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException($"{ProviderSectionPath}:DatabasePath is required.", nameof(configuration));
        }

        services.AddSingleton<IJobStore>(_ => new SqliteJobStore(databasePath));
        return services.AddLScheduler(configuration);
    }

    public static IServiceCollection AddLSchedulerSqlite(
        this IServiceCollection services,
        string databasePath,
        Action<JobSchedulerOptions> configure)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configure, nameof(configure));

        services.AddSingleton<IJobStore>(_ => new SqliteJobStore(databasePath));
        return services.AddLScheduler(configure);
    }

    public static IServiceCollection AddLSchedulerSqlite(
        this IServiceCollection services,
        string databasePath,
        IConfiguration configuration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));

        services.AddSingleton<IJobStore>(_ => new SqliteJobStore(databasePath));
        return services.AddLScheduler(configuration);
    }
}
