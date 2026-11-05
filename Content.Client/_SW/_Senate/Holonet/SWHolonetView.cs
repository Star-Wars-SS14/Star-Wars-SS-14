using System.Numerics;
using Content.Client._SW._Senate.Consoles.SectorSelect;
using Content.Shared._SW._Senate.Holonet;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Holonet;

/// <summary>
/// Portal shell shared by the terminal and the PDA; the PDA uses the same navigation in a single column.
/// All player text reaches the screen as a Label or through FormattedMessage.AddText, never as markup.
/// </summary>
public sealed partial class SWHolonetView : PanelContainer
{
    private const string CoruscantSector = "SWSectorCoruscant";

    public readonly NetEntity Device;
    private readonly bool _compact;
    private readonly Action<SWHolonetRequest> _send;
    private readonly float _textWidth;
    private readonly SWHolonetNavigateRequest _nav = new();

    private readonly BoxContainer _content;
    private readonly ScrollContainer _scroll;
    private readonly Label _persona;
    private readonly Label _role;
    private readonly Label _notice;
    private readonly PanelContainer _noticeBar;
    private readonly SWHolonetTicker _ticker = new();
    private readonly SWSectorMiniMap _map = new();
    private readonly SWHolonetAdvert _advert = new();
    private readonly Dictionary<SWHolonetSection, Button> _tabs = new();

    private SWHolonetStateEvent? _state;

    /// <summary>The private context on screen; zero when only public content may be shown.</summary>
    private ulong _context;

    /// <summary>Raised on every hide. Private states built before the server heard of it are dropped.</summary>
    private ulong _epoch;
    private SWHolonetClientSystem? _holonet;
    private Vector2? _pendingScroll;
    private int _pendingScrollFrames;

    public SWHolonetView(NetEntity device, bool compact, Action<SWHolonetRequest> send)
    {
        Device = device;
        _compact = compact;
        _send = send;
        _textWidth = compact ? 500f : 600f;
        HorizontalExpand = true;
        VerticalExpand = true;
        SWConsoleStyle.StyleRoot(this);
        PanelOverride = new StyleBoxFlat { BackgroundColor = SWHolonetPalette.Background };
        _map.Select(CoruscantSector);
        _map.MinHeight = compact ? 110 : 130;

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        AddChild(root);
        root.AddChild(new PanelContainer
        {
            MinHeight = 4,
            PanelOverride = new StyleBoxFlat { BackgroundColor = SWHolonetPalette.Mint },
        });

        var header = new BoxContainer
        {
            Orientation = compact ? BoxContainer.LayoutOrientation.Vertical : BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(16, 8, 16, 6),
            SeparationOverride = compact ? 4 : 20,
        };
        root.AddChild(header);

        var identity = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        // The brand is the way back to the home page, which has no tab of its own in the design.
        var brandButton = new ContainerButton { HorizontalExpand = compact };
        var brand = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        brand.AddChild(SWConsoleStyle.Text(Loc.GetString("sw-holonet-brand"), SWHolonetPalette.Bright, SWConsoleStyle.Title));
        brand.AddChild(SWConsoleStyle.Text(Loc.GetString("sw-holonet-brand-subtitle"), SWHolonetPalette.Dim, SWConsoleStyle.Small));
        brandButton.AddChild(brand);
        brandButton.OnPressed += _ => OpenSection(SWHolonetSection.Home);
        identity.AddChild(brandButton);
        header.AddChild(identity);

        var tabs = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = !compact,
        };
        AddTab(tabs, SWHolonetSection.News, Loc.GetString("sw-holonet-tab-news"));
        AddTab(tabs, SWHolonetSection.Forum, Loc.GetString("sw-holonet-tab-forum"));
        AddTab(tabs, SWHolonetSection.Mail, Loc.GetString("sw-holonet-tab-mail"));

