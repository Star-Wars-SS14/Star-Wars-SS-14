using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>The planet's figures as tiles: three by two beside the map, seven in a row without it.</summary>
internal sealed class SWGlobalWarMarketStats : BoxContainer
{
    private readonly Label _stock;
    private readonly Label _consumption;
    private readonly Label _coverage;
    private readonly Label _supply;
    private readonly Label _incoming;
    private readonly Label _uncovered;
    private readonly Label? _demand;
    private readonly TextureRect? _stockIcon;

    public SWGlobalWarMarketStats(bool wide)
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        var first = Row();
        var stock = new SWGlobalWarStatTile(Loc.GetString("sw-global-war-market-stat-stock"), 13, "stock");
        first.AddChild(stock);
        _stock = stock.Value;
        _stockIcon = stock.Icon;
        _consumption = Tile(first, "sw-global-war-market-stat-consumption", 13);
        if (wide)
        {
            _demand = Tile(first, "sw-global-war-market-stat-demand", 17);
            _supply = Tile(first, "sw-global-war-market-stat-supply", 17);
            _incoming = Tile(first, "sw-global-war-market-stat-incoming", 17, "in_transit");
            _uncovered = Tile(first, "sw-global-war-market-stat-uncovered", 17);
            _coverage = Tile(first, "sw-global-war-market-stat-coverage", 17);
            return;
        }

        _coverage = Tile(first, "sw-global-war-market-stat-coverage", 17);
        var second = Row();
        _supply = Tile(second, "sw-global-war-market-stat-supply", 17);
        _incoming = Tile(second, "sw-global-war-market-stat-incoming", 17, "in_transit");
        _uncovered = Tile(second, "sw-global-war-market-stat-uncovered", 17);
    }

    /// <summary>A narrow panel takes the long figures a size smaller, so that they are not cut.</summary>
    public void SetNarrow(bool narrow)
    {
        _stock.FontOverride = _consumption.FontOverride = SWGlobalWarConsoleStyle.Bold(narrow ? 11 : 13);
        // Its caption is the longest, and the tile has no room for both.
        if (_stockIcon != null)
            _stockIcon.Visible = !narrow;
    }

    public void Update(SWGlobalWarMarketTotals totals)
    {
        _stock.Text = $"{SWGlobalWarMarketPresentation.Amount(totals.Stock)} / {SWGlobalWarMarketPresentation.Amount(totals.Target)}";
        _consumption.Text = Loc.GetString("sw-global-war-market-per-minute", ("value", SWGlobalWarMarketPresentation.Amount(totals.Consumption)));
        _coverage.Text = SWGlobalWarMarketPresentation.Coverage(totals.Coverage);
        _supply.Text = SWGlobalWarMarketPresentation.Amount(totals.Supply);
        _incoming.Text = SWGlobalWarMarketPresentation.Amount(totals.Incoming);
        _uncovered.Text = SWGlobalWarMarketPresentation.Amount(totals.Uncovered);
        _uncovered.FontColorOverride = totals.Uncovered > 0 ? SWGlobalWarConsoleStyle.Bad : Color.White;
        if (_demand != null)
            _demand.Text = SWGlobalWarMarketPresentation.Amount(totals.Demand);
    }

    private BoxContainer Row()
    {
        var row = new BoxContainer { SeparationOverride = 4, HorizontalExpand = true };
        AddChild(row);
        return row;
    }

    private static Label Tile(BoxContainer row, string key, int size, string? icon = null)
    {
        var tile = new SWGlobalWarStatTile(Loc.GetString(key), size, icon);
        row.AddChild(tile);
        return tile.Value;
    }
}
