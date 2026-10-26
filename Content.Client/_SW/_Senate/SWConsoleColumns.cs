using System;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate;

/// <summary>Reserves the secondary column before measuring wrapped document text.</summary>
public sealed class SWConsoleColumns : Container
{
    public Control First { get; }
    public Control Second { get; }
    public float FixedWidth { get; }
    public float Breakpoint { get; }
    public bool FixedFirst { get; }
    private const float Gap = 20;

    public SWConsoleColumns(Control first, Control second, float fixedWidth = 210, float breakpoint = 650, bool fixedFirst = false)
    {
        First = first;
        Second = second;
        FixedWidth = fixedWidth;
        Breakpoint = breakpoint;
        FixedFirst = fixedFirst;
        HorizontalExpand = true;
        AddChild(first);
        AddChild(second);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) ? availableSize.X : Breakpoint;
        if (width < Breakpoint)
        {
            First.Measure(new Vector2(width, float.PositiveInfinity));
            Second.Measure(new Vector2(width, float.PositiveInfinity));
            return new Vector2(Math.Max(First.DesiredSize.X, Second.DesiredSize.X),
                First.DesiredSize.Y + Gap + Second.DesiredSize.Y);
        }

        var flexibleWidth = Math.Max(0, width - FixedWidth - Gap);
        First.Measure(new Vector2(FixedFirst ? FixedWidth : flexibleWidth, availableSize.Y));
        Second.Measure(new Vector2(FixedFirst ? flexibleWidth : FixedWidth, availableSize.Y));
        return new Vector2(width, Math.Max(First.DesiredSize.Y, Second.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (finalSize.X < Breakpoint)
        {
            First.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(finalSize.X, First.DesiredSize.Y)));
            Second.Arrange(UIBox2.FromDimensions(new Vector2(0, First.DesiredSize.Y + Gap),
                new Vector2(finalSize.X, Math.Max(0, finalSize.Y - First.DesiredSize.Y - Gap))));
        }
        else
        {
            var left = FixedFirst ? FixedWidth : Math.Max(0, finalSize.X - FixedWidth - Gap);
            First.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(left, finalSize.Y)));
            Second.Arrange(UIBox2.FromDimensions(new Vector2(left + Gap, 0),
                new Vector2(Math.Max(0, finalSize.X - left - Gap), finalSize.Y)));
        }
        return finalSize;
    }
}
