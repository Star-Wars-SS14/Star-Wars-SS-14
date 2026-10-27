using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Senate atlas layout: a dominant map and a bounded sidebar, stacked on narrow windows.</summary>
public sealed class SWGlobalWarAtlasLayout : Container
{
    private const float Gap = 16;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) ? availableSize.X : 1000;
        var height = float.IsFinite(availableSize.Y) ? availableSize.Y : 500;
        LayoutChildren(new Vector2(width, height), false);
        return new Vector2(width, Math.Max(220, height));
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
        // Short windows retain columns so the map cannot squeeze the jump button out of the sidebar.
        var stacked = size.X < 740 && size.Y >= 440;
        var detailWidth = Math.Clamp(size.X * .34f, 280, 360);
        var mapSize = stacked ? new Vector2(size.X, Math.Max(250, size.Y * .52f))
            : new Vector2(Math.Max(0, size.X - detailWidth - Gap), size.Y);
        var detailSize = stacked ? new Vector2(size.X, Math.Max(0, size.Y - mapSize.Y - Gap))
            : new Vector2(detailWidth, size.Y);
        map.Measure(mapSize);
        details.Measure(detailSize);
        if (!arrange)
            return;

        map.Arrange(UIBox2.FromDimensions(Vector2.Zero, mapSize));
        details.Arrange(UIBox2.FromDimensions(stacked ? new Vector2(0, mapSize.Y + Gap)
            : new Vector2(mapSize.X + Gap, 0), detailSize));
    }
}
