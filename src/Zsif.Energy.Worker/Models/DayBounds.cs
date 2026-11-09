namespace Zsif.Energy.Worker.Models;

public readonly record struct DayBounds(DateOnly Day, DateTimeOffset From, DateTimeOffset To)
{
    public const int IntervalMinutes = 15;

    //92 on spring DST day, 100 on autumn DST day, otherwise 96
    public int ExpectedIntervalCount => (int)((To - From).TotalMinutes / IntervalMinutes);

    public static DayBounds Create(DateOnly day, TimeZoneInfo timeZone)
    {
        return new DayBounds(day, StartOfDay(day, timeZone), StartOfDay(day.AddDays(1), timeZone));
    }

    public static DateOnly Today(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo timeZone)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(localMidnight, timeZone.GetUtcOffset(localMidnight));
    }
}
