using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate;

/// <summary>Multiline document input with the same string binding as the former LineEdit.</summary>
public sealed class SWConsoleTextEdit : BoxContainer
{
    public TextEdit Editor { get; } = new() { HorizontalExpand = true, VerticalExpand = true };
    public string Text
    {
        get => Rope.Collapse(Editor.TextRope);
        set
        {
            Editor.TextRope = new Rope.Leaf(value);
            Editor.CursorPosition = new TextEdit.CursorPos(0, TextEdit.LineBreakBias.Top);
        }
    }

    public string PlaceHolder
    {
        get => Editor.Placeholder == null ? string.Empty : Rope.Collapse(Editor.Placeholder);
        set => Editor.Placeholder = new Rope.Leaf(value);
    }

    public SWConsoleTextEdit()
    {
        MinHeight = 92;
        HorizontalExpand = true;
        var surface = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = SWConsoleStyle.Navigation,
                BorderColor = SWConsoleStyle.Edge,
                BorderThickness = new Thickness(0, 0, 0, 1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            },
        };
        surface.AddChild(Editor);
        AddChild(surface);
    }
}
