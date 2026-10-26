using System;
using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate;

public static class SWConsoleStyle
{
    public static readonly Color BgDark = Color.FromHex("#111D28");
    public static readonly Color BgPanel = Color.FromHex("#172633");
    public static readonly Color BgRowAlt = Color.FromHex("#1B2C3B");
    public static readonly Color Navigation = Color.FromHex("#0B151F");
    public static readonly Color Hover = Color.FromHex("#294657");
    public static readonly Color Selected = Color.FromHex("#203D50");
    public static readonly Color Accent = Color.FromHex("#93D9F3");
    public static readonly Color Brass = Color.FromHex("#D4B57D");
    public static readonly Color TextBright = Color.FromHex("#E2EDF1");
    public static readonly Color TextDim = Color.FromHex("#A0B4C1");
    public static readonly Color Good = Color.FromHex("#A4C5AD");
    public static readonly Color Warn = Color.FromHex("#D8BC8C");
    public static readonly Color Bad = Color.FromHex("#DFA7A7");
    public static readonly Color Neutral = TextDim;
    public static readonly Color PillGood = Color.FromHex("#27342B");
    public static readonly Color PillDim = Color.FromHex("#223442");
    public static readonly Color PillBad = Color.FromHex("#3D2C30");
    public static readonly Color Edge = Color.FromHex("#314959");

    private static Font? _mono;
    private static Font? _monoBold;
    private static Font? _monoSmall;
    private static Font? _monoLarge;
    private static Font? _header;

    public static Font Body => _mono ??= LoadFont("Regular", 12);
    public static Font Strong => _monoBold ??= LoadFont("Bold", 12);
    public static Font Small => _monoSmall ??= LoadFont("Regular", 11);
    public static Font Title => _monoLarge ??= LoadFont("Bold", 17);
    public static Font Header => _header ??= LoadFont("Bold", 15);

