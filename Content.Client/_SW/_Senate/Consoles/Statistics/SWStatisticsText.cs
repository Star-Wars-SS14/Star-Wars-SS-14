using Content.Client._SW._Senate;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Readable body text for this console without changing shared Senate styles.</summary>
public static class SWStatisticsText
{
    private static Font? _body;
    public static Font Body => _body ??= new VectorFont(
        IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 14);

    public static RichTextLabel Wrap(string text, Color color, float fallbackWidth = 660)
    {
        var label = SWConsoleStyle.Wrap(text, color, fallbackWidth);
        label.Stylesheet = new Stylesheet(new StyleRule[] { StylesheetHelpers.Element<RichTextLabel>().Prop("font", Body) });
        return label;
    }
}
