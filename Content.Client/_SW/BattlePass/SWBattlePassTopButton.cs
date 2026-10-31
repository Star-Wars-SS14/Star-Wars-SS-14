using System.Numerics;
using Content.Client._SW.UserInterface;
using Content.Client.Resources;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._SW.BattlePass;

/// <summary>
/// The in-game button, right after F1. The same form as its neighbours, with the only coloured icon and frame in the bar.
/// The frame thickens and a counter appears when a reward waits. It never blinks: a fight goes on behind it.
/// </summary>
public sealed class SWBattlePassTopButton : ContainerButton
{
    private static readonly Color Ice = Color.FromHex("#9BE7FF");
    private static readonly Color BadgeFill = Color.FromHex("#5CB353");
    private static readonly Color BadgeText = Color.FromHex("#0C0E0F");
    private static readonly SpriteSpecifier.Rsi Icon = new(new ResPath(SWBattlePassWindow.IconRsi), "snowflake");

    private readonly PanelContainer _badge;
    private readonly Label _badgeText;

    public SWBattlePassTopButton()
    {
        // The bar's gap is 5: this makes 12, which sets it apart from the service buttons.
        Margin = new Thickness(7, 0, 0, 0);
        MinSize = new Vector2(42, 48);
        HorizontalExpand = true;
        ToolTip = Loc.GetString("sw-battlepass-button-tooltip");
        StyleBoxOverride = SWTerminalStyle.Box(SWTerminalStyle.ButtonFill, Ice, 1);

        var font = IoCManager.Resolve<IResourceCache>().GetFont(SWTerminalStyle.BoldFont, 11);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
        };
        box.AddChild(new TextureRect
        {
            Texture = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>().Frame0(Icon),
            HorizontalAlignment = HAlignment.Center,
            VerticalExpand = true,
            Stretch = TextureRect.StretchMode.KeepCentered,
            ModulateSelfOverride = Ice,
        });
        box.AddChild(new Label
        {
            Text = Loc.GetString("sw-battlepass-button"),
            HorizontalAlignment = HAlignment.Center,
            FontOverride = font,
            FontColorOverride = Ice,
        });
        AddChild(box);

        _badgeText = new Label
        {
            FontOverride = font,
            FontColorOverride = BadgeText,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        _badge = new PanelContainer
        {
            PanelOverride = SWTerminalStyle.Fill(BadgeFill),
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Top,
            MinSize = new Vector2(16, 16),
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _badge.AddChild(_badgeText);
        AddChild(_badge);
    }

    /// <summary>
    /// Shows how many rewards can be claimed.
    /// </summary>
    public void SetClaimable(int count)
    {
        _badge.Visible = count > 0;
        _badgeText.Text = count.ToString();
        StyleBoxOverride = SWTerminalStyle.Box(SWTerminalStyle.ButtonFill, Ice, count > 0 ? 2 : 1);
    }
}
