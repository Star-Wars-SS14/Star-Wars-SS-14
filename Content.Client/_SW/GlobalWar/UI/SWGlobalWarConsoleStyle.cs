using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>HoloNet colors, typography and window chrome adapted from the Senate consoles.</summary>
public static class SWGlobalWarConsoleStyle
{
    public static readonly Color BgDark = Color.FromHex("#111D28");
    public static readonly Color BgPanel = Color.FromHex("#172633");
    public static readonly Color Navigation = Color.FromHex("#0B151F");
    public static readonly Color Hover = Color.FromHex("#294657");
    public static readonly Color Selected = Color.FromHex("#203D50");
    public static readonly Color Accent = Color.FromHex("#93D9F3");
    public static readonly Color Brass = Color.FromHex("#D4B57D");
    public static readonly Color TextBright = Color.FromHex("#E2EDF1");
    public static readonly Color TextDim = Color.FromHex("#A0B4C1");
    public static readonly Color Good = Color.FromHex("#A4C5AD");
    public static readonly Color Bad = Color.FromHex("#DFA7A7");
    public static readonly Color PillDim = Color.FromHex("#223442");
    public static readonly Color Edge = Color.FromHex("#314959");

    private static Font? _body;
    private static Font? _strong;
    private static Font? _small;
    private static Font? _title;
    public static Font Body => _body ??= LoadFont("Regular", 12);
    public static Font Strong => _strong ??= LoadFont("Bold", 12);
    public static Font Small => _small ??= LoadFont("Regular", 11);
    public static Font Title => _title ??= LoadFont("Bold", 17);

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>()
            .GetResource<FontResource>($"/Fonts/NotoSans/NotoSans-{variant}.ttf"), size);

    /// <summary>Extends the game stylesheet with HoloNet typography and fits the window to the UI viewport.</summary>
    /// <param name="root">Root panel of the navigation window.</param>
    public static void StyleRoot(PanelContainer root)
    {
        root.PanelOverride = new StyleBoxFlat { BackgroundColor = BgDark };
        var baseRules = IoCManager.Resolve<IUserInterfaceManager>().Stylesheet?.Rules ?? Array.Empty<StyleRule>();
        root.Stylesheet = new Stylesheet(baseRules.Concat(new StyleRule[]
        {
            StylesheetHelpers.Element<Label>().Prop("font", Body).Prop("font-color", TextDim),
            StylesheetHelpers.Element<RichTextLabel>().Prop("font", Body).Prop("font-color", TextBright),
            StylesheetHelpers.Element<RichTextLabel>().Class("GlobalWarPlanetTitle").Prop("font", Title),
            StylesheetHelpers.Element<RichTextLabel>().Class("GlobalWarMapHelp").Prop("font", Small).Prop("font-color", TextDim),
            StylesheetHelpers.Element<LineEdit>().Prop("font", Body).Prop("font-color", TextBright)
                .Prop(LineEdit.StylePropertyStyleBox, new StyleBoxFlat
                {
                    BackgroundColor = Navigation, BorderColor = Edge, BorderThickness = new Thickness(0, 0, 0, 1),
                    ContentMarginLeftOverride = 8, ContentMarginRightOverride = 8,
                    ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6,
                }).Prop(LineEdit.StylePropertyCursorColor, Accent).Prop(LineEdit.StylePropertySelectionColor, Selected),
            StylesheetHelpers.Element<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder).Prop("font-color", TextDim),
        }).ToArray());

        for (Control? parent = root.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is not BaseWindow window)
                continue;

            StyleWindowChrome(window);
            var viewport = IoCManager.Resolve<IUserInterfaceManager>().WindowRoot.Size;
            var preferred = window.SetSize;
            if (viewport.X > 0 && viewport.Y > 0 && float.IsFinite(preferred.X) && float.IsFinite(preferred.Y))
                window.SetSize = Vector2.Min(preferred, Vector2.Max(window.MinSize, viewport - new Vector2(24)));
            break;
        }
    }

    private static void StyleWindowChrome(Control control)
    {
        if (control.Name == "WindowHeader" && control is PanelContainer header)
            header.PanelOverride = new StyleBoxFlat { BackgroundColor = Navigation };
        if (control.Name == "WindowTitle" && control is Label title)
        {
            title.FontOverride = Small;
            title.FontColorOverride = TextDim;
        }
        foreach (var child in control.Children)
            StyleWindowChrome(child);
    }
}
