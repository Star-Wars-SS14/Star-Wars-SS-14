using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._SW.UserInterface;

/// <summary>
/// The look of SW console windows: near-black panels, hairline borders, one monospaced font, a faction accent.
/// <see cref="SWTerminalStyleUIController"/> puts these rules last, because they tie with the global ones.
/// </summary>
public static class SWTerminalStyle
{
    public const string Window = "SWTermWindow";
    public const string Card = "SWTermCard";
    public const string Divider = "SWTermDivider";
    public const string SectionDivider = "SWTermSectionDivider";
    public const string AccentLine = "SWTermAccent";
    public const string Chip = "SWTermChip";
    public const string ChipMoney = "SWTermChipMoney";
    public const string ChipScore = "SWTermChipScore";
    public const string Frame = "SWTermFrame";
    public const string DoneFrame = "SWTermDoneFrame";
    public const string Close = "SWTermClose";
    public const string Title = "SWTermTitle";
    public const string Heading = "SWTermHeading";
    public const string Text = "SWTermText";
    public const string TextDone = "SWTermTextDone";
    public const string Value = "SWTermValue";
    public const string Money = "SWTermMoney";
    public const string Price = "SWTermPrice";
    public const string Button = "SWTermButton";
    public const string MainButton = "SWTermMainButton";
    public const string Option = "SWTermOption";
    public const string OptionItem = "SWTermOptionItem";
    public const string Field = "SWTermField";
    public const string BarFrame = "SWTermBarFrame";
    public const string BarDone = "SWTermBarDone";
    public const string BarOpen = "SWTermBarOpen";
    public const string NeutralAccent = "Neutral";
    public const string MoneyAccent = "Money";

    /// <summary>
    /// Gold means money in every window, so that a reward or a balance reads the same everywhere.
    /// </summary>
    private static readonly Color MoneyColor = Color.FromHex("#D9A441");

    /// <summary>
    /// The Mandalorian accent is gold too, and their score must not read as money.
    /// </summary>
    private static readonly Color BeskarFill = Color.FromHex("#2F5F7A");
    private static readonly Color BeskarBorder = Color.FromHex("#4C86A8");

    /// <summary>
    /// The colours of each faction: accent line and bars, then the main button's fill, hover and border.
    /// </summary>
    private static readonly (string Name, Color Accent, Color Fill, Color Hover, Color Border)[] Accents =
    {
        ("Empire", Color.FromHex("#E01D1E"), Color.FromHex("#8B141A"), Color.FromHex("#A81A21"), Color.FromHex("#BB272C")),
        ("Republic", Color.FromHex("#3E9BE0"), Color.FromHex("#1C5A8F"), Color.FromHex("#236DAD"), Color.FromHex("#2F80C4")),
        ("Mandalorian", Color.FromHex("#D9A441"), Color.FromHex("#7A5A17"), Color.FromHex("#936D1C"), Color.FromHex("#B0862C")),
        ("BlackSun", Color.FromHex("#9B5DE5"), Color.FromHex("#5B2A9D"), Color.FromHex("#6E36B8"), Color.FromHex("#8250CC")),
        ("Exchange", Color.FromHex("#2BBBB3"), Color.FromHex("#0F6B68"), Color.FromHex("#13807C"), Color.FromHex("#22A39C")),
        (NeutralAccent, Color.FromHex("#8E97A8"), Color.FromHex("#3A3F4A"), Color.FromHex("#474D5A"), Color.FromHex("#5D6476")),
        (MoneyAccent, MoneyColor, Color.FromHex("#7A5A17"), Color.FromHex("#936D1C"), Color.FromHex("#B0862C")),
    };

    /// <summary>
    /// The line colour of an accent.
    /// </summary>
    public static Color AccentColor(string accent)
    {
        foreach (var (name, color, _, _, _) in Accents)
        {
            if (name == accent)
                return color;
        }

        return SectionDividerColor;
    }

    /// <summary>
    /// Picks the accent for a faction prototype ID. Anything else, or no faction, is neutral.
    /// </summary>
    public static string AccentOf(string? faction)
    {
        return faction switch
        {
            "EmpireFaction" => "Empire",
            "RepublicFaction" => "Republic",
            "MandalorianFaction" => "Mandalorian",
            "BlackSunFaction" => "BlackSun",
            "ExchangeFaction" => "Exchange",
            _ => NeutralAccent,
        };
    }

