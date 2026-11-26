using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Market workspace that can devote its full width to the selected operation without losing the map.</summary>
public sealed class SWGlobalWarMarketAtlasLayout : Container
{
    private bool _showMap = true;
    public bool ShowMap
    {
        get => _showMap;
        set
        {
            if (_showMap == value)
                return;
            _showMap = value;
            if (ChildCount == 2)
                GetChild(0).Visible = value;
            InvalidateMeasure();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var size = new Vector2(float.IsFinite(availableSize.X) ? availableSize.X : 1000,
            float.IsFinite(availableSize.Y) ? availableSize.Y : 500);
        LayoutChildren(size, false);
        return size;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        LayoutChildren(finalSize, true);
        return finalSize;
    }

    private void LayoutChildren(Vector2 size, bool arrange)
    {
        if (ChildCount != 2)
            return;
        var map = GetChild(0);
        var details = GetChild(1);
        if (!_showMap)
        {
            details.Measure(size);
            if (arrange)
                details.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            return;
        }
        var detailWidth = Math.Clamp(size.X * .27f, 280, 358);
        var mapSize = new Vector2(Math.Max(0, size.X - detailWidth - 8), size.Y);
        var detailSize = new Vector2(detailWidth, size.Y);
        map.Measure(mapSize);
        details.Measure(detailSize);
        if (!arrange)
            return;
        map.Arrange(UIBox2.FromDimensions(Vector2.Zero, mapSize));
        details.Arrange(UIBox2.FromDimensions(new Vector2(mapSize.X + 8, 0), detailSize));
    }
}
