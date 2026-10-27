using LScheduler.MySql;

namespace LScheduler.Tests;

public sealed class MySqlDockerFixture : DockerComposeFixture
{
    public MySqlDockerFixture()
        : base("LScheduler.MySql", "lscheduler-mysql-tests", TimeSpan.FromSeconds(90))
    {
    }

    public string ConnectionString =>
        "Server=127.0.0.1;Port=3307;Database=ltask;User ID=root;Password=ltask_root_password;SslMode=None;AllowPublicKeyRetrieval=True;";

    protected override Task ProbeAsync(CancellationToken cancellationToken)
    {
        var store = new MySqlJobStore(ConnectionString);
        return store.LoadJobsAsync(cancellationToken);
    }
}