    public const string RegularFont = "/Fonts/_SW/JetBrainsMono/JetBrainsMono-Regular.ttf";
    public const string BoldFont = "/Fonts/_SW/JetBrainsMono/JetBrainsMono-Bold.ttf";
    private const string CrossTexture = "/Textures/Interface/Nano/cross.svg.png";

    public static readonly Color Background = Color.FromHex("#0C0E0F");
    public static readonly Color WindowBorder = Color.FromHex("#7C8493");
    public static readonly Color CardBorder = Color.FromHex("#4F5666");
    private static readonly Color ChipBorder = Color.FromHex("#697387");
    public static readonly Color DividerColor = Color.FromHex("#25282E");
    public static readonly Color SectionDividerColor = Color.FromHex("#5D6476");
    private static readonly Color Green = Color.FromHex("#5CB353");
    public static readonly Color ButtonFill = Color.FromHex("#272A30");
    public static readonly Color ButtonHover = Color.FromHex("#343842");
    public static readonly Color ButtonBorder = Color.FromHex("#5D6476");
    public static readonly Color FieldFill = Color.FromHex("#1B1D23");
    public static readonly Color Disabled = Color.FromHex("#16181C");
    public static readonly Color TextBright = Color.FromHex("#E1E1EF");
    private static readonly Color TextDim = Color.FromHex("#AEB3BF");
    private static readonly Color TextDoneColor = Color.FromHex("#8A91A0");
    private static readonly Color TextOff = Color.FromHex("#5D6476");

