namespace Artibir.Izleme.Time;

public static class TurkeyTime
{
    public static TimeZoneInfo Zone { get; } = Resolve();

    public static DateTime NowUnspecified()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    public static DateTime StartOfToday() => NowUnspecified().Date;

    static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }
}
