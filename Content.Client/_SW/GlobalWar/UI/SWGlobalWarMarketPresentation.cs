using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Totals preserve each planet's demand and surplus instead of cancelling them against one another.</summary>
internal sealed record SWGlobalWarMarketTotals(double Stock, double Target, double Demand, double Supply, double Consumption,
    double Incoming, double Uncovered, double? Coverage, double Reserved, double Production,
    double RecipeUse, double GrowthUse, double Capacity, double ReservedIncoming)
{
    public double Balance => Supply - Demand;
    public double Available => Stock - Reserved;

    public static SWGlobalWarMarketTotals From(IEnumerable<SWGlobalWarMarketGoodView> goods)
    {
        var rows = goods.ToArray();
        var consuming = rows.Where(good => good.ConsumptionPerMinute + good.RecipeConsumptionPerMinute + good.GrowthConsumptionPerMinute > 0).ToArray();
        return new SWGlobalWarMarketTotals(rows.Sum(good => good.Stock), rows.Sum(good => good.TargetStock),
            rows.Sum(good => good.Demand), rows.Sum(good => good.Supply), rows.Sum(good => good.ConsumptionPerMinute),
            rows.Sum(good => good.Incoming), rows.Sum(good => Math.Max(0, good.Demand - good.Incoming)),
            consuming.Length == 0 ? null : consuming.Min(good => good.Available /
                (good.ConsumptionPerMinute + good.RecipeConsumptionPerMinute + good.GrowthConsumptionPerMinute)),
            rows.Sum(good => good.Reserved), rows.Sum(good => good.ProductionPerMinute),
            rows.Sum(good => good.RecipeConsumptionPerMinute), rows.Sum(good => good.GrowthConsumptionPerMinute),
            rows.Sum(good => good.Capacity), rows.Sum(good => good.ReservedIncoming));
    }
}

internal static class SWGlobalWarMarketPresentation
{
    private static readonly Color[] CommodityColors =
    {
        Color.FromHex("#79DEEC"), Color.FromHex("#E9C17E"), Color.FromHex("#B7D794"),
        Color.FromHex("#C2B4E7"), Color.FromHex("#DFA7A7"), Color.FromHex("#A7C5E7"),
    };

    /// <summary>The goods that have their own icon. The prototype keeps its entity, so the choice lives here.</summary>
    private static string? CommodityIconState(string commodity) => commodity switch
    {
        "SWGlobalWarF" => "food_bulk",
        "SWGlobalWarE" => "electronics",
        "SWGlobalWarAE" => "advanced_electronics",
        "SWGlobalWarL" => "luxury",
        _ => null,
    };

    /// <summary>A good's picture: its own icon, or the sprite of its entity.</summary>
    public static Control CommodityPicture(SWGlobalWarCommodityPrototype commodity, Vector2 size, Thickness margin)
    {
        if (CommodityIconState(commodity.ID) is { } state)
        {
            return new TextureRect
            {
                Texture = SWGlobalWarConsoleStyle.Art("metrics", state),
                TextureScale = new Vector2(2f),
                Stretch = TextureRect.StretchMode.KeepCentered,
                SetSize = size,
                Margin = margin,
                VerticalAlignment = Control.VAlignment.Center,
            };
        }

        var view = new EntityPrototypeView
        {
            SetSize = size,
            Margin = margin,
            Stretch = SpriteView.StretchMode.Fill,
            VerticalAlignment = Control.VAlignment.Center,
        };
        view.SetPrototype(commodity.Entity);
        return view;
    }

    /// <summary>A small icon for a line of text.</summary>
    public static TextureRect SmallIcon(string rsi, string state) => new()
    {
        Texture = SWGlobalWarConsoleStyle.Art(rsi, state),
        TextureScale = new Vector2(1f),
        Stretch = TextureRect.StretchMode.KeepCentered,
        SetSize = new Vector2(16),
        VerticalAlignment = Control.VAlignment.Center,
    };

    public static string AvailabilityIcon(SWGlobalWarMarketAvailability availability) => availability switch
    {
        SWGlobalWarMarketAvailability.Unknown => "unknown",
        SWGlobalWarMarketAvailability.Restricted => "restricted",
        SWGlobalWarMarketAvailability.Stale => "stale",
        _ => "unavailable",
    };

    public static Color CommodityColor(ProtoId<SWGlobalWarCommodityPrototype> commodity)
    {
        // Stable across processes, unlike randomized string hash codes.
        var hash = 2166136261u;
        foreach (var character in commodity.Id)
            hash = unchecked((hash ^ character) * 16777619);
        return CommodityColors[hash % CommodityColors.Length];
    }

    public static string Availability(SWGlobalWarMarketAvailability availability) => Loc.GetString(availability switch
    {
        SWGlobalWarMarketAvailability.Unknown => "sw-global-war-market-data-unknown",
        SWGlobalWarMarketAvailability.Restricted => "sw-global-war-market-data-restricted",
        SWGlobalWarMarketAvailability.Stale => "sw-global-war-market-data-stale",
        _ => "sw-global-war-market-offline",
    });

    /// <summary>
    /// A quantity short enough for a tile or a table cell: whole units from a hundred up, one decimal from ten up.
    /// Three decimals of a thousand units only pushed the figure out of its cell.
    /// </summary>
    public static string Amount(double value)
    {
        var size = Math.Abs(value);
        return value.ToString(size >= 100 ? "0" : size >= 10 ? "0.#" : "0.##");
    }

    public static string Coverage(double? minutes) => minutes is { } value
        ? Loc.GetString("sw-global-war-market-minutes", ("value", value.ToString("0.##")))
        : Loc.GetString("sw-global-war-market-no-consumption");

    public static string Summary(SWGlobalWarMarketTotals totals) => Loc.GetString("sw-global-war-market-summary",
        ("stock", totals.Stock.ToString("0.###")), ("target", totals.Target.ToString("0.###")), ("demand", totals.Demand.ToString("0.###")), ("supply", totals.Supply.ToString("0.###")),
        ("consumption", totals.Consumption.ToString("0.###")), ("incoming", totals.Incoming.ToString("0.###")), ("uncovered", totals.Uncovered.ToString("0.###")),
        ("coverage", Coverage(totals.Coverage)));
}