        var channel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Center,
            // A clipping label measures as zero width, so the channel block needs its own fixed width.
            SetWidth = compact ? 170 : 190,
        };
        _role = SWConsoleStyle.Text(string.Empty, SWHolonetPalette.Dim, SWConsoleStyle.Small);
        _role.ClipText = true;
        _persona = SWConsoleStyle.Text(string.Empty, SWHolonetPalette.Bright, SWConsoleStyle.Strong);
        _persona.ClipText = true;
        channel.AddChild(_role);
        channel.AddChild(_persona);

        if (compact)
        {
            identity.AddChild(channel);
            header.AddChild(tabs);
        }
        else
        {
            header.AddChild(tabs);
            header.AddChild(channel);
        }

        root.AddChild(Rule());

        _notice = SWConsoleStyle.Text(string.Empty, SWHolonetPalette.Warn, SWConsoleStyle.Strong);
        _notice.ClipText = true;
        _noticeBar = new PanelContainer
        {
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = SWHolonetPalette.Panel,
                ContentMarginLeftOverride = 16,
                ContentMarginRightOverride = 16,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4,
            },
        };
        _noticeBar.AddChild(_notice);
        root.AddChild(_noticeBar);

        _scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(16, 10, 16, 10),
            SeparationOverride = 6,
        };
        _scroll.AddChild(_content);
        root.AddChild(_scroll);
        root.AddChild(_ticker);
        _ticker.SetItems(Array.Empty<string>());
        Show(new Label { Text = Loc.GetString("sw-holonet-connecting"), FontColorOverride = SWHolonetPalette.Dim });
    }

    private void AddTab(BoxContainer tabs, SWHolonetSection section, string title)
    {
        var button = SWConsoleStyle.TabButton(title);
        SWConsoleStyle.StyleTabButton(button, false);
        button.OnPressed += _ => OpenSection(section);
        _tabs[section] = button;
        tabs.AddChild(button);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _holonet = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SWHolonetClientSystem>();
        _holonet.Register(this);
        _epoch = _holonet.NextEpoch();
        SendNavigation();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        // A view can outlive a client shutdown order; only look the system up while it still exists.
        if (IoCManager.Resolve<IEntitySystemManager>().TryGetEntitySystem<SWHolonetClientSystem>(out var system))
            system.Unregister(this);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateDraftTimeouts(args.DeltaSeconds);

        // Restored only after layout has measured the rebuilt content.
        if (_pendingScroll is not { } value || --_pendingScrollFrames > 0)
            return;
        _scroll.SetScrollValue(value);
        _pendingScroll = null;
    }

    public void Apply(SWHolonetStateEvent state)
    {
        if (state.Private)
        {
            // An answer to a view already hidden, or a number older than the current one, is simply dropped.
            if (state.Epoch != _epoch || state.Context < _context)
                return;

            _context = state.Context;
        }
        else if (_context != 0)
        {
            // Normally preceded by the revocation notice; this covers a state that overtakes nothing else.
            OnContextRevoked(_context);
        }

        var scroll = _scroll.GetScrollValue();
        var sameScreen = _state != null && _state.Available == state.Available && _state.Section == state.Section
            && _state.Article?.Id == state.Article?.Id && _state.Thread?.Id == state.Thread?.Id
            && _state.Message?.Id == state.Message?.Id && _state.ActiveBox == state.ActiveBox
            && _state.NewsPage == state.NewsPage && _state.ForumPage == state.ForumPage
            && _state.ThreadPage == state.ThreadPage && _state.MailPage == state.MailPage;
        _state = state;

        // Must come first: the previous character's drafts may not survive into any rendering below.
        ApplyPersona(state);
        if (state.Available)
            SyncNavigation(state);
        UpdateChrome(state);

        var formClosed = ApplyAcknowledgement(state);
        if (!state.Available)
        {
            RenderDenied(state);
            return;
        }

        if (!state.Private)
            SuspendForm();

        // Rebuilding a form would discard focus and the text being typed.
        if (FormOpen && !formClosed)
        {
            UpdateForm(state);
            return;
        }

        if (state.Private && TryResumeForm())
            return;

        Render(state);
        if (sameScreen && !formClosed)
        {
            _pendingScroll = scroll;
            _pendingScrollFrames = 2;
        }
    }

    /// <summary>The server may have redirected or clamped the view, so it is the source of truth.</summary>
    private void SyncNavigation(SWHolonetStateEvent state)
    {
        _nav.Section = state.Section;
        switch (state.Section)
        {
            case SWHolonetSection.News:
                _nav.NewsPage = state.NewsPage;
                _nav.ArticleId = state.Article?.Id ?? 0;
                break;
            case SWHolonetSection.Forum:
                _nav.ForumPage = state.ForumPage;
                _nav.ThreadId = state.Thread?.Id ?? 0;
                _nav.ThreadPage = state.ThreadPage;
                break;
            case SWHolonetSection.Mail when state.Private:
                _nav.MailBox = state.ActiveBox;
                _nav.Folder = state.Folder;
                _nav.MailPage = state.MailPage;
                _nav.MessageId = state.Message?.Id ?? 0;
                _nav.DirectoryPage = state.DirectoryPage;
                break;
        }
    }

    private void UpdateChrome(SWHolonetStateEvent state)
    {
        _persona.Text = state.Persona;
        _role.Text = state.Available ? state.Role : string.Empty;
        _notice.Text = state.Notice;
        _noticeBar.Visible = state.Notice.Length > 0;
        _ticker.SetItems(state.Ticker);
        // As in the design, the home page is the news front page, so its tab stays lit there.
        var lit = state.Section == SWHolonetSection.Home ? SWHolonetSection.News : state.Section;
        foreach (var (section, button) in _tabs)
            SWConsoleStyle.StyleTabButton(button, state.Available && section == lit);
    }

    private void Render(SWHolonetStateEvent state)
    {
        ClearContent();
        switch (state.Section)
        {
            case SWHolonetSection.News:
                RenderNews(state);
                break;
            case SWHolonetSection.Forum:
                RenderForum(state);
                break;
            case SWHolonetSection.Mail:
                RenderMail(state);
                break;
            default:
                RenderHome(state);
                break;
        }
    }

    private void RenderDenied(SWHolonetStateEvent state)
    {
        // Leaving any form also removes previously delivered private text from the screen; drafts remain.
        SuspendForm();
        ClearContent();
        Show(Eyebrow(Loc.GetString("sw-holonet-denied-title")));
        Show(Paragraph(state.Reason, SWHolonetPalette.Dim));
    }

    private void ClearContent()
    {
        // Long-lived animated parts keep their state instead of replaying their intro on every update.
        _map.Orphan();
        _advert.Orphan();
        _content.DisposeAllChildren();
    }

    private void Show(Control control) => _content.AddChild(control);

    private void SendNavigation()
    {
        // Without a context the mail fields below are ignored by the server, and they are empty anyway.
        _send(new SWHolonetNavigateRequest
        {
            Context = _context,
            Epoch = _epoch,
            Section = _nav.Section,
            ArticleId = _nav.ArticleId,
            NewsPage = _nav.NewsPage,
            ThreadId = _nav.ThreadId,
            ThreadPage = _nav.ThreadPage,
            ForumPage = _nav.ForumPage,
            MailBox = _nav.MailBox,
            Folder = _nav.Folder,
            MailPage = _nav.MailPage,
            MessageId = _nav.MessageId,
            WantDirectory = _nav.WantDirectory,
            Search = _nav.Search,
            DirectoryPage = _nav.DirectoryPage,
        });
    }

    private void Go(Action<SWHolonetNavigateRequest> change)
    {
        change(_nav);
        SendNavigation();
    }

    private void OpenSection(SWHolonetSection section)
    {
        StoreForm();
        _activeDraft = null;
        _resumeDraft = null;
        Go(n =>
        {
            n.Section = section;
            n.ArticleId = 0;
            n.ThreadId = 0;
            n.MessageId = 0;
            n.WantDirectory = false;
        });
    }

    private static Label Eyebrow(string text, Color? color = null) =>
        SWConsoleStyle.Text(text, color ?? SWHolonetPalette.Mint, SWConsoleStyle.Strong);

    private static Label Caption(string text, Color? color = null)
    {
        var label = SWConsoleStyle.Text(text, color ?? SWHolonetPalette.Dim, SWConsoleStyle.Small);
        label.ClipText = true;
        return label;
    }

    private Control Paragraph(string text, Color? color = null) =>
        SWConsoleStyle.Wrap(text, color ?? SWHolonetPalette.Bright, _textWidth);

    /// <summary>Metadata that holds player text and must wrap instead of being cut off.</summary>
    private Control Detail(string text, Color? color = null) =>
        SWConsoleStyle.Wrap(text, color ?? SWHolonetPalette.Dim, _textWidth);

    private Control Byline(string text) => Rich(text, SWHolonetPalette.Gold, _textWidth, true);

    private static Control Rich(string text, Color color, float width, bool bold)
    {
        var label = new SWConsoleStyle.WrappingRichTextLabel(width)
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Top,
        };
        var message = new FormattedMessage();
        message.PushColor(color);
        if (bold)
            message.PushTag(new MarkupNode("bold", null, null));
        message.AddText(text);
        if (bold)
            message.Pop();
        message.Pop();
        label.SetMessage(message);
        return label;
    }

    private Control SideParagraph(string text, Color? color = null) =>
        SWConsoleStyle.Wrap(text, color ?? SWHolonetPalette.Bright, 220f);

    /// <summary>Wrapping heading: the tag is built in code, and the player text stays literal.</summary>
    private Control Heading(string text, int level, float? width = null)
    {
        var label = new SWConsoleStyle.WrappingRichTextLabel(width ?? _textWidth)
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Top,
        };
        var message = new FormattedMessage();
        message.PushColor(SWHolonetPalette.Bright);
        message.PushTag(new MarkupNode("head", new MarkupParameter((long) level), null));
        message.AddText(text);
        message.Pop();
        message.Pop();
        label.SetMessage(message);
        return label;
    }

    private static PanelContainer Rule() => new()
    {
        MinHeight = 1,
        Margin = new Thickness(0, 3, 0, 3),
        PanelOverride = new StyleBoxFlat { BackgroundColor = SWHolonetPalette.Edge },
    };

    private static PanelContainer Card(Control child, Color? background = null)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = background ?? SWHolonetPalette.Panel,
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8,
            },
        };
        panel.AddChild(child);
        return panel;
    }

    private static BoxContainer Column(float minWidth = 0f, bool expand = false) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        MinWidth = minWidth,
        MaxWidth = expand || minWidth <= 0f ? float.PositiveInfinity : minWidth,
        HorizontalExpand = expand,
        SeparationOverride = 6,
    };

    private static BoxContainer Row() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        HorizontalExpand = true,
        SeparationOverride = 12,
    };

    private static Button Primary(string text, Action onPressed, bool disabled = false)
    {
        var button = new Button
        {
            Text = text,
            Disabled = disabled,
            HorizontalAlignment = HAlignment.Left,
            VerticalAlignment = VAlignment.Center,
        };
        button.ModulateSelfOverride = SWHolonetPalette.Gold;
        button.Label.FontOverride = SWConsoleStyle.Strong;
        button.Label.FontColorOverride = SWHolonetPalette.Background;
        button.OnPressed += _ => onPressed();
        return button;
    }

    private static Button Quiet(string text, Action onPressed, bool disabled = false)
    {
        var button = SWConsoleStyle.Action(text, SWHolonetPalette.Mint, disabled);
        button.HorizontalAlignment = HAlignment.Left;
        button.VerticalAlignment = VAlignment.Center;
        button.OnPressed += _ => onPressed();
        return button;
    }

    /// <summary>A selectable row: the title wraps in full, while the secondary line clips to keep rows compact.</summary>
    private static ContainerButton Entry(string title, string detail, bool selected, bool emphasize, Action onPressed)
    {
        var button = new ContainerButton { HorizontalExpand = true };
        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        text.AddChild(Rich(title, emphasize ? SWHolonetPalette.Gold : SWHolonetPalette.Bright, 220f, true));
        text.AddChild(Caption(detail));
        button.AddChild(Card(text, selected ? SWHolonetPalette.Selected : SWHolonetPalette.Panel));
        button.OnPressed += _ => onPressed();
        return button;
    }

    private static BoxContainer Pager(int page, int pages, Action<int> open)
    {
        var row = Row();
        if (pages <= 1)
            return row;
        row.AddChild(Quiet(Loc.GetString("sw-holonet-back"), () => open(page - 1), page <= 0));
        // Not a clipping caption: in a horizontal row that would measure as zero width and vanish.
        var position = SWConsoleStyle.Text(Loc.GetString("sw-holonet-page", ("page", page + 1), ("pages", pages)),
            SWHolonetPalette.Dim, SWConsoleStyle.Small);
        position.VerticalAlignment = VAlignment.Center;
        row.AddChild(position);
        row.AddChild(Quiet(Loc.GetString("sw-holonet-next"), () => open(page + 1), page >= pages - 1));
        return row;
    }
}
