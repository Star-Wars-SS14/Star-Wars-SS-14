using System.Numerics;
using Content.Client.ContextMenu.UI;
using Content.Client.Examine;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Palette;
using Content.Client.Stylesheets.Sheetlets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Controls.FancyTree;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Client.UserInterface.Systems.Actions.Windows;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Content.Client._SW.UserInterface.SWTerminalStyle;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._SW.UserInterface;

/// <summary>
/// The terminal look of every ordinary window and control, written over the Nanotrasen rules.
/// A rule here wins a tie because it stands later in the sheet, so each one repeats the selector it replaces.
/// </summary>
public static class SWTerminalGlobalRules
{
    private const string WindowHeading = "WindowHeadingBackground";
    private const string FancyWindowTitle = "FancyWindowTitle";
    private const string TimerLabel = "LabelBig";
    private const string TerminalPath = "/Textures/_SW/Interface/Terminal/";
    private const string RadialPath = TerminalPath + "Nano/Radial/";

    private static readonly Color ButtonPressed = Color.FromHex("#3A3F4A");
    private static readonly Color ButtonPressedBorder = Color.FromHex("#8E97A8");
    private static readonly Color PanelLightFill = Color.FromHex("#16181C");
    private static readonly Color ChatFill = Background.WithAlpha(221f / 255f);
    private static readonly Color TextDimColor = Color.FromHex("#AEB3BF");
    private static readonly Color AlertLine = Color.FromHex("#BB3232");
    private static readonly Color Grabber = Color.FromHex("#4F5666");
    private static readonly Color GrabberHover = Color.FromHex("#697387");
    private static readonly Color GrabberPressed = Color.FromHex("#8E97A8");

    public static StyleRule[] GetRules(IResourceCache res)
    {
        var rules = new List<StyleRule>();
        AddWindows(rules, res);
        AddPanels(rules);
        AddButtons(rules);
        AddLists(rules);
        AddFields(rules, res);
        AddHud(rules, res);
        AddLabels(rules, res);
        AddMissing(rules, res);
        return rules.ToArray();
    }

    /// <summary>
    /// The lines of a window in one accent. Neutral is the look of everything no faction owns.
    /// </summary>
    public static StyleRule[] GetAccentRules(string accent)
    {
        var line = accent == NeutralAccent ? SectionDividerColor : AccentColor(accent);

        // A line in the box, not a child, because the default window has no room for one.
        var header = new StyleBoxFlat
        {
            BackgroundColor = Background,
            BorderColor = line,
            BorderThickness = new Thickness(0, 0, 0, 3),
        };
        header.SetContentMarginOverride(StyleBox.Margin.Bottom, 3);

        return new StyleRule[]
        {
            E().Class(DefaultWindow.StyleClassWindowHeader).Panel(header),
            E<PanelContainer>().Class(StyleClass.LowDivider).Panel(Fill(line)).MinSize(new Vector2(3, 3)),
            E<PanelContainer>().Class(StyleClass.HighDivider).Panel(Fill(line)),
            E<PanelContainer>().Class(StyleClass.Highlight).Panel(Fill(line)),
            E<HLine>().Class(StyleClass.Highlight).Panel(Fill(line)),
        };
    }

