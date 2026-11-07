using System.Linq;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.UserInterface.Hud;

/// <summary>
/// Each hand as the mock's one card: the hand's slot and its item status together, dark when idle, in the side's colour
/// with a bright frame when it is the active hand. The cards are the mock's own pictures, stretched as nine patches.
/// The game's own frames of the slot and the status are hidden, nothing else about them changes.
/// </summary>
public sealed class SWHudHands
{
    private const string Path = "/Textures/_SW/Interface/Terminal/Hud/";
    private const float Pad = 2;

    private readonly Dictionary<string, StyleBoxTexture> _cards = new();
    private readonly Dictionary<string, Texture> _icons = new();

    private StyleBoxTexture Card(string name)
    {
        if (_cards.TryGetValue(name, out var box))
            return box;
        box = new StyleBoxTexture { Texture = Load(name) };
        box.SetPatchMargin(StyleBox.Margin.All, 6);
        return _cards[name] = box;
    }

    private Texture Load(string name) =>
        _icons.TryGetValue(name, out var texture)
            ? texture
            : _icons[name] = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(Path + name + ".png").Texture;

    /// <summary>Hides the game's own frames, every frame, since the game sets them again as hands change.</summary>
    public void Prepare(HotbarGui hotbar)
    {
        foreach (var panel in Panels(hotbar))
        {
            // Its frame and its highlight are its two panels; the text sits in a box beside them.
            foreach (var frame in panel.Children.OfType<PanelContainer>())
                frame.Modulate = Color.Transparent;
        }

        foreach (var hand in hotbar.HandContainer.GetButtons())
        {
            // The highlight stays visible, it tells which hand is active; only its picture goes.
            hand.HighlightRect.Modulate = Color.Transparent;
            var empty = hand.Entity == null;
            // SlotControl handles input on ButtonRect, including for occupied hands.
            // Keep it visible for hit testing and hide only its picture.
            hand.ButtonRect.Visible = true;
            hand.ButtonRect.Modulate = empty ? Color.White.WithAlpha(.5f) : Color.Transparent;
            if (empty)
            {
                var icon = Load(hand.HandLocation == Content.Shared.Hands.Components.HandLocation.Left ? "hand_l" : "hand_r");
                if (hand.ButtonRect.Texture != icon)
                    hand.ButtonRect.Texture = icon;
                // At its own 32 px in the middle of the slot, as in the mock, not stretched over it.
                hand.ButtonRect.Stretch = TextureRect.StretchMode.KeepCentered;
            }
        }
    }

    /// <summary>Draws a card behind each hand and the status beside it.</summary>
    public void Draw(DrawingHandleScreen handle, Control decor, HotbarGui hotbar, bool console, SWGlobalWarFaction side)
    {
        var look = console ? "console" : "light";
        var hands = hotbar.HandContainer.GetButtons().Where(h => h.VisibleInTree).OrderBy(h => h.GlobalPixelPosition.X).ToList();
        var panels = Panels(hotbar).Where(p => p.VisibleInTree).ToList();
        foreach (var hand in hands)
        {
            var box = Rect(decor, hand);
            // The status panel nearest to this hand is its own: right hand on the left, left hand on the right.
            var panel = panels.OrderBy(p => MathF.Abs(Rect(decor, p).Center.X - box.Center.X)).FirstOrDefault();
            if (panel != null)
                box = Union(box, Rect(decor, panel));
            var pad = Pad * decor.UIScale;
            box = new UIBox2(box.Left - pad, box.Top - pad, box.Right + pad, box.Bottom + pad);
            var active = hand.HighlightRect.Visible;
            var name = !active ? $"{look}_hand"
                : side == SWGlobalWarFaction.Empire ? $"{look}_hand_on_E" : $"{look}_hand_on_R";
            Card(name).Draw(handle, Snap(box), decor.UIScale);
        }
    }

    private static IEnumerable<ItemStatusPanel> Panels(Control parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is ItemStatusPanel panel)
                yield return panel;
            else
            {
                foreach (var inner in Panels(child))
                    yield return inner;
            }
        }
    }

    private static UIBox2 Rect(Control decor, Control control) =>
        UIBox2.FromDimensions(control.GlobalPixelPosition - decor.GlobalPixelPosition, control.PixelSize);

    private static UIBox2 Union(UIBox2 a, UIBox2 b) =>
        new(MathF.Min(a.Left, b.Left), MathF.Min(a.Top, b.Top), MathF.Max(a.Right, b.Right), MathF.Max(a.Bottom, b.Bottom));

    private static UIBox2 Snap(UIBox2 box) =>
        new(MathF.Round(box.Left), MathF.Round(box.Top), MathF.Round(box.Right), MathF.Round(box.Bottom));
}