    // Kept for the director panel, which also consumes the Senate style.
    public static Font Mono => Body;
    public static Font MonoBold => Strong;
    public static Font MonoSmall => Small;
    public static Font MonoLarge => Title;

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>($"/Fonts/NotoSans/NotoSans-{variant}.ttf"), size);

    public static Label Text(string text, Color color, Font font, float minWidth = 0, Control.HAlignment? align = null)
    {
        var label = new Label { Text = text, FontOverride = font, FontColorOverride = color };
        if (minWidth > 0)
            label.MinWidth = minWidth;
        if (align is { } a)
            label.HorizontalAlignment = a;
        return label;
    }

    public static Label Section(string text) => new()
    {
        Text = text,
        FontOverride = Strong,
        FontColorOverride = TextBright,
        Margin = new Thickness(0, 10, 0, 6),
    };

    public static RichTextLabel Wrap(string text, Color color, float fallbackWidth = 660f)
    {
        var label = new WrappingRichTextLabel(fallbackWidth)
        {
            HorizontalExpand = true,
            HorizontalAlignment = Control.HAlignment.Stretch,
            VerticalAlignment = Control.VAlignment.Top,
        };

        var msg = new FormattedMessage();
        msg.PushColor(color);
        msg.AddText(text);
        msg.Pop();
        label.SetMessage(msg);
        return label;
    }

    public static Control Pill(string text, Color fg, Color bg) => Text(text, fg, Small);

    public static PanelContainer Row(Color? bg = null, Color? leftAccent = null)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = bg ?? BgPanel,
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
            BorderColor = Edge,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        return new PanelContainer { PanelOverride = box, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 2) };
    }

    public static void StyleRoot(PanelContainer p)
    {
        p.PanelOverride = new StyleBoxFlat { BackgroundColor = BgDark };
        // Extend the active game sheet: replacing it would strip dropdown, slider and button defaults.
        var baseRules = IoCManager.Resolve<IUserInterfaceManager>().Stylesheet?.Rules ?? Array.Empty<StyleRule>();
        p.Stylesheet = new Stylesheet(baseRules.Concat(new StyleRule[]
        {
            StylesheetHelpers.Element<LineEdit>().Prop("font", Body).Prop("font-color", TextBright)
                .Prop(LineEdit.StylePropertyStyleBox, new StyleBoxFlat
                {
                    BackgroundColor = Navigation, BorderColor = Edge, BorderThickness = new Thickness(0, 0, 0, 1),
                    ContentMarginLeftOverride = 8, ContentMarginRightOverride = 8,
                    ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6,
                }).Prop(LineEdit.StylePropertyCursorColor, Accent).Prop(LineEdit.StylePropertySelectionColor, Selected),
            StylesheetHelpers.Element<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder).Prop("font-color", TextDim),
            StylesheetHelpers.Element<TextEdit>().Prop("font", Body).Prop("font-color", TextBright)
                .Prop(TextEdit.StylePropertyCursorColor, Accent).Prop(TextEdit.StylePropertySelectionColor, Selected),
            StylesheetHelpers.Element<TextEdit>().Pseudo(TextEdit.StylePseudoClassPlaceholder).Prop("font-color", TextDim),
        }).ToArray());
        // Prefer a document workspace, but keep its initial size inside the player's UI viewport.
        for (Control? parent = p.Parent; parent != null; parent = parent.Parent)
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

    public static void StyleHeader(PanelContainer p) =>
        p.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Navigation,
            BorderColor = Brass,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

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

    public static void StyleTabBar(PanelContainer p) =>
        p.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = BgDark,
            BorderColor = Edge,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

    public static Button TabButton(string title)
    {
        var btn = new SWConsoleButton { Text = title, Margin = new Thickness(0, 0, 4, 2) };
        btn.Label.FontOverride = MonoBold;
        return btn;
    }

    public static void StyleTabButton(Button btn, bool active)
    {
        btn.ModulateSelfOverride = Color.White;
        if (btn is SWConsoleButton modern)
        {
            modern.SetAppearance(TextBright, selected: active, quiet: true);
            return;
        }
        btn.StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = active ? Selected : Color.Transparent,
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 7,
            ContentMarginBottomOverride = 7,
        };
        btn.Label.FontColorOverride = active ? TextBright : TextDim;
    }

    public static Button Action(string text, Color accent, bool disabled = false)
    {
        var btn = new SWConsoleButton { Text = text, Disabled = disabled };
        btn.SetAppearance(accent);
        return btn;
    }

    public static Button Primary(string text, bool disabled = false)
    {
        var button = new SWConsoleButton { Text = text, Disabled = disabled };
        button.SetAppearance(TextBright, primary: true);
        return button;
    }

    public static ProgressBar Bar(float value, float min, float max, Color color)
    {
        return new ProgressBar
        {
            MinValue = min,
            MaxValue = max <= min ? min + 1 : max,
            Value = value,
            HorizontalExpand = true,
            MinHeight = 12,
            Margin = new Thickness(0, 2, 0, 0),
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = PillDim },
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = color },
        };
    }

    public static Color ValueColor(float v) => v >= 60f ? Good : v <= 40f ? Bad : TextBright;

    public static Color SeverityColor(float severity) =>
        severity >= 70f ? Bad : severity >= 40f ? Warn : Good;

    // RichTextLabel wraps text at whatever width it was MEASURED with and never re-wraps on arrange
    // (RobustToolbox RichTextLabel: Draw uses the wrapping computed in the last MeasureOverride). Parents
    // routinely MEASURE us at a width wider than the width we are ultimately ARRANGED to (a scroll
    // container measures children at its own width but arranges them minus the scrollbar/margins), so the
    // text wraps too wide and gets clipped. Mirroring OutputPanel/chat, we ignore the measure-time width
    // and always wrap against our real arranged Width, re-measuring on Resized so the wrap follows layout.
    public sealed class WrappingRichTextLabel : RichTextLabel
    {
        private readonly float _fallbackWidth;

        public WrappingRichTextLabel(float fallbackWidth)
        {
            _fallbackWidth = fallbackWidth;
            Stylesheet = new Stylesheet(new StyleRule[]
            {
                StylesheetHelpers.Element<RichTextLabel>().Prop("font", Body),
            });
        }

        protected override void Resized()
        {
            base.Resized();
            InvalidateMeasure();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            // Prefer the LIVE constraint: the previously arranged Width can be stale garbage from a pass
            // made while the subtree was hidden/collapsed, and latching onto it kept the wrong wrap height
            // forever (only a manual window resize re-arranged and healed it). The remembered width is
            // only a fallback for a genuinely infinite first-pass constraint.
            var width = availableSize.X;
            if (float.IsInfinity(width) || width <= 0f)
                width = Width > 1f ? Width : _fallbackWidth;

            return base.MeasureOverride(new Vector2(width, availableSize.Y));
        }
    }
}
