namespace LScheduler.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = ResolveRepositoryRoot();

    public static string GetProviderProjectPath(string providerProjectName)
    {
        return Path.Combine(RepositoryRoot, "src", providerProjectName);
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LScheduler.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