    private static void AddWindows(List<StyleRule> rules, IResourceCache res)
    {
        var title = res.GetFont(BoldFont, 14);

        rules.AddRange(new StyleRule[]
        {
            E().Class(DefaultWindow.StyleClassWindowPanel).Panel(Box(Background, WindowBorder, 1)),
            E().Class(StyleClass.BorderedWindowPanel).Panel(Box(Background, WindowBorder, 1)),
            E().Class(WindowHeading).Panel(Fill(Background)),

            E<Label>().Class(DefaultWindow.StyleClassWindowTitle).Font(title).FontColor(TextBright),
            E<Label>().Class(FancyWindowTitle).Font(title).FontColor(TextBright),

            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).PseudoNormal().Modulate(TextBright),

        });
    }

    private static void AddPanels(List<StyleRule> rules)
    {
        rules.AddRange(new StyleRule[]
        {
            E().Class(StyleClass.BackgroundPanel).Panel(Box(Background, WindowBorder, 1, 14, 2)).Modulate(Color.White),
            E().Class(StyleClass.BackgroundPanelOpenLeft).Panel(Box(Background, CardBorder, 1, 8, 14, 2)).Modulate(Color.White),
            E().Class(StyleClass.BackgroundPanelOpenRight).Panel(Box(Background, CardBorder, 1, 14, 8, 2)).Modulate(Color.White),

            // The Nanotrasen logo in window footers is not ours. A rule removes it from every window at once.
            E<TextureRect>().Class("NTLogoDark").Prop(TextureRect.StylePropertyTexture, Texture.Transparent),

            E<PanelContainer>().Class(StyleClass.PanelLight).Panel(Fill(PanelLightFill)),
            E<PanelContainer>().Class("PdaContentBackground").Panel(Fill(Background)).Modulate(Color.White),
            E<PanelContainer>().Class("PdaBackground").Panel(Fill(Background)).Modulate(Color.White),
            E<PanelContainer>().Class("PdaBackgroundRect").Panel(Fill(PanelLightFill)).Modulate(Color.White),
            E<PanelContainer>().Class("PdaBorderRect").Panel(Box(Color.Transparent, WindowBorder, 1)),
            E<PanelContainer>().Class(StyleClass.PanelDark).Panel(Fill(Background)),
            E<PanelContainer>().Class("BackgroundDark").Panel(Fill(PanelLightFill)),
        });
    }

    private static void AddButtons(List<StyleRule> rules)
    {
        // The base sheet tints a white texture with modulate-self. Our boxes hold their own colour, so that goes white.
        // The selector stays this weak so that the positive, negative and confirm tints keep beating it.
        rules.AddRange(new StyleRule[]
        {
            E().PseudoNormal().Modulate(Color.White),
            E().PseudoHovered().Modulate(Color.White),
            E().PseudoPressed().Modulate(Color.White),
            E().PseudoDisabled().Modulate(Color.White),
        });

        AddButtonKind(rules, null, 14, 14, 2);
        AddButtonKind(rules, StyleClass.ButtonOpenLeft, 8, 14, 2);
        AddButtonKind(rules, StyleClass.ButtonOpenRight, 14, 8, 2);
        AddButtonKind(rules, StyleClass.ButtonOpenBoth, 8, 8, 2);
        AddButtonKind(rules, StyleClass.ButtonSquare, 8, 8, 2);
        AddButtonKind(rules, StyleClass.ButtonSmall, 0, 0, 0);

        // A status keeps the base colours. A tint cannot give a border lighter than the fill, so these are set directly.
        foreach (var (status, palette) in new[] { (StyleClass.Positive, Palettes.Green), (StyleClass.Negative, Palettes.Red) })
        {
            var border = Color.InterpolateBetween(palette.Element, Color.White, 0.4f);
            rules.AddRange(new StyleRule[]
            {
                CButton().Class(status).PseudoNormal().Modulate(Color.White).Box(Box(palette.Element, border, 1, 14, 2)),
                CButton().Class(status).PseudoHovered().Modulate(Color.White).Box(Box(palette.HoveredElement, border, 1, 14, 2)),
                CButton().Class(status).PseudoPressed().Modulate(Color.White).Box(Box(palette.PressedElement, border, 1, 14, 2)),
                CButton().Class(status).PseudoDisabled().Modulate(Color.White).Box(Box(Disabled, DividerColor, 1, 14, 2)),
            });
        }

        // A confirm button is tinted by the base rule, over a white box.
        var tinted = Box(Color.White, Color.White, 0, 14, 2);

        foreach (var state in new[]
                 {
                     ContainerButton.StylePseudoClassNormal,
                     ContainerButton.StylePseudoClassHover,
                     ContainerButton.StylePseudoClassPressed,
                     ContainerButton.StylePseudoClassDisabled,
                 })
        {
            rules.Add(E<ConfirmButton>().Pseudo(ConfirmButton.ConfirmPrefix + state).Box(tinted));
        }
    }

    private static void AddButtonKind(List<StyleRule> rules, string? kind, float left, float right, float vertical)
    {
        rules.AddRange(new StyleRule[]
        {
            CButton().MaybeClass(kind).PseudoNormal().Box(Box(ButtonFill, ButtonBorder, 1, left, right, vertical)),
            CButton().MaybeClass(kind).PseudoHovered().Box(Box(ButtonHover, ButtonBorder, 1, left, right, vertical)),
            CButton().MaybeClass(kind).PseudoPressed().Box(Box(ButtonPressed, ButtonPressedBorder, 1, left, right, vertical)),
            CButton().MaybeClass(kind).PseudoDisabled().Box(Box(Disabled, DividerColor, 1, left, right, vertical)),
        });
    }

    private static void AddLists(List<StyleRule> rules)
    {
        var tabActive = Fill(ButtonHover);
        tabActive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 5);
        var tabInactive = Fill(Background);
        tabInactive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 5);

        // These rows hold a white box in code, so unlike other buttons they take their colour from modulate.
        var row = ContainerButton.StyleClassButton;

        // The gold corner of a section heading is a picture. A rule line says the same.
        var heading = new StyleBoxFlat
        {
            BackgroundColor = Color.Transparent,
            BorderColor = SectionDividerColor,
            BorderThickness = new Thickness(0, 0, 0, 2),
        };
        heading.SetContentMarginOverride(StyleBox.Margin.Left, 2);
        heading.SetContentMarginOverride(StyleBox.Margin.Top, 2);
        heading.SetContentMarginOverride(StyleBox.Margin.Bottom, 2);

        rules.AddRange(new StyleRule[]
        {
            E<TabContainer>()
                .Prop(TabContainer.StylePropertyPanelStyleBox, Box(Background, CardBorder, 1, 2, 2))
                .Prop(TabContainer.StylePropertyTabStyleBox, tabActive)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabInactive),

            E<ItemList>()
                .Prop(ItemList.StylePropertyBackground, Fill(Background))
                .Prop(ItemList.StylePropertyItemBackground, ListItem(Background))
                .Prop(ItemList.StylePropertyDisabledItemBackground, ListItem(Disabled))
                .Prop(ItemList.StylePropertySelectedItemBackground, ListItem(ButtonHover)),

            E<ListContainerButton>().Class(row).PseudoNormal().Modulate(ButtonFill),
            E<ListContainerButton>().Class(row).PseudoHovered().Modulate(ButtonHover),
            E<ListContainerButton>().Class(row).PseudoPressed().Modulate(ButtonPressed),
            E<ListContainerButton>().Class(row).PseudoDisabled().Modulate(Disabled),

            E<NanoHeading>().ParentOf(E<PanelContainer>()).Panel(heading),

            E<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground).Panel(Box(Background, CardBorder, 1)),

            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, VGrabber(Grabber)),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, VGrabber(GrabberHover)),
            E<VScrollBar>().PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, VGrabber(GrabberPressed)),
            E<HScrollBar>().Prop(ScrollBar.StylePropertyGrabber, HGrabber(Grabber)),
            E<HScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, HGrabber(GrabberHover)),
            E<HScrollBar>().PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, HGrabber(GrabberPressed)),
        });
    }

    private static void AddFields(List<StyleRule> rules, IResourceCache res)
    {
        var sliderBack = Fill(FieldFill);
        sliderBack.SetContentMarginOverride(StyleBox.Margin.Vertical, 6);

        rules.AddRange(new StyleRule[]
        {
            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, Box(FieldFill, ButtonBorder, 1, 5, 3)),

            E<Slider>()
                .Prop(Slider.StylePropertyBackground, sliderBack)
                .Prop(Slider.StylePropertyForeground, Box(Color.Transparent, ButtonBorder, 1))
                .Prop(Slider.StylePropertyFill, Fill(GrabberHover))
                .Prop(Slider.StylePropertyGrabber, Box(GrabberPressed, TextBright, 1, 5, 2)),

            E<TextureRect>()
                .Class(CheckBox.StyleClassCheckBox)
                .Prop(TextureRect.StylePropertyTexture, res.GetTexture($"{TerminalPath}checkbox_unchecked.png")),
            E<TextureRect>()
                .Class(CheckBox.StyleClassCheckBox)
                .Class(CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, res.GetTexture($"{TerminalPath}checkbox_checked.png")),

            E<StripeBack>().Prop(StripeBack.StylePropertyBackground, Fill(PanelLightFill)),
        });
    }

    private static void AddHud(List<StyleRule> rules, IResourceCache res)
    {
        // The top bar keeps its layout. Its icons take their colours from code.
        foreach (var kind in new[] { null, StyleClass.ButtonOpenLeft, StyleClass.ButtonOpenRight, StyleClass.ButtonOpenBoth, StyleClass.ButtonSquare })
        {
            rules.AddRange(new StyleRule[]
            {
                E<MenuButton>().MaybeClass(kind).PseudoNormal().Modulate(Color.White).Box(Box(ButtonFill, ButtonBorder, 1)),
                E<MenuButton>().MaybeClass(kind).PseudoHovered().Modulate(Color.White).Box(Box(ButtonHover, ButtonBorder, 1)),
                E<MenuButton>().MaybeClass(kind).PseudoPressed().Modulate(Color.White).Box(Box(ButtonPressed, ButtonPressedBorder, 1)),
                E<MenuButton>().MaybeClass(kind).PseudoDisabled().Modulate(Color.White).Box(Box(Disabled, DividerColor, 1)),
            });
        }

        foreach (var (selector, normal, hover) in new[]
                 {
                     ("RadialMenuButton", "button_normal", "button_hover"),
                     ("RadialMenuCloseButton", "close_normal", "close_hover"),
                     ("RadialMenuBackButton", "back_normal", "back_hover"),
                 })
        {
            rules.AddRange(new StyleRule[]
            {
                E<TextureButton>().Class(selector).Prop(TextureButton.StylePropertyTexture, res.GetTexture($"{RadialPath}{normal}.png")),
                E<TextureButton>().Class(selector).Pseudo(TextureButton.StylePseudoClassHover).Prop(TextureButton.StylePropertyTexture, res.GetTexture($"{RadialPath}{hover}.png")),
            });
        }

        var tooltip = Box(Background, WindowBorder, 1, 7, 2);

        rules.AddRange(new StyleRule[]
        {
            E<PanelContainer>().Class(StyleClass.TooltipPanel).Modulate(Color.White).Panel(tooltip),
            E<Tooltip>().Prop(Tooltip.StylePropertyPanel, tooltip),
            E<PanelContainer>().Class(ExamineSystem.StyleClassEntityTooltip).Panel(tooltip),

            E<PanelContainer>().Class(ChatInputBox.StyleClassChatPanel).Panel(Box(ChatFill, CardBorder, 1)),
            E().Class(SeparatedChatGameScreen.StyleClassChatContainer).Panel(Fill(Background)),
            E<OutputPanel>().Class(SeparatedChatGameScreen.StyleClassChatOutput).Panel(Fill(Background)),

            E<PanelContainer>()
                .Class(ContextMenuPopup.StyleClassContextMenuPopup)
                .Panel(Box(Background, CardBorder, 1, ContextMenuElement.ElementMargin, ContextMenuElement.ElementMargin)),
            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoNormal().Modulate(Background),
            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoHovered().Modulate(ButtonHover),
            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoPressed().Modulate(ButtonPressed),
            E<ContextMenuElement>().Class(ContextMenuElement.StyleClassContextMenuButton).PseudoDisabled().Modulate(Disabled),
        });
    }

    /// <summary>
    /// Everything else the base sheet draws with its own colours or pictures. The owner found the guidebook's tree by
    /// looking, so this list comes from comparing every base selector with ours: see the case file for what is left.
    /// </summary>
    private static void AddMissing(List<StyleRule> rules, IResourceCache res)
    {
        const string tree = TreeItem.StyleIdentifierTreeButton;
        var monotone = Box(Color.Transparent, ButtonBorder, 1, 14, 2);
        var monotonePressed = Box(TextBright, TextBright, 1, 14, 2);
        var bar = Fill(FieldFill);
        bar.SetContentMarginOverride(StyleBox.Margin.Vertical, 14.5f);
        var whisper = Box(Color.Transparent, CardBorder, 1, 7, 2);
        var alert = new StyleBoxFlat
        {
            BackgroundColor = Background,
            BorderColor = AlertLine,
            BorderThickness = new Thickness(0, 0, 0, 3),
        };
        alert.SetContentMarginOverride(StyleBox.Margin.Bottom, 3);

        rules.AddRange(new StyleRule[]
        {
            E<ContainerButton>().Identifier(tree).Class(TreeItem.StyleClassEvenRow).Box(Fill(PanelLightFill)),
            E<ContainerButton>().Identifier(tree).Class(TreeItem.StyleClassOddRow).Box(Fill(Background)),
            E<ContainerButton>().Identifier(tree).Class(TreeItem.StyleClassSelected).Box(Fill(ButtonPressed)),
            E<ContainerButton>().Identifier(tree).Pseudo(ContainerButton.StylePseudoClassHover).Box(Fill(ButtonHover)),
            E<FancyTree>()
                .Prop(FancyTree.StylePropertyLineColor, Grabber)
                .Prop(FancyTree.StylePropertyIconColor, GrabberPressed),

            E<ProgressBar>().Prop(ProgressBar.StylePropertyBackground, bar),

            E<MonotoneButton>().Box(monotone),
            E<MonotoneButton>().Class(StyleClass.ButtonOpenLeft).Box(monotone),
            E<MonotoneButton>().Class(StyleClass.ButtonOpenRight).Box(monotone),
            E<MonotoneButton>().Class(StyleClass.ButtonOpenBoth).Box(monotone),
            // Its pressed text is dark, so the pressed fill has to stay bright.
            E<MonotoneButton>().PseudoPressed().Box(monotonePressed),
            E<MonotoneButton>().Class(StyleClass.ButtonOpenLeft).PseudoPressed().Box(monotonePressed),
            E<MonotoneButton>().Class(StyleClass.ButtonOpenRight).PseudoPressed().Box(monotonePressed),
            E<MonotoneButton>().Class(StyleClass.ButtonOpenBoth).PseudoPressed().Box(monotonePressed),

            E<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).PseudoHovered().Modulate(ButtonHover),
            E<ExamineButton>().Class(ExamineButton.StyleClassExamineButton).PseudoPressed().Modulate(ButtonPressed),

            E<Placeholder>().Prop(Placeholder.StylePropertyPanel, Box(PanelLightFill, CardBorder, 1)),

            E<TextureRect>()
                .Class(MonotoneCheckBox.StyleClassMonotoneCheckBox)
                .Prop(TextureRect.StylePropertyTexture, res.GetTexture($"{TerminalPath}checkbox_unchecked.png")),
            E<TextureRect>()
                .Class(MonotoneCheckBox.StyleClassMonotoneCheckBox)
                .Class(CheckBox.StyleClassCheckBoxChecked)
                .Prop(TextureRect.StylePropertyTexture, res.GetTexture($"{TerminalPath}checkbox_checked.png")),

            E<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).Modulate(TextBright),
            E<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).Pseudo(ContainerButton.StylePseudoClassHover).Modulate(Color.White),
            E<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).Pseudo(ContainerButton.StylePseudoClassPressed).Modulate(TextDimColor),

            E<LineEdit>().Class(ActionsWindow.StyleClassActionSearchBox).Box(Box(FieldFill, ButtonBorder, 1, 5, 3)),
            E<PanelContainer>().Class(ActionButton.StyleClassActionHighlightRect).Panel(Box(Color.Transparent, TextBright, 2)),

            E<PanelContainer>().Class("speechBox", "sayBox").Panel(Box(Background, WindowBorder, 1, 7, 2)),
            E<PanelContainer>().Class("speechBox", "whisperBox").Panel(whisper),

            // An alert must still read as one, so this line does not follow the accent.
            E().Class(StyleClass.AlertWindowHeader).Panel(alert),

            E<Button>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(Box(ButtonFill, ButtonBorder, 1, 6, 2)),
            E<ContainerButton>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(Box(ButtonFill, ButtonBorder, 1, 6, 2)),
        });
    }

    private static void AddLabels(List<StyleRule> rules, IResourceCache res)
    {
        rules.AddRange(new StyleRule[]
        {
            E<Label>().Class(StyleClass.LabelHeading).Font(res.GetFont(BoldFont, 15)).FontColor(TextBright),
            E<Label>().Class(StyleClass.LabelHeadingBigger).Font(res.GetFont(BoldFont, 18)).FontColor(TextBright),
            E<Label>().Class(StyleClass.LabelKeyText).FontColor(TextBright),

            // The lobby's round timer asks for a class only the old sheet had, so it draws at the default size.
            E<Label>().Class(TimerLabel).Font(res.GetFont(BoldFont, 16)).FontColor(TextBright),
        });
    }

    private static MutableSelectorElement CButton()
    {
        return E<ContainerButton>().Class(ContainerButton.StyleClassButton);
    }

    private static StyleBoxFlat ListItem(Color color)
    {
        var box = Fill(color);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 4);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        return box;
    }

    private static StyleBoxFlat VGrabber(Color color)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = color,
            ContentMarginLeftOverride = ScrollbarSheetlet.DefaultGrabberSize,
            ContentMarginTopOverride = ScrollbarSheetlet.DefaultGrabberSize,
        };
    }

    private static StyleBoxFlat HGrabber(Color color)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = color,
            ContentMarginTopOverride = ScrollbarSheetlet.DefaultGrabberSize,
        };
    }
}