    public static StyleRule[] GetRules(IResourceCache res)
    {
        // Sizes measured on the reference at 1280x720, where the game draws the UI at 0.9.
        var regular = res.GetFont(RegularFont, 12);
        var small = res.GetFont(RegularFont, 11);
        var bold = res.GetFont(BoldFont, 13);
        var chip = res.GetFont(BoldFont, 11);
        var heading = res.GetFont(BoldFont, 14);
        var main = res.GetFont(BoldFont, 17);
        var title = res.GetFont(BoldFont, 20);

        var rules = new List<StyleRule>();
        foreach (var (name, accent, fill, hover, border) in Accents)
        {
            var mainClass = MainButton + name;
            var scoreFill = name == "Mandalorian" ? BeskarFill : fill;
            var scoreBorder = name == "Mandalorian" ? BeskarBorder : border;
            rules.AddRange(new StyleRule[]
            {
                E<PanelContainer>().Class(AccentLine + name).Panel(Fill(accent)),
                E<PanelContainer>().Class(ChipScore + name).Panel(Box(scoreFill, scoreBorder, 1, 6, 2)),
                E<ProgressBar>().Class(BarOpen + name)
                    .Prop(ProgressBar.StylePropertyBackground, new StyleBoxEmpty())
                    .Prop(ProgressBar.StylePropertyForeground, Fill(accent)),
                E<ContainerButton>().Class(mainClass).PseudoNormal().Modulate(Color.White).Box(Box(fill, border, 1, 8, 8)),
                E<ContainerButton>().Class(mainClass).PseudoHovered().Modulate(Color.White).Box(Box(hover, border, 1, 8, 8)),
                E<ContainerButton>().Class(mainClass).PseudoPressed().Modulate(Color.White).Box(Box(fill, border, 1, 8, 8)),
                E<ContainerButton>().Class(mainClass).PseudoDisabled().Modulate(Color.White).Box(Box(Disabled, DividerColor, 1, 8, 8)),
                E<ContainerButton>().Class(mainClass).ParentOf(E<Label>()).Font(main).FontColor(Color.White).AlignMode(Label.AlignMode.Center),
                E<ContainerButton>().Class(mainClass).PseudoDisabled().ParentOf(E<Label>()).FontColor(TextOff),
            });
        }

        rules.AddRange(new StyleRule[]
        {
            E<PanelContainer>().Class(Window).Panel(Box(Background, WindowBorder, 1)),
            E<PanelContainer>().Class(Card).Panel(Box(Background, CardBorder, 1)),
            E<PanelContainer>().Class(ChipMoney).Panel(Box(Color.FromHex("#7A5A17"), Color.FromHex("#B0862C"), 1, 6, 2)),
            E<PanelContainer>().Class(Frame).Panel(Box(Background, ChipBorder, 1)),
            E<PanelContainer>().Class(DoneFrame).Panel(Box(Background, Green, 1)),
            E<PanelContainer>().Class(Divider).Panel(Fill(DividerColor)),
            E<PanelContainer>().Class(SectionDivider).Panel(Fill(SectionDividerColor)),

            E<TextureButton>().Class(Close).Prop(TextureButton.StylePropertyTexture, res.GetTexture(CrossTexture)),
            E<TextureButton>().Class(Close).PseudoNormal().Modulate(TextBright),
            E<TextureButton>().Class(Close).PseudoHovered().Modulate(Color.White),
            E<TextureButton>().Class(Close).PseudoPressed().Modulate(TextDim),

            E<Label>().Class(Title).Font(title).FontColor(TextBright),
            E<Label>().Class(Heading).Font(heading).FontColor(TextBright),
            E<Label>().Class(Text).Font(regular).FontColor(TextDim),
            E<RichTextLabel>().Class(Text).Font(regular).FontColor(TextDim),
            E<RichTextLabel>().Class(TextDone).Font(regular).FontColor(TextDoneColor),
            E<Label>().Class(Value).Font(bold).FontColor(TextBright),
            E<Label>().Class(Money).Font(bold).FontColor(MoneyColor),
            E<Label>().Class(Price).Font(res.GetFont(BoldFont, 12)).FontColor(MoneyColor),
            E<PanelContainer>().Class(Chip).ParentOf(E<Label>()).Font(chip).FontColor(Color.White),

            E<ContainerButton>().Class(Button).PseudoNormal().Modulate(Color.White).Box(Box(ButtonFill, ButtonBorder, 1, 8, 7)),
            E<ContainerButton>().Class(Button).PseudoHovered().Modulate(Color.White).Box(Box(ButtonHover, ButtonBorder, 1, 8, 7)),
            E<ContainerButton>().Class(Button).PseudoPressed().Modulate(Color.White).Box(Box(FieldFill, ButtonBorder, 1, 8, 7)),
            E<ContainerButton>().Class(Button).PseudoDisabled().Modulate(Color.White).Box(Box(Disabled, DividerColor, 1, 8, 7)),
            E<ContainerButton>().Class(Button).ParentOf(E<Label>()).Font(regular).FontColor(TextBright).AlignMode(Label.AlignMode.Center),

            E<OptionButton>().Class(Option).PseudoNormal().Modulate(Color.White).Box(Box(FieldFill, ButtonBorder, 1, 6, 7)),
            E<OptionButton>().Class(Option).PseudoHovered().Modulate(Color.White).Box(Box(ButtonFill, ButtonBorder, 1, 6, 7)),
            E<OptionButton>().Class(Option).PseudoPressed().Modulate(Color.White).Box(Box(ButtonFill, ButtonBorder, 1, 6, 7)),
            E<OptionButton>().Class(Option).ParentOf(E<BoxContainer>()).ParentOf(E<Label>()).Font(small).FontColor(TextBright).AlignMode(Label.AlignMode.Left),

            E<ContainerButton>().Class(OptionItem).PseudoNormal().Modulate(Color.White).Box(Box(FieldFill, ButtonBorder, 1, 6, 5)),
            E<ContainerButton>().Class(OptionItem).PseudoHovered().Modulate(Color.White).Box(Box(ButtonHover, ButtonBorder, 1, 6, 5)),
            E<ContainerButton>().Class(OptionItem).PseudoPressed().Modulate(Color.White).Box(Box(ButtonFill, ButtonBorder, 1, 6, 5)),
            E<ContainerButton>().Class(OptionItem).ParentOf(E<Label>()).Font(small).FontColor(TextBright).AlignMode(Label.AlignMode.Left),

            E<LineEdit>().Class(Field).Box(Box(FieldFill, ButtonBorder, 1, 6, 7)).Font(small).FontColor(TextBright),

            E<PanelContainer>().Class(BarFrame).Panel(Box(Background, CardBorder, 1)),
            E<ProgressBar>().Class(BarDone)
                .Prop(ProgressBar.StylePropertyBackground, new StyleBoxEmpty())
                .Prop(ProgressBar.StylePropertyForeground, Fill(Green)),
        });

        return rules.ToArray();
    }

    public static StyleBoxFlat Fill(Color color)
    {
        return new StyleBoxFlat { BackgroundColor = color };
    }

    public static StyleBoxFlat Box(Color fill, Color border, float thickness, float marginX = 0, float marginY = 0)
    {
        return Box(fill, border, thickness, marginX, marginX, marginY);
    }

    public static StyleBoxFlat Box(Color fill, Color border, float thickness, float left, float right, float vertical)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = fill,
            BorderColor = border,
            BorderThickness = new Thickness(thickness),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Left, left);
        box.SetContentMarginOverride(StyleBox.Margin.Right, right);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        return box;
    }
}
