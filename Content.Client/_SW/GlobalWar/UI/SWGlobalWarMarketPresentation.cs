using System.Linq;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Totals preserve each planet's demand and surplus instead of cancelling them against one another.</summary>
internal sealed record SWGlobalWarMarketTotals(long Stock, long Target, long Demand, long Supply, long Consumption,
    long Incoming, long Uncovered, double? Coverage)
{
    public long Balance => Supply - Demand;

    public static SWGlobalWarMarketTotals From(IEnumerable<SWGlobalWarMarketGoodView> goods)
    {
        var rows = goods.ToArray();
        var consuming = rows.Where(good => good.ConsumptionPerMinute > 0).ToArray();
        return new SWGlobalWarMarketTotals(rows.Sum(good => (long) good.Stock), rows.Sum(good => (long) good.TargetStock),
            rows.Sum(good => (long) good.Demand), rows.Sum(good => (long) good.Supply), rows.Sum(good => (long) good.ConsumptionPerMinute),
            rows.Sum(good => (long) good.Incoming), rows.Sum(good => (long) Math.Max(0, good.Demand - good.Incoming)),
            consuming.Length == 0 ? null : consuming.Min(good => (double) good.Stock / good.ConsumptionPerMinute));
    }
}

internal static class SWGlobalWarMarketPresentation
{
    private static readonly Color[] CommodityColors =
    {
        Color.FromHex("#79DEEC"), Color.FromHex("#E9C17E"), Color.FromHex("#B7D794"),
        Color.FromHex("#C2B4E7"), Color.FromHex("#DFA7A7"), Color.FromHex("#A7C5E7"),
    };

    public static Color CommodityColor(ProtoId<SWGlobalWarCommodityPrototype> commodity)
    {
        // Stable across processes, unlike randomized string hash codes.
        var hash = 2166136261u;
        foreach (var character in commodity.Id)
            hash = unchecked((hash ^ character) * 16777619);
        return CommodityColors[hash % CommodityColors.Length];
    }

    public static string Coverage(double? minutes) => minutes is { } value
        ? Loc.GetString("sw-global-war-market-minutes", ("value", value.ToString("0.##")))
        : Loc.GetString("sw-global-war-market-no-consumption");

    public static string Summary(SWGlobalWarMarketTotals totals) => Loc.GetString("sw-global-war-market-summary",
        ("stock", totals.Stock), ("target", totals.Target), ("demand", totals.Demand), ("supply", totals.Supply),
        ("consumption", totals.Consumption), ("incoming", totals.Incoming), ("uncovered", totals.Uncovered),
        ("coverage", Coverage(totals.Coverage)));
}
