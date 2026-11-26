using System;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Economy;

/// <summary>The production diagram keeps the main workspace; inspection scrolls independently.</summary>
public sealed class SWProductionWorkspace : Container
{
    public Control Diagram { get; }
    public Control Inspector { get; }
    private const float Gap = 12;
    public SWProductionWorkspace(Control diagram, Control inspector)
    {
        Name = "ProductionWorkspace";
        HorizontalExpand = VerticalExpand = true;
        Diagram = diagram;
        Inspector = inspector;
        AddChild(diagram);
        AddChild(inspector);
    }
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var size = new Vector2(float.IsFinite(availableSize.X) ? availableSize.X : 1000,
            float.IsFinite(availableSize.Y) ? availableSize.Y : 500);
        Layout(size, false);
        // The parent box allocates remaining height through VerticalExpand. Do not consume
        // its whole measurement budget before the footer has been measured.
        return new Vector2(size.X, 0);
    }
    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Layout(finalSize, true);
        return finalSize;
    }
    private void Layout(Vector2 size, bool arrange)
    {
        var stacked = size.X < 920;
        var inspectorWidth = Math.Clamp(size.X * .27f, 260, 300);
        var diagramSize = stacked ? new Vector2(size.X, Math.Max(0, size.Y * .68f - Gap))
            : new Vector2(Math.Max(0, size.X - inspectorWidth - Gap), size.Y);
        var inspectorSize = stacked ? new Vector2(size.X, Math.Max(0, size.Y - diagramSize.Y - Gap))
            : new Vector2(inspectorWidth, size.Y);
        Diagram.Measure(diagramSize);
        Inspector.Measure(inspectorSize);
        if (!arrange) return;
        Diagram.Arrange(UIBox2.FromDimensions(Vector2.Zero, diagramSize));
        Inspector.Arrange(UIBox2.FromDimensions(stacked ? new Vector2(0, diagramSize.Y + Gap)
            : new Vector2(diagramSize.X + Gap, 0), inspectorSize));
    }
}
