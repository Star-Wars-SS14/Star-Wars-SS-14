using System.Numerics;
using Content.Client._SW.BattlePass;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.UserInterface.Hud;

/// <summary>
/// The top bar's buttons as the keys of the mocks "17 HUD: пульт" and "18 HUD: лёгкий пульт": a raised key with a light
/// top-left edge, the help key in amber, the season key in teal, a 24 px icon over an 8 pt label. Only their look changes:
/// the buttons, their order and what they open stay the game's.
/// </summary>
public sealed class SWHudKeys
{
    private const string Path = "/Textures/_SW/Interface/Terminal/Hud/";
    private const float IconSize = 24;
    private const int Separation = 4;
    private static readonly Color LabelColor = Color.FromHex("#DDE2EC");

    private readonly Dictionary<string, StyleBoxTexture> _boxes = new();
    private Font? _font;
    private Font? _seasonFont;

    private StyleBoxTexture Box(string look, string kind, string state)
    {
        var name = $"{look}_{kind}{state}";
        if (_boxes.TryGetValue(name, out var box))
            return box;

        box = new StyleBoxTexture { Texture = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(Path + name + ".png").Texture };
        box.SetPatchMargin(StyleBox.Margin.All, 4);
        return _boxes[name] = box;
    }

    public void Apply(BoxContainer bar, bool console)
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _font ??= cache.GetFont(SWTerminalStyle.BoldFont, 8);
        _seasonFont ??= cache.GetFont(SWTerminalStyle.BoldFont, 10);
        var look = console ? "console" : "light";
        var size = console ? new Vector2(46, 60) : new Vector2(48, 56);
        var wide = console ? new Vector2(72, 60) : new Vector2(84, 56);
        bar.SeparationOverride = Separation;

        foreach (var child in bar.Children)
        {
            if (child is not BaseButton { Visible: true } button)
                continue;

            var season = button is SWBattlePassTopButton;
            var kind = season ? "key_season" : child.Name == "AHelpButton" ? "key_amber" : "key";
            var state = button.Pressed || button.DrawMode == BaseButton.DrawModeEnum.Pressed ? "_pressed"
                : button.DrawMode == BaseButton.DrawModeEnum.Hover ? "_hover" : string.Empty;
            if (button is ContainerButton container)
                container.StyleBoxOverride = Box(look, kind, state);
            button.SetSize = season ? wide : size;
            button.MinSize = Vector2.Zero;
            button.HorizontalExpand = false;
            button.Margin = new Thickness(0);

            if (child is MenuButton menu)
                Key(menu.ButtonRoot);
            else if (season)
                Season(button);
            else if (button.ChildCount > 0 && button.GetChild(0) is BoxContainer content)
                Key(content);
        }
    }

    /// <summary>An icon of 24 over its key's name, as in the mock.</summary>
    private void Key(BoxContainer content)
    {
        foreach (var part in content.Children)
        {
            switch (part)
            {
                case TextureRect { Texture: { } texture } icon:
                    icon.TextureScale = new Vector2(IconSize / Math.Max(texture.Width, texture.Height));
                    icon.Margin = new Thickness(0, 6, 0, 0);
                    break;
                case Label label:
                    label.FontOverride = _font;
                    label.FontColorOverride = LabelColor;
                    label.Margin = new Thickness(0, 0, 0, 4);
                    break;
            }
        }
    }

    /// <summary>The season key carries only its word in the mock.</summary>
    private void Season(BaseButton button)
    {
        foreach (var part in button.Children)
        {
            if (part is not BoxContainer content)
                continue;
            foreach (var piece in content.Children)
            {
                if (piece is TextureRect icon)
                    icon.Visible = false;
                else if (piece is Label label)
                {
                    label.FontOverride = _seasonFont;
                    label.FontColorOverride = Color.White;
                    label.VerticalExpand = true;
                    label.VAlign = Label.VAlignMode.Center;
                }
            }
        }
    }
}
