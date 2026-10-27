using Cronos;

namespace LScheduler;

public static class CronHelper
{
    public static DateTime? GetNextOccurrence(string cron, DateTime baseTimeUtc)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ArgumentException("Cron is required.", nameof(cron));
        }

        var segments = cron.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var format = segments.Length switch
        {
            5 => CronFormat.Standard,
            6 => CronFormat.IncludeSeconds,
            _ => throw new ArgumentException("Cron must contain 5 or 6 segments.", nameof(cron)),
        };

        var utcBaseTime = baseTimeUtc.Kind == DateTimeKind.Utc
            ? baseTimeUtc
            : baseTimeUtc.ToUniversalTime();

        var expression = CronExpression.Parse(cron, format);
        return expression.GetNextOccurrence(utcBaseTime, TimeZoneInfo.Utc);
    }
}
