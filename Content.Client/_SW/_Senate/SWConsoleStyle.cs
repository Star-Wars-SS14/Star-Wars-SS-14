using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate;

public static class SWConsoleStyle
{
    public static readonly Color BgDark = Color.FromHex("#0A0E14");
    public static readonly Color BgPanel = Color.FromHex("#141B26");
    public static readonly Color BgRowAlt = Color.FromHex("#0F141D");
    public static readonly Color Accent = Color.FromHex("#37C8D6");
    public static readonly Color TextBright = Color.FromHex("#D6E2F0");
    public static readonly Color TextDim = Color.FromHex("#6E7E92");
    public static readonly Color Good = Color.FromHex("#43D16A");
    public static readonly Color Warn = Color.FromHex("#E0A23B");
    public static readonly Color Bad = Color.FromHex("#E0556B");
    public static readonly Color Neutral = Color.FromHex("#7A8696");
    public static readonly Color PillGood = Color.FromHex("#15301E");
    public static readonly Color PillDim = Color.FromHex("#1A2230");
    public static readonly Color PillBad = Color.FromHex("#321A22");
    public static readonly Color Edge = Color.FromHex("#1C2530");

    private static Font? _mono;
    private static Font? _monoBold;
    private static Font? _monoSmall;
    private static Font? _monoLarge;

    public static Font Mono => _mono ??= LoadFont("Regular", 11);
    public static Font MonoBold => _monoBold ??= LoadFont("Bold", 12);
    public static Font MonoSmall => _monoSmall ??= LoadFont("Regular", 10);
    public static Font MonoLarge => _monoLarge ??= LoadFont("Bold", 14);

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>($"/Fonts/RobotoMono/RobotoMono-{variant}.ttf"), size);

    public static Label Text(string text, Color color, Font font, float minWidth = 0, Control.HAlignment? align = null)
    {
        var label = new Label { Text = text, FontOverride = font, FontColorOverride = color };
        if (minWidth > 0)
            label.MinWidth = minWidth;
        if (align is { } a)
            label.HorizontalAlignment = a;
        return label;
    }

    public static Label Section(string text) => Text("// " + text, Accent, MonoBold);

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

    public static Control Pill(string text, Color fg, Color bg)
    {
        var panel = new PanelContainer
        {
            VerticalAlignment = Control.VAlignment.Center,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = bg,
                BorderColor = fg,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 7,
                ContentMarginRightOverride = 7,
                ContentMarginTopOverride = 1,
                ContentMarginBottomOverride = 1,
            },
        };
        panel.AddChild(Text(text, fg, MonoSmall));
        return panel;
    }

    public static PanelContainer Row(Color? bg = null, Color? leftAccent = null)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = bg ?? BgPanel,
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 3,
            ContentMarginBottomOverride = 3,
        };

        if (leftAccent is { } a)
        {
            box.BorderColor = a;
            box.BorderThickness = new Thickness(2, 0, 0, 0);
        }

        return new PanelContainer { PanelOverride = box, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 2) };
    }

    public static void StyleRoot(PanelContainer p) =>
        p.PanelOverride = new StyleBoxFlat { BackgroundColor = BgDark };

    public static void StyleHeader(PanelContainer p) =>
        p.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = BgPanel,
            BorderColor = Accent,
            BorderThickness = new Thickness(0, 0, 0, 2),
        };

    public static void StyleTabBar(PanelContainer p) =>
        p.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = BgDark,
            BorderColor = Edge,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

    public static Button TabButton(string title)
    {
        var btn = new Button { Text = title, Margin = new Thickness(0, 0, 4, 0) };
        btn.Label.FontOverride = MonoBold;
        return btn;
    }

    public static void StyleTabButton(Button btn, bool active)
    {
        btn.StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = active ? BgPanel : BgDark,
            BorderColor = Accent,
            BorderThickness = active ? new Thickness(0, 0, 0, 2) : new Thickness(0),
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 4,
        };
        btn.Label.FontColorOverride = active ? Accent : TextDim;
    }

    public static Button Action(string text, Color accent, bool disabled = false)
    {
        var btn = new Button { Text = text, Disabled = disabled };
        btn.Label.FontOverride = MonoBold;
        btn.Label.FontColorOverride = disabled ? TextDim : accent;
        btn.StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = BgPanel,
            BorderColor = disabled ? Edge : accent,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 4,
        };
        return btn;
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
