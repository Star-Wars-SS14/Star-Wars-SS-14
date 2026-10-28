using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A stable inventory card whose values update without replacing controls or resetting the scroll position.</summary>
internal sealed class SWGlobalWarMarketGoodRow : PanelContainer
{
    private readonly Label _stock;
    private readonly Label _consumption;
    private readonly Label _demand;
    private readonly Label _supply;
    private readonly Label _incoming;
    private readonly Label _uncovered;
    private readonly Label _coverage;
    private readonly SWGlobalWarMarketBar _bar = new();

    public SWGlobalWarMarketGoodRow(SWGlobalWarCommodityPrototype commodity)
    {
        HorizontalExpand = true;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = SWGlobalWarConsoleStyle.Navigation,
            BorderColor = SWGlobalWarConsoleStyle.Edge,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(12), SeparationOverride = 6 };
        var header = new BoxContainer { SeparationOverride = 8 };
        var icon = new EntityPrototypeView { SetSize = new Vector2(32), Stretch = SpriteView.StretchMode.Fill };
        icon.SetPrototype(commodity.Entity);
        header.AddChild(icon);
        header.AddChild(new Label
        {
            Text = Loc.GetString(commodity.Name), HorizontalExpand = true, ClipText = true,
            FontOverride = SWGlobalWarConsoleStyle.Strong,
            FontColorOverride = SWGlobalWarMarketPresentation.CommodityColor(commodity.ID),
        });
        body.AddChild(header);
        _stock = Pair(body, "sw-global-war-market-stock");
        body.AddChild(_bar);
        _consumption = Pair(body, "sw-global-war-market-consumption");
        _demand = Pair(body, "sw-global-war-market-demand");
        _supply = Pair(body, "sw-global-war-market-supply");
        _incoming = Pair(body, "sw-global-war-market-incoming");
        _uncovered = Pair(body, "sw-global-war-market-uncovered");
        _coverage = Pair(body, "sw-global-war-market-coverage");
        AddChild(body);
        ToolTip = Loc.GetString("sw-global-war-market-target-hint");
    }

    public void Update(SWGlobalWarMarketTotals totals)
    {
        SetValue(_stock, $"{totals.Stock} / {totals.Target}");
        SetValue(_consumption, Loc.GetString("sw-global-war-market-per-minute", ("value", totals.Consumption)));
        SetValue(_demand, totals.Demand.ToString(), totals.Demand > 0 ? SWGlobalWarConsoleStyle.Bad : SWGlobalWarConsoleStyle.TextDim);
        SetValue(_supply, totals.Supply.ToString(), totals.Supply > 0 ? SWGlobalWarConsoleStyle.Good : SWGlobalWarConsoleStyle.TextDim);
        SetValue(_incoming, totals.Incoming.ToString(), SWGlobalWarConsoleStyle.Accent);
        SetValue(_uncovered, totals.Uncovered.ToString(), totals.Uncovered > 0 ? SWGlobalWarConsoleStyle.Brass : SWGlobalWarConsoleStyle.TextDim);
        SetValue(_coverage, SWGlobalWarMarketPresentation.Coverage(totals.Coverage));
        _bar.Value = totals.Stock;
        _bar.Maximum = totals.Target;
        _bar.Visible = totals.Target > 0;
        _bar.Fill = totals.Demand > 0 ? SWGlobalWarConsoleStyle.Bad : SWGlobalWarConsoleStyle.Good;
    }

    private static void SetValue(Label label, string text, Color? color = null)
    {
        if (label.Text != text)
            label.Text = text;
        if (color is { } tint && label.FontColorOverride != tint)
            label.FontColorOverride = tint;
    }

    private static Label Pair(BoxContainer body, string key)
    {
        var line = new BoxContainer { SeparationOverride = 8, HorizontalExpand = true };
        line.AddChild(new Label { Text = Loc.GetString(key), HorizontalExpand = true, ClipText = true });
        var value = new Label { FontColorOverride = SWGlobalWarConsoleStyle.TextBright };
        line.AddChild(value);
        body.AddChild(line);
        return value;
    }
}
