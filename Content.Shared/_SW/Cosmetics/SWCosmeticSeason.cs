using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Shared._SW.Cosmetics;

/// <summary>
/// When cosmetics can be worn and when they are on sale. The dates are replicated cvars, so the owner can move them
/// without a new build. The server's answer is the one that counts; the client only uses its own for the loadout text.
/// </summary>
public static class SWCosmeticSeason
{
    public const string ModeAuto = "auto";
    public const string ModeOn = "on";
    public const string ModeOff = "off";

    public static bool IsVisible(IConfigurationManager cfg, DateTime utcNow)
    {
        return cfg.GetCVar(SWVars.CosmeticSeason) switch
        {
            ModeOn => true,
            ModeOff => false,
            _ => InYearlyWindow(utcNow, cfg.GetCVar(SWVars.CosmeticVisibleFrom), cfg.GetCVar(SWVars.CosmeticVisibleUntil)),
        };
    }

    public static bool IsSaleOpen(IConfigurationManager cfg, DateTime utcNow)
    {
        return InDateWindow(utcNow, cfg.GetCVar(SWVars.CosmeticSaleFrom), cfg.GetCVar(SWVars.CosmeticSaleUntil));
    }

    /// <summary>
    /// Whether the date is inside a window given as "MM-dd" that repeats every year and may cross the new year.
    /// Both ends count. A badly written date closes the window.
    /// </summary>
    public static bool InYearlyWindow(DateTime date, string from, string until)
    {
        if (!TryParseDay(from, out var start) || !TryParseDay(until, out var end))
            return false;

        var today = date.Month * 100 + date.Day;
        return start <= end
            ? today >= start && today <= end
            : today >= start || today <= end;
    }

    /// <summary>
    /// Whether the date is inside a window given as "yyyy-MM-dd". Both ends count. A badly written date closes it.
    /// </summary>
    public static bool InDateWindow(DateTime date, string from, string until)
    {
        if (!TryParseDate(from, out var start) || !TryParseDate(until, out var end))
            return false;

        var today = date.Year * 10000 + date.Month * 100 + date.Day;
        return today >= start && today <= end;
    }

    /// <summary>
    /// Turns "MM-dd" into "dd.MM" for the text players read.
    /// </summary>
    public static string FormatDay(string day)
    {
        var parts = day.Split('-');
        return parts.Length == 2 ? $"{parts[1]}.{parts[0]}" : day;
    }

    private static bool TryParseDay(string text, out int day)
    {
        day = 0;
        var parts = text.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var month) || month is < 1 or > 12
            || !int.TryParse(parts[1], out var dayOfMonth) || dayOfMonth is < 1 or > 31)
        {
            return false;
        }

        day = month * 100 + dayOfMonth;
        return true;
    }

    private static bool TryParseDate(string text, out int date)
    {
        date = 0;
        var split = text.IndexOf('-');
        if (split <= 0
            || !int.TryParse(text[..split], out var year)
            || !TryParseDay(text[(split + 1)..], out var day))
        {
            return false;
        }

        date = year * 10000 + day;
        return true;
    }
}
