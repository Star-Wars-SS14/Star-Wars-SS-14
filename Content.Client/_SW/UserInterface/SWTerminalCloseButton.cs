using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.UserInterface;

/// <summary>
/// The cross of a terminal window. It fills its frame and draws itself in whole pixels: a scaled texture landed on a
/// rounded rectangle and stood a pixel off the middle at most UI scales.
/// </summary>
public sealed class SWTerminalCloseButton : BaseButton
{
    private const float CrossSize = 10;
    private static readonly Color Hover = Color.FromHex("#FF6A5A");

    public SWTerminalCloseButton()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var color = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed ? Hover : SWTerminalStyle.TextBright;
        var blockX = Block(PixelWidth);
        var blockY = Block(PixelHeight);

        // The block's size has the parity of the frame, so that it stands exactly in the middle.
        var left = (PixelWidth - blockX) / 2;
        var top = (PixelHeight - blockY) / 2;
        var arm = Math.Max(1, (int) MathF.Round((CrossSize * UIScale / blockX - 1) / 2));
        for (var i = -arm; i <= arm; i++)
        {
            var x = left + i * blockX;
            Fill(handle, x, top + i * blockY, blockX, blockY, color);
            Fill(handle, x, top - i * blockY, blockX, blockY, color);
        }
    }

    private int Block(int frame)
    {
        var block = Math.Max(1, (int) MathF.Round(2 * UIScale));
        return (frame - block) % 2 == 0 ? block : block + 1;
    }

    private static void Fill(DrawingHandleScreen handle, int x, int y, int width, int height, Color color)
    {
        handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y), new Vector2(width, height)), color);
    }
}
