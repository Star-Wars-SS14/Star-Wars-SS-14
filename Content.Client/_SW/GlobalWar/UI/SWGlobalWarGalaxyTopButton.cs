using System.Numerics;
using Content.Client._SW.UserInterface;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>The "Map" button in the top bar: the same form as the battle pass beside it, with a planet for an icon.</summary>
public sealed class SWGlobalWarGalaxyTopButton : ContainerButton
{
    public SWGlobalWarGalaxyTopButton()
    {
        Margin = new Thickness(7, 0, 0, 0);
        MinSize = new Vector2(42, 48);
        HorizontalExpand = true;
        ToolTip = Loc.GetString("sw-global-war-galaxy-title");
        StyleBoxOverride = SWTerminalStyle.Box(SWTerminalStyle.ButtonFill, SWTerminalStyle.CardBorder, 1);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
        };
        box.AddChild(new TextureRect
        {
            // The chart sprite keeps a wide margin of space around the disc: cut the disc out.
            Texture = Disc(SWGlobalWarMapControl.PlanetTexture("SWGlobalWarN0")),
            HorizontalAlignment = HAlignment.Center,
            VerticalExpand = true,
            SetSize = new Vector2(24, 24),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        });
        box.AddChild(new Label
        {
            Text = Loc.GetString("sw-global-war-galaxy-button"),
            HorizontalAlignment = HAlignment.Center,
            FontOverride = IoCManager.Resolve<IResourceCache>().GetFont(SWTerminalStyle.BoldFont, 11),
            FontColorOverride = SWTerminalStyle.TextBright,
        });
        AddChild(box);
    }

    /// <summary>The middle 32 pixels of a 64-pixel chart sprite. An RSI frame already sits in an atlas, so cut from that.</summary>
    private static Texture Disc(Texture sprite)
    {
        var region = UIBox2.FromDimensions(16, 16, 32, 32);
        return sprite is AtlasTexture atlas
            ? new AtlasTexture(atlas.SourceTexture, region.Translated(atlas.SubRegion.TopLeft))
            : new AtlasTexture(sprite, region);
    }
}
