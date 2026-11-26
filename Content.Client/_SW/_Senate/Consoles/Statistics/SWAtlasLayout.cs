using System;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Keep the atlas dominant while giving numeric tables a readable, bounded width.</summary>
public sealed class SWAtlasLayout : Container
{
    private readonly Control _map;
    private readonly Control _details;
    private readonly float _stackBelow;
    private readonly float _minDetails;
    private readonly float _maxDetails;
    private const float Gap = 16;
    private readonly float _minMapHeight;

    public SWAtlasLayout(Control map, Control details, float stackBelow = 680, float minDetails = 300, float maxDetails = 410, float minMapHeight = 220)
    {
        _map = map;
        _details = details;
        _stackBelow = stackBelow;
        _minDetails = minDetails;
        _maxDetails = maxDetails;
        _minMapHeight = minMapHeight;
        _map.MinHeight = minMapHeight;
        HorizontalExpand = VerticalExpand = true;
        AddChild(map);
        AddChild(details);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) ? availableSize.X : 1000;
        var height = float.IsFinite(availableSize.Y) ? availableSize.Y : 500;
        MeasureChildren(new Vector2(width, height), false);
        return new Vector2(width, Math.Max(_minMapHeight, height));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        MeasureChildren(finalSize, true);
        return finalSize;
    }

    private void MeasureChildren(Vector2 size, bool arrange)
    {
        var stacked = size.X < _stackBelow;
        var detailWidth = Math.Clamp(size.X * .37f, _minDetails, _maxDetails);
        var mapSize = stacked ? new Vector2(size.X, Math.Max(_minMapHeight, size.Y * .46f))
            : new Vector2(Math.Max(0, size.X - detailWidth - Gap), size.Y);
        var detailSize = stacked ? new Vector2(size.X, Math.Max(0, size.Y - mapSize.Y - Gap))
            : new Vector2(detailWidth, size.Y);
        _map.Measure(mapSize);
        _details.Measure(detailSize);
        if (!arrange) return;
        _map.Arrange(UIBox2.FromDimensions(Vector2.Zero, mapSize));
        _details.Arrange(UIBox2.FromDimensions(stacked ? new Vector2(0, mapSize.Y + Gap)
            : new Vector2(mapSize.X + Gap, 0), detailSize));
    }
}
