using LScheduler.SqlServer;

namespace LScheduler.Tests;

public sealed class SqlServerDockerFixture : DockerComposeFixture
{
    public SqlServerDockerFixture()
        : base("LScheduler.SqlServer", "lscheduler-sqlserver-tests", TimeSpan.FromSeconds(150))
    {
    }

    public string ConnectionString =>
        "Server=127.0.0.1,14333;Database=master;User Id=sa;Password=Your_password123!;Encrypt=False;TrustServerCertificate=True;";

    protected override Task ProbeAsync(CancellationToken cancellationToken)
    {
        var store = new SqlServerJobStore(ConnectionString);
        return store.LoadJobsAsync(cancellationToken);
    }
}
