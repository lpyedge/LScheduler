using LScheduler;

namespace LScheduler.Tests;

internal static class TestExecutionLog
{
    public static ExecutionLogOptions All => new()
    {
        Success = true,
        Failure = true,
        Retry = true,
    };
}
