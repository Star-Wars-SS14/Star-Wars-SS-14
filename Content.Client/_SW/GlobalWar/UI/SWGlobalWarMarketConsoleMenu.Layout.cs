using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    /// <summary>The table needs no chart, so the window shrinks to what the mock draws.</summary>
    private static readonly Vector2 TableSize = new(1180, 640);

    /// <summary>Under this width the strip under the map has no room for its own name.</summary>
    private const float WideTitleWidth = 1400;

    /// <summary>Under this width the table drops its four least needed columns and the figures go under the planet card.</summary>
    private const float NarrowTableWidth = 1150;

    private bool? _marketShowMap;
    private bool _marketLayoutDirty = true;
    private bool _marketLayoutSettle;

    private void UpdateMarketLayout()
    {
        if (_marketLayoutSettle)
        {
            // Sizes changed from a frame update leave the panel's stack with its old arrangement until it is asked again.
            _marketLayoutSettle = false;
            DetailsPanel.GetChild(0).InvalidateMeasure();
        }

        if (!_marketLayoutDirty)
            return;
        _marketLayoutDirty = false;
        RefreshMarketLayout();
        _marketLayoutSettle = true;
    }

    private void InitializeMarketLayout()
    {
        // Visibility changes during Arrange leave the outer scroll with the previous child extent.
        OnResized += () => _marketLayoutDirty = true;
        MapToggle.OnPressed += _ =>
        {
            _marketShowMap = !MarketLayout.ShowMap;
            if (_marketShowMap == true)
                UseChartSize();
            else
                SetSize = TableSize;
            // The window changes size at the toggle, and stays in the middle of the screen.
            if (Parent != null)
                LayoutContainer.SetPosition(this, (Parent.Size - SetSize) / 2);
            RefreshMarketLayout();
            _marketLayoutSettle = true;
            WindowScroll.SetScrollValue(default);
        };
    }

    private void RefreshMarketLayout()
    {
        if (Size.X <= 0 || Size.Y <= 0)
            return;
        var compact = Size.X < 1100 || Size.Y < 620;
        var showMap = _marketShowMap ?? !compact;
        MarketLayout.ShowMap = showMap;
        CornerBrackets = showMap;
        PlanetCard.SetWidth = showMap ? float.NaN : 296;
        PlanetCard.SetHeight = showMap ? 112 : 76;
        PlanetCard.HorizontalExpand = showMap;
        MapTitle.Visible = Size.X >= WideTitleWidth;
        _mapStats.SetNarrow(Size.X < WideTitleWidth);
        WideStatsSlot.Visible = !showMap;
        MapStatsSlot.Visible = showMap;
        GoodsHeaderSlot.Visible = !showMap;
        var narrow = Size.X < NarrowTableWidth;
        MarketHead.Orientation = narrow && !showMap ? BoxContainer.LayoutOrientation.Vertical : BoxContainer.LayoutOrientation.Horizontal;
        if (narrow && !showMap)
            PlanetCard.SetWidth = float.NaN;
        foreach (var row in _goodsRows.Values)
        {
            row.SetShape(!showMap);
            row.SetNarrow(narrow);
        }
        foreach (var header in GoodsHeaderSlot.Children)
            SWGlobalWarMarketGoodRow.SetHeaderNarrow(header, narrow);
        RefreshDetails();
        MapToggle.Text = Loc.GetString(showMap ? "sw-global-war-market-collapse-map" : "sw-global-war-market-expand-map");
        // The outer scroll is a fallback; the active operation keeps its own bounded scroll and visible tabs.
        MarketLayout.MaxHeight = Math.Max(220, Size.Y - 96);
    }

}
