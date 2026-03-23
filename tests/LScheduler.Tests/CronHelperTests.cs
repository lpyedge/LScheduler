using LScheduler;

namespace LScheduler.Tests;

public sealed class CronHelperTests
{
    [Fact]
    public void GetNextOccurrence_Throws_WhenCronIsBlank()
    {
        var exception = Assert.Throws<ArgumentException>(() => CronHelper.GetNextOccurrence(" ", DateTime.UtcNow));

        Assert.Equal("cron", exception.ParamName, ignoreCase: true);
    }

    [Fact]
    public void GetNextOccurrence_SupportsStandardCron()
    {
        var baseTimeUtc = new DateTime(2026, 03, 23, 10, 15, 00, DateTimeKind.Utc);

        var next = CronHelper.GetNextOccurrence("*/5 * * * *", baseTimeUtc);

        Assert.Equal(new DateTime(2026, 03, 23, 10, 20, 00, DateTimeKind.Utc), next);
    }

    [Fact]
    public void GetNextOccurrence_SupportsCronWithSeconds()
    {
        var baseTimeUtc = new DateTime(2026, 03, 23, 10, 15, 03, DateTimeKind.Utc);

        var next = CronHelper.GetNextOccurrence("*/5 * * * * *", baseTimeUtc);

        Assert.Equal(new DateTime(2026, 03, 23, 10, 15, 05, DateTimeKind.Utc), next);
    }

    [Fact]
    public void GetNextOccurrence_ConvertsLocalTimeToUtc()
    {
        var localTime = new DateTime(2026, 03, 23, 10, 15, 00, DateTimeKind.Local);

        var next = CronHelper.GetNextOccurrence("* * * * *", localTime);

        Assert.NotNull(next);
        Assert.Equal(DateTimeKind.Utc, next.Value.Kind);
    }
}
