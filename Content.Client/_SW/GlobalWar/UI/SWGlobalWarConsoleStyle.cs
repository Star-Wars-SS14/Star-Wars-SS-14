using System.Linq;
using System.Text.RegularExpressions;
using Content.Client._SW.UserInterface;
using Content.Client.Resources;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.TaskSystem;
using Robust.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Utility;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Campaign colours and fonts on the terminal palette, see <see cref="SWTerminalStyle"/> for what a colour means.</summary>
public static class SWGlobalWarConsoleStyle
{
    public static readonly Color BgDark = SWTerminalStyle.Background;
    public static readonly Color BgPanel = SWTerminalStyle.Disabled;
    public static readonly Color Navigation = SWTerminalStyle.Background;
    public static readonly Color Hover = SWTerminalStyle.ButtonHover;
    public static readonly Color Selected = SWTerminalStyle.ButtonHover;

    /// <summary>What to look at now: the selected planet, moving freight. Never a faction colour, because this is also text.</summary>
    public static readonly Color Accent = SWTerminalStyle.TextBright;

    /// <summary>Waiting, shortage, the current place. Not money, which is <see cref="Money"/>.</summary>
    public static readonly Color Brass = Color.FromHex("#E2AA80");
    public static readonly Color Money = SWTerminalStyle.MoneyColor;
    public static readonly Color TextBright = SWTerminalStyle.TextBright;
    public static readonly Color TextDim = SWTerminalStyle.TextDim;
    public static readonly Color Good = SWTerminalStyle.Green;
    public static readonly Color Bad = Color.FromHex("#FF9991");
    public static readonly Color PillDim = SWTerminalStyle.ButtonFill;
    public static readonly Color Edge = SWTerminalStyle.CardBorder;

    /// <summary>A sum as our text writes it, whichever language the text is in.</summary>
    private static readonly Regex MoneyPattern = new(@"\d[\d ]*\s?(ГК|GC)\b", RegexOptions.Compiled);

    private static Font? _body;
    private static Font? _strong;
    private static Font? _small;
    private static Font? _title;
    private static Font? _smallStrong;
    private static Font? _titleLarge;
    private static Font? _mainButton;
    private static Font? _nameLarge;
    private static Stylesheet? _sheet;
    private static Stylesheet? _sheetBase;
    public static Font Body => _body ??= LoadFont(SWTerminalStyle.RegularFont, 12);
    public static Font Strong => _strong ??= LoadFont(SWTerminalStyle.BoldFont, 12);
    public static Font Small => _small ??= LoadFont(SWTerminalStyle.RegularFont, 11);
    public static Font MainButton => _mainButton ??= LoadFont(SWTerminalStyle.BoldFont, 13);
    public static Font TitleLarge => _titleLarge ??= LoadFont(SWTerminalStyle.BoldFont, 17);
    public static Font NameLarge => _nameLarge ??= LoadFont(SWTerminalStyle.BoldFont, 20);
    public static Font SmallStrong => _smallStrong ??= LoadFont(SWTerminalStyle.BoldFont, 11);
    public static Font Title => _title ??= LoadFont(SWTerminalStyle.BoldFont, 14);

