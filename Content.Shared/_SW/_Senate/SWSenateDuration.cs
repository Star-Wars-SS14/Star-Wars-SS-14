namespace Content.Shared._SW._Senate;

public static class SWSenateDuration
{
    public static string FormatClock(double hour)
    {
        var minute = (int) Math.Floor(hour * 60d + 0.00001d);
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    public static string FormatHours(double hours)
    {
        var minutes = (int) Math.Ceiling(Math.Max(0d, hours) * 60d);
        return minutes >= 60 ? $"{minutes / 60} ч {minutes % 60:00} мин" : $"{minutes} мин";
    }
}
