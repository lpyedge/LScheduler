using System.Diagnostics;
using System.Text;

namespace LScheduler.Tests;

internal sealed class DockerComposeEnvironment
{
    private readonly string _workingDirectory;
    private readonly string _composeFilePath;
    private readonly string _projectName;

    public DockerComposeEnvironment(string workingDirectory, string composeFilePath, string projectName)
    {
        _workingDirectory = workingDirectory;
        _composeFilePath = composeFilePath;
        _projectName = projectName;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return RunDockerAsync(["compose", "-p", _projectName, "-f", _composeFilePath, "up", "-d"], cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return RunDockerAsync(["compose", "-p", _projectName, "-f", _composeFilePath, "down", "-v", "--remove-orphans"], cancellationToken);
    }

    private async Task RunDockerAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                outputBuilder.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                errorBuilder.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start docker process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker command failed with exit code {process.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{outputBuilder}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{errorBuilder}");
        }
    }
}
