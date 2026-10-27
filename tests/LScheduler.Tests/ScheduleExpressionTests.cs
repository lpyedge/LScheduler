using LScheduler;

namespace LScheduler.Tests;

public sealed class JobScheduleTests
{
    [Fact]
    public void ImplicitString_UsesCronExpression()
    {
        JobSchedule schedule = "*/5 * * * * *";

        Assert.Equal(JobScheduleKind.Cron, schedule.Kind);
        Assert.Equal("*/5 * * * * *", schedule.Expression);
        Assert.Equal("*/5 * * * * *", schedule.CronExpression);
    }

    [Fact]
    public void EverySeconds_BuildsIntervalSchedule()
    {
        var schedule = JobSchedule.EverySeconds(5);

        Assert.Equal(JobScheduleKind.Every, schedule.Kind);
        Assert.Equal(TimeSpan.FromSeconds(5), schedule.Interval);
        Assert.False(schedule.IsOneTime);
    }

    [Fact]
    public void Delay_BuildsOneTimeSchedule()
    {
        var schedule = JobSchedule.Delay(TimeSpan.FromMinutes(2));

        Assert.Equal(JobScheduleKind.Delay, schedule.Kind);
        Assert.Equal(TimeSpan.FromMinutes(2), schedule.Interval);
        Assert.True(schedule.IsOneTime);
    }

    [Fact]
    public void DailyAt_BuildsExpectedCron()
    {
        var schedule = JobSchedule.DailyAt(6, 30);

        Assert.Equal("0 30 6 * * *", schedule.Expression);
    }

    [Fact]
    public void WeeklyAt_BuildsExpectedCron()
    {
        var schedule = JobSchedule.WeeklyAt(12, 0, DayOfWeek.Monday, DayOfWeek.Friday);

        Assert.Equal("0 0 12 * * 1,5", schedule.Expression);
    }

    [Fact]
    public void MonthlyAt_BuildsExpectedCron()
    {
        var schedule = JobSchedule.MonthlyAt(12, 0, 25, 5, 15);

        Assert.Equal("0 0 12 5,15,25 * *", schedule.Expression);
    }

    [Fact]
    public void YearlyAt_BuildsExpectedCron()
    {
        var schedule = JobSchedule.YearlyAt(12, 25, 8, 30);

        Assert.Equal("0 30 8 25 12 *", schedule.Expression);
    }

    [Fact]
    public void EveryMinutes_Throws_WhenValueIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.EveryMinutes(0));
    }

    [Fact]
    public void WeeklyAt_Throws_WhenNoDaysProvided()
    {
        Assert.Throws<ArgumentException>(() => JobSchedule.WeeklyAt(12, 0));
    }

    [Fact]
    public void Cron_Throws_WhenExpressionIsBlank()
    {
        Assert.Throws<ArgumentException>(() => JobSchedule.Cron(" "));
    }

    [Fact]
    public void Every_Throws_WhenIntervalIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.Every(TimeSpan.Zero));
    }

    [Fact]
    public void EverySeconds_Throws_WhenValueIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.EverySeconds(0));
    }

    [Fact]
    public void EveryHours_BuildsIntervalSchedule()
    {
        var schedule = JobSchedule.EveryHours(2);

        Assert.Equal(JobScheduleKind.Every, schedule.Kind);
        Assert.Equal(TimeSpan.FromHours(2), schedule.Interval);
    }

    [Fact]
    public void EveryMinutes_BuildsIntervalSchedule()
    {
        var schedule = JobSchedule.EveryMinutes(3);

        Assert.Equal(JobScheduleKind.Every, schedule.Kind);
        Assert.Equal(TimeSpan.FromMinutes(3), schedule.Interval);
    }

    [Fact]
    public void EveryHours_Throws_WhenValueIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.EveryHours(0));
    }

    [Fact]
    public void DailyAt_Throws_WhenHourIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.DailyAt(24, 0));
    }

    [Fact]
    public void DailyAt_Throws_WhenMinuteIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.DailyAt(12, 60));
    }

    [Fact]
    public void MonthlyAt_Throws_WhenNoDaysProvided()
    {
        Assert.Throws<ArgumentException>(() => JobSchedule.MonthlyAt(12, 0));
    }

    [Fact]
    public void MonthlyAt_Throws_WhenDayIsOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.MonthlyAt(12, 0, 0));
    }

    [Fact]
    public void YearlyAt_Throws_WhenMonthIsOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.YearlyAt(13, 1, 8, 30));
    }

    [Fact]
    public void YearlyAt_Throws_WhenDayIsOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.YearlyAt(12, 32, 8, 30));
    }

    [Fact]
    public void WeeklyAt_MapsSundayAndSaturdayToCronValues()
    {
        var schedule = JobSchedule.WeeklyAt(9, 15, DayOfWeek.Sunday, DayOfWeek.Saturday);

        Assert.Equal("0 15 9 * * 0,6", schedule.Expression);
    }

    [Fact]
    public void WeeklyAt_MapsTuesdayWednesdayAndThursdayToCronValues()
    {
        var schedule = JobSchedule.WeeklyAt(9, 15, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday);

        Assert.Equal("0 15 9 * * 2,3,4", schedule.Expression);
    }

    [Fact]
    public void WeeklyAt_Throws_WhenHourIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.WeeklyAt(24, 0, DayOfWeek.Monday));
    }

    [Fact]
    public void WeeklyAt_Throws_WhenMinuteIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.WeeklyAt(12, 60, DayOfWeek.Monday));
    }

    [Fact]
    public void WeeklyAt_Throws_WhenDayOfWeekIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSchedule.WeeklyAt(12, 0, (DayOfWeek)99));
    }

    [Fact]
    public void ToString_ReturnsFriendlyText_ForIntervalSchedules()
    {
        var every = JobSchedule.Every(TimeSpan.FromSeconds(5));
        var delay = JobSchedule.Delay(TimeSpan.FromSeconds(10));

        Assert.Equal("Every(00:00:05)", every.ToString());
        Assert.Equal("Delay(00:00:10)", delay.ToString());
    }

    [Fact]
    public void ToString_ReturnsCronExpression_ForCronSchedule()
    {
        var schedule = JobSchedule.Cron("*/5 * * * * *");

        Assert.Equal("*/5 * * * * *", schedule.ToString());
    }
}
