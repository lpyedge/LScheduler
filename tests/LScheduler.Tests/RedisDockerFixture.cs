using LScheduler.Redis;

namespace LScheduler.Tests;

public sealed class RedisDockerFixture : DockerComposeFixture
{
    public RedisDockerFixture()
        : base("LScheduler.Redis", "lscheduler-redis-tests", TimeSpan.FromSeconds(60))
    {
    }

    public string Configuration => "127.0.0.1:6380";

    protected override Task ProbeAsync(CancellationToken cancellationToken)
    {
        var store = new RedisJobStore(Configuration);
        return store.LoadJobsAsync(cancellationToken);
    }
}
