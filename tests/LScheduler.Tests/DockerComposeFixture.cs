namespace LScheduler.Tests;

public abstract class DockerComposeFixture : IAsyncLifetime
{
    private readonly DockerComposeEnvironment _environment;
    private readonly TimeSpan _waitTimeout;

    protected DockerComposeFixture(string providerProjectName, string projectName, TimeSpan? waitTimeout = null)
    {
        var projectPath = TestPaths.GetProviderProjectPath(providerProjectName);
        _environment = new DockerComposeEnvironment(
            projectPath,
            Path.Combine(projectPath, "docker-compose.yml"),
            projectName);
        _waitTimeout = waitTimeout ?? TimeSpan.FromSeconds(90);
    }

    public async Task InitializeAsync()
    {
        await _environment.StopAsync();
        await _environment.StartAsync();
        await WaitUntilReadyAsync(_waitTimeout);
    }

    public Task DisposeAsync()
    {
        return _environment.StopAsync();
    }

    protected abstract Task ProbeAsync(CancellationToken cancellationToken);

    private async Task WaitUntilReadyAsync(TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        Exception? lastException = null;

        while (DateTime.UtcNow - startTime < timeout)
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                await ProbeAsync(cancellationTokenSource.Token);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new TimeoutException("Timed out while waiting for dockerized service to become ready.", lastException);
    }
}
