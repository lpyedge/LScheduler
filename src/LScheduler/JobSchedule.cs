namespace LScheduler;

public readonly struct JobSchedule
{
    private JobSchedule(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("Schedule expression is required.", nameof(expression));
        }

        _ = CronHelper.GetNextOccurrence(expression, DateTime.UtcNow);
        Kind = JobScheduleKind.Cron;
        CronExpression = expression;
        Interval = null;
    }

    private JobSchedule(JobScheduleKind kind, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");
        }

        Kind = kind;
        CronExpression = null;
        Interval = interval;
    }

    public JobScheduleKind Kind { get; }

    public string? CronExpression { get; }

    public TimeSpan? Interval { get; }

    public bool IsOneTime => Kind == JobScheduleKind.Delay;

    public string Expression => CronExpression ?? Kind.ToString();

    public static JobSchedule Cron(string expression)
    {
        return new JobSchedule(expression);
    }

    public static JobSchedule Every(TimeSpan interval)
    {
        return new JobSchedule(JobScheduleKind.Every, interval);
    }

    public static JobSchedule Delay(TimeSpan delay)
    {
        return new JobSchedule(JobScheduleKind.Delay, delay);
    }

    public static JobSchedule EverySeconds(int seconds)
    {
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Seconds must be greater than zero.");
        }

        return Every(TimeSpan.FromSeconds(seconds));
    }

    public static JobSchedule EveryMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), "Minutes must be greater than zero.");
        }

        return Every(TimeSpan.FromMinutes(minutes));
    }

    public static JobSchedule EveryHours(int hours)
    {
        if (hours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hours), "Hours must be greater than zero.");
        }

        return Every(TimeSpan.FromHours(hours));
    }

    public static JobSchedule DailyAt(int hour, int minute)
    {
        ValidateTime(hour, minute);

        return new JobSchedule($"0 {minute} {hour} * * *");
    }

    public static JobSchedule WeeklyAt(int hour, int minute, params DayOfWeek[] daysOfWeek)
    {
        ValidateTime(hour, minute);

        if (daysOfWeek is null || daysOfWeek.Length == 0)
        {
            throw new ArgumentException("At least one day of week is required.", nameof(daysOfWeek));
        }

        var daySegment = string.Join(",", daysOfWeek.Select(MapDayOfWeek));
        return new JobSchedule($"0 {minute} {hour} * * {daySegment}");
    }

    public static JobSchedule MonthlyAt(int hour, int minute, params int[] daysOfMonth)
    {
        ValidateTime(hour, minute);

        if (daysOfMonth is null || daysOfMonth.Length == 0)
        {
            throw new ArgumentException("At least one day of month is required.", nameof(daysOfMonth));
        }

        foreach (var day in daysOfMonth)
        {
            if (day is < 1 or > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(daysOfMonth), "Days of month must be between 1 and 31.");
            }
        }

        var daySegment = string.Join(",", daysOfMonth.OrderBy(day => day));
        return new JobSchedule($"0 {minute} {hour} {daySegment} * *");
    }

    public static JobSchedule YearlyAt(int month, int day, int hour, int minute)
    {
        ValidateTime(hour, minute);

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");
        }

        if (day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), "Day must be between 1 and 31.");
        }

        return new JobSchedule($"0 {minute} {hour} {day} {month} *");
    }

    public override string ToString()
    {
        return Kind switch
        {
            JobScheduleKind.Cron => CronExpression!,
            JobScheduleKind.Every => $"Every({Interval})",
            JobScheduleKind.Delay => $"Delay({Interval})",
            _ => Expression,
        };
    }

    public static implicit operator JobSchedule(string expression)
    {
        return Cron(expression);
    }

    private static void ValidateTime(int hour, int minute)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), "Hour must be between 0 and 23.");
        }

        if (minute is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), "Minute must be between 0 and 59.");
        }
    }

    private static int MapDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek)),
        };
    }
}

public enum JobScheduleKind
{
    Cron = 1,
    Every = 2,
    Delay = 3,
}