    /// <summary>Loads a campaign icon at its native pixel size from the declared RSI state.</summary>
    public static Texture GovernmentIcon(string group, string state) => IoCManager.Resolve<IEntityManager>()
        .System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(new ResPath($"_SW/Interface/MegaMode/{group}.rsi"), state));

    private static Font LoadFont(string path, int size) => IoCManager.Resolve<IResourceCache>().GetFont(path, size);

    private static readonly Dictionary<int, Font> BoldFonts = new();

    /// <summary>The bold terminal font at any size the mock asks for.</summary>
    public static Font Bold(int size)
    {
        if (!BoldFonts.TryGetValue(size, out var font))
            BoldFonts[size] = font = LoadFont(SWTerminalStyle.BoldFont, size);
        return font;
    }

    public static readonly Color RowFill = Color.FromHex("#121418");
    public static readonly Color RowSelected = Color.FromHex("#1E2836");
    public static readonly Color Amber = Color.FromHex("#FF9F45");

    /// <summary>A list row: a dark strip, or a lit one with a white frame when chosen.</summary>
    public static StyleBox RowBox(bool selected) => SWTerminalStyle.Box(selected ? RowSelected : RowFill,
        selected ? Color.White : SWTerminalStyle.DividerColor, 1);

    public static readonly Color RepublicText = Color.FromHex("#6FB7FF");
    public static readonly Color EmpireText = Color.FromHex("#FF9991");

    /// <summary>A side's name in text. Faction accents themselves are too dark for words on a dark band.</summary>
    public static Color SideText(SWGlobalWarFaction side) => side switch
    {
        SWGlobalWarFaction.Republic => RepublicText,
        SWGlobalWarFaction.Empire => EmpireText,
        _ => TextDim,
    };

    /// <summary>The soldier icon that stands for a side, or null for the neutral worlds that have none.</summary>
    public static Texture? SideIcon(SWGlobalWarFaction side) => side == SWGlobalWarFaction.Neutral ? null
        : IoCManager.Resolve<IEntityManager>().System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(new ResPath("_SW/Interface/Misc/job_icons.rsi"),
            side == SWGlobalWarFaction.Republic ? "RepublicSoldier" : "EmpireSoldier"));

    /// <summary>Which side the local player's faction is, by its prototype ID.</summary>
    public static SWGlobalWarFaction LocalSide() => LocalFaction() switch
    {
        "RepublicFaction" => SWGlobalWarFaction.Republic,
        "EmpireFaction" => SWGlobalWarFaction.Empire,
        _ => SWGlobalWarFaction.Neutral,
    };

    /// <summary>A scene made for one window.</summary>
    public static Texture Picture(string name) => IoCManager.Resolve<Robust.Client.ResourceManagement.IResourceCache>()
        .GetResource<Robust.Client.ResourceManagement.TextureResource>($"/Textures/_SW/Interface/MegaMode/windows/{name}.png").Texture;

    /// <summary>The side's letter in the names of those scenes. Whoever has no side sees the Republic's sky.</summary>
    public static string SideLetter(SWGlobalWarFaction? side = null) =>
        (side ?? LocalSide()) == SWGlobalWarFaction.Empire ? "E" : "R";

    /// <summary>A picture from the campaign's art folder.</summary>
    public static Texture Art(string rsi, string state) => IoCManager.Resolve<IEntityManager>().System<SpriteSystem>()
        .Frame0(new SpriteSpecifier.Rsi(new ResPath($"_SW/Interface/MegaMode/{rsi}.rsi"), state));

    /// <summary>How many screen pixels one pixel of map art takes: 2 at 720p, 3 at 1080p.</summary>
    public static int ArtScale =>
        Math.Max(1, (int) MathF.Round(IoCManager.Resolve<IUserInterfaceManager>().WindowRoot.PixelHeight / 360f));

    /// <summary>The faction prototype of the local player's body, if any.</summary>
    public static string? LocalFaction()
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        return entities.TryGetComponent<SWFactionMemberComponent>(IoCManager.Resolve<IPlayerManager>().LocalEntity, out var member)
            ? member.Faction
            : null;
    }

    /// <summary>Plain text with every sum in gold, so that money reads the same in all campaign windows.</summary>
    public static FormattedMessage MoneyMessage(string text)
    {
        var message = new FormattedMessage();
        var position = 0;
        foreach (Match match in MoneyPattern.Matches(text))
        {
            message.AddText(text[position..match.Index]);
            message.PushColor(Money);
            message.AddText(match.Value);
            message.Pop();
            position = match.Index + match.Length;
        }

        message.AddText(text[position..]);
        return message;
    }

    /// <summary>A framed block of a window. Only a thing that stands apart gets a frame.</summary>
    public static StyleBoxFlat CardBox() => SWTerminalStyle.Box(SWTerminalStyle.Background, SWTerminalStyle.CardBorder, 1);

    /// <summary>
    /// The active sheet with one monospaced font for plain labels, buttons and fields. Our rules go before the
    /// <c>SWTerm*</c> ones, because they tie with them and a main button must keep its own font.
    /// </summary>
    public static Stylesheet Sheet()
    {
        var current = IoCManager.Resolve<IUserInterfaceManager>().Stylesheet;
        if (_sheet != null && ReferenceEquals(_sheetBase, current))
            return _sheet;

        var button = () => E<ContainerButton>().Class(ContainerButton.StyleClassButton);
        var fieldBox = () => SWTerminalStyle.Box(SWTerminalStyle.FieldFill, SWTerminalStyle.ButtonBorder, 1, 6, 5);
        var rules = new StyleRule[]
        {
            E<Label>().Font(Body).FontColor(SWTerminalStyle.TextBody),
            E<RichTextLabel>().Font(Body).FontColor(SWTerminalStyle.TextBody),
            E<RichTextLabel>().Class("GlobalWarPlanetTitle").Font(Title).FontColor(TextBright),
            E<RichTextLabel>().Class("GlobalWarMapHelp").Font(Small).FontColor(TextDim),
            button().ParentOf(E<Label>()).Font(Body).FontColor(TextBright),
            button().PseudoDisabled().ParentOf(E<Label>()).FontColor(SWTerminalStyle.TextOff),
            E<Label>().Class(OptionButton.StyleClassOptionButton).Font(Small).FontColor(TextBright).AlignMode(Label.AlignMode.Left),
            E<OptionButton>().Class(ContainerButton.StyleClassButton).PseudoNormal().Box(fieldBox()),
            E<OptionButton>().Class(ContainerButton.StyleClassButton).PseudoHovered().Box(SWTerminalStyle.Box(SWTerminalStyle.ButtonFill, SWTerminalStyle.ButtonBorder, 1, 6, 5)),
            E<OptionButton>().Class(ContainerButton.StyleClassButton).PseudoPressed().Box(SWTerminalStyle.Box(SWTerminalStyle.ButtonFill, SWTerminalStyle.ButtonBorder, 1, 6, 5)),
            E<LineEdit>().Font(Small).FontColor(TextBright).Box(fieldBox())
                .Prop(LineEdit.StylePropertyCursorColor, TextBright)
                .Prop(LineEdit.StylePropertySelectionColor, SWTerminalStyle.ButtonHover),
            E<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder).FontColor(TextDim),
            E<TextEdit>().Font(Small).FontColor(TextBright)
                .Prop(TextEdit.StylePropertyCursorColor, TextBright)
                .Prop(TextEdit.StylePropertySelectionColor, SWTerminalStyle.ButtonHover),
        };

        _sheetBase = current;
        _sheet = new Stylesheet((current?.Rules ?? Array.Empty<StyleRule>())
            .Concat(rules)
            .Concat(SWTerminalStyle.GetRules(IoCManager.Resolve<IResourceCache>()))
            .ToArray());
        return _sheet;
    }
}
