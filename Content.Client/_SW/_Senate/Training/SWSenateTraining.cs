using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Full-screen Senate training over the lobby: a role list, a chapter list and the chapter playback. It only shows
/// text and pictures on this client; it sends nothing to the server and never touches the ready state.
/// Anything that changes the UI tree is queued and applied by the controller, outside the UI tree walk.
/// </summary>
public sealed partial class SWSenateTraining : LayoutContainer
{
    private const int ButtonCount = 6;
    private const float StatusRowHeight = 22f;
    private const float ButtonRowHeight = 36f;
    private const float BarSeparation = 4f;
    private const float BarEdge = 12f;
    private const float CompactEdge = 4f;

    private const string ManualHint = "Этот кадр не сменится сам. Чтобы перейти дальше, нажмите «Пропустить».";

    /// <summary>Where the shot's text is. In the last two modes the window cannot show it whole, so it scrolls.</summary>
    private enum TextMode
    {
        /// <summary>Drawn on a plate; the shots change by themselves.</summary>
        Drawn,
        /// <summary>In a scroll area above the button panel; the shots change by hand.</summary>
        Scroll,
        /// <summary>Too low even for that: text and buttons share one scroll area; the shots change by hand.</summary>
        Compact,
    }

    private readonly string? _recommendedJob;
    private readonly bool _reducedMotion;

    private readonly BoxContainer _menu;
    private readonly Label _menuHeading;
    private readonly BoxContainer _rows;
    private readonly Label _menuStatus;
    private readonly Button _menuBack;

    private readonly PanelContainer _textArea;
    private readonly BoxContainer _bar;
    private readonly Label _shotHeading;
    private readonly Label _barStatus;
    private readonly GridContainer _buttons;
    private readonly Button _previous;
    private readonly Button _pause;

    private SWTrainingRole? _role;
    private SWTrainingChapter? _chapter;
    private int _shotIndex;
    private float _shotTime;
    private bool _paused;
    private bool _chapterDone;
    private Action? _pending;
    private int _barColumns = -1;
    private float _barHeight = -1f;
    private float _barReserve = 96f;

    private TextMode _textMode = TextMode.Drawn;
    private TextMode _wantedMode = TextMode.Drawn;
    private (string Chapter, int Shot, TextMode Mode)? _shownText;

    /// <summary>The player asked to leave the training. The controller removes the control.</summary>
    public event Action? CloseRequested;

    public SWSenateTraining(string? recommendedJob, bool reducedMotion)
    {
        _recommendedJob = recommendedJob;
        _reducedMotion = reducedMotion;
        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        LoadStaging();

        _menu = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        AddChild(_menu);
        SetAnchorPreset(_menu, LayoutPreset.Wide);
        SetMarginLeft(_menu, 24);
        SetMarginRight(_menu, -24);
        SetMarginTop(_menu, 16);
        SetMarginBottom(_menu, -12);

        _menuHeading = new Label { FontOverride = _titleFont, FontColorOverride = SWConsoleStyle.TextBright, HorizontalExpand = true, ClipText = true };
        _menu.AddChild(_menuHeading);
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        _rows = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 2 };
        scroll.AddChild(_rows);
        _menu.AddChild(scroll);

        var footer = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10 };
        _menuStatus = new Label { VAlign = Label.VAlignMode.Center, FontColorOverride = SWConsoleStyle.TextDim, HorizontalExpand = true, ClipText = true };
        footer.AddChild(_menuStatus);
        _menuBack = new Button { Text = "Назад" };
        _menuBack.OnPressed += _ => _pending = ShowRoles;
        footer.AddChild(_menuBack);
        var menuExit = new Button { Text = "Выйти" };
        menuExit.OnPressed += _ => CloseRequested?.Invoke();
        footer.AddChild(menuExit);
        _menu.AddChild(footer);

        _textArea = new PanelContainer
        {
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PlateFill,
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8,
            },
        };
        AddChild(_textArea);
        SetAnchorPreset(_textArea, LayoutPreset.Wide);

        _bar = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = (int) BarSeparation, Visible = false };
        AddChild(_bar);
        SetAnchorPreset(_bar, LayoutPreset.BottomWide);
        SetMarginLeft(_bar, 24);
        SetMarginRight(_bar, -24);

        var statusRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10 };
        _shotHeading = new Label { VAlign = Label.VAlignMode.Center, FontColorOverride = SWConsoleStyle.TextBright, HorizontalExpand = true, ClipText = true };
        statusRow.AddChild(_shotHeading);
        _barStatus = new Label { VAlign = Label.VAlignMode.Center, FontColorOverride = SWConsoleStyle.TextDim };
        statusRow.AddChild(_barStatus);
        _bar.AddChild(statusRow);

        _buttons = new GridContainer { Columns = ButtonCount, HorizontalExpand = true };
        _bar.AddChild(_buttons);
        AddBarButton("К главам", () => _pending = ReturnToChapters);
        _previous = AddBarButton("Назад", PreviousShot);
        _pause = AddBarButton("Пауза", () => _paused = !_paused);
        AddBarButton("Пропустить", NextShot);
        AddBarButton("С начала", RestartChapter);
        AddBarButton("Выйти", () => CloseRequested?.Invoke());

        ShowRoles();
    }

    private Button AddBarButton(string text, Action pressed)
    {
        var button = new Button { Text = text, HorizontalExpand = true };
        button.OnPressed += _ => pressed();
        _buttons.AddChild(button);
        return button;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        GrabKeyboardFocus();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.CloseModals)
            return;

        args.Handle();
        if (_chapter != null)
            _pending = ReturnToChapters;
        else if (_role != null)
            _pending = ShowRoles;
        else
            CloseRequested?.Invoke();
    }

    /// <summary>Runs queued navigation and text placement. Called by the controller, outside the UI tree walk.</summary>
    public void ApplyPending()
    {
        if (_pending is { } pending)
        {
            _pending = null;
            pending();
        }

        SyncText();
    }

    /// <summary>Mirrors the lobby countdown as plain text. It does not offer any lobby action.</summary>
    public void UpdateStatus(string status)
    {
        if (_menuStatus.Text != status)
            _menuStatus.Text = status;
        if (_barStatus.Text != status)
            _barStatus.Text = status;
    }

    // ---- lists ------------------------------------------------------------

    private void ShowRoles()
    {
        _role = null;
        _chapter = null;
        SetPlaying(false);
        _menuBack.Visible = false;
        _menuHeading.Text = "Обучение Сената: выберите занятие";
        _rows.RemoveAllChildren();
        _rows.AddChild(SWConsoleStyle.Wrap(
            "Сначала посмотрите общий выпуск, затем главы своего занятия. Обучение только показывает порядок действий: оно не назначает должность и не меняет готовность.",
            SWConsoleStyle.TextDim));

        foreach (var role in SWSenateTrainingScript.Roles)
        {
            if (role.JobId != null && role.JobId == _recommendedJob)
                AddRoleRow(role, "Выбрано в настройках персонажа. Это не гарантирует назначение на должность.");
        }

        foreach (var role in SWSenateTrainingScript.Roles)
        {
            if (role.JobId == null || role.JobId != _recommendedJob)
                AddRoleRow(role, null);
        }

        GrabKeyboardFocus();
    }

    private void AddRoleRow(SWTrainingRole role, string? description)
    {
        _rows.AddChild(MakeRow(role.Title, description, "Выбрать", () => ShowChapters(role)));
    }

    private void ShowChapters(SWTrainingRole role)
    {
        _role = role;
        _chapter = null;
        SetPlaying(false);
        _menuBack.Visible = true;
        _menuHeading.Text = $"Главы: {role.Title}";
        _rows.RemoveAllChildren();
        foreach (var id in role.ChapterIds)
        {
            if (SWSenateTrainingScript.FindChapter(id) is not { } chapter)
                continue;

            _rows.AddChild(MakeRow($"Глава {chapter.Id}. {chapter.Title}", chapter.Goal, "Смотреть", () => Play(chapter)));
        }

        GrabKeyboardFocus();
    }

    /// <summary>Row text wraps in full; only the fixed-size button sits beside it, so narrow windows lose nothing.</summary>
    private Control MakeRow(string title, string? description, string action, Action pressed)
    {
        var row = SWConsoleStyle.Row();
        var box = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10, HorizontalExpand = true };
        row.AddChild(box);
        var text = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2, HorizontalExpand = true };
        text.AddChild(SWConsoleStyle.Wrap(title, SWConsoleStyle.TextBright));
        if (description != null)
            text.AddChild(SWConsoleStyle.Wrap(description, SWConsoleStyle.TextDim));
        box.AddChild(text);
        var button = new Button { Text = action, VerticalAlignment = VAlignment.Center };
        button.OnPressed += _ => _pending = pressed;
        box.AddChild(button);
        return row;
    }

    private void SetPlaying(bool playing)
    {
        if (!playing)
        {
            _wantedMode = TextMode.Drawn;
            SetTextMode(TextMode.Drawn);
        }

        _menu.Visible = !playing;
        _bar.Visible = playing;
    }

    // ---- playback ---------------------------------------------------------

    private void Play(SWTrainingChapter chapter)
    {
        _chapter = chapter;
        _shotIndex = 0;
        _shotTime = 0f;
        _paused = false;
        _chapterDone = false;
        _layoutKey = null;
        SetPlaying(true);
        GrabKeyboardFocus();
    }

    private void ReturnToChapters()
    {
        if (_role != null)
            ShowChapters(_role);
        else
            ShowRoles();
    }

    private void PreviousShot()
    {
        if (_chapter == null)
            return;

        _chapterDone = false;
        _pending = null;
        _shotIndex = Math.Max(0, _shotIndex - 1);
        _shotTime = 0f;
    }

    private void NextShot()
    {
        if (_chapter == null || _chapterDone)
            return;

        if (_shotIndex + 1 < _chapter.Shots.Length)
        {
            _shotIndex++;
            _shotTime = 0f;
            return;
        }

        _chapterDone = true;
        _pending = ReturnToChapters;
    }

    private void RestartChapter()
    {
        if (_chapter == null)
            return;

        _chapterDone = false;
        _pending = null;
        _paused = false;
        _shotIndex = 0;
        _shotTime = 0f;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_chapter == null)
            return;

        FitBar();
        var manual = _textMode != TextMode.Drawn;
        if (!_chapterDone && !_paused)
        {
            var duration = SWSenateTrainingScript.ShotDuration(_chapter, _chapter.Shots[_shotIndex]);
            _shotTime += args.DeltaSeconds;
            // Text that may need scrolling is never taken away: the shot waits, short of its closing fade.
            if (manual)
                _shotTime = MathF.Min(_shotTime, duration - EdgeFade);
            else if (_shotTime >= duration)
                NextShot();
        }

        _previous.Disabled = _shotIndex == 0;
        _pause.Disabled = manual;
        var pauseText = _paused ? "Продолжить" : "Пауза";
        if (_pause.Text != pauseText)
            _pause.Text = pauseText;

        var count = _chapter.Shots.Length;
        var heading = Width >= 620f
            ? $"Глава {_chapter.Id}. {_chapter.Title}, кадр {_shotIndex + 1} из {count}"
            : $"Глава {_chapter.Id}, кадр {_shotIndex + 1} из {count}";
        if (_shotHeading.Text != heading)
            _shotHeading.Text = heading;
    }

    /// <summary>Lays the buttons out in as many rows as the window needs and reserves their real height.</summary>
    private void FitBar()
    {
        var columns = Width >= 720f ? ButtonCount : Width >= 400f ? 3 : Width >= 220f ? 2 : 1;
        if (columns != _barColumns)
        {
            _barColumns = columns;
            _buttons.Columns = columns;
        }

        var rows = (ButtonCount + columns - 1) / columns;
        var height = StatusRowHeight + BarSeparation + rows * ButtonRowHeight + (rows - 1) * BarSeparation;
        // Buttons may measure taller than planned, for instance with another font.
        height = MathF.Max(height, _bar.DesiredSize.Y);
        if (MathHelper.CloseTo(height, _barHeight))
            return;

        _barHeight = height;
        SetMarginTop(_bar, -(height + BarEdge));
        SetMarginBottom(_bar, -BarEdge);
        _barReserve = height + BarEdge * 2f;
    }

    // ---- scrolling text ---------------------------------------------------

    private void SetTextMode(TextMode mode)
    {
        _textMode = mode;
        _shownText = null;
        _textArea.Visible = mode != TextMode.Drawn;
        if (mode != TextMode.Compact)
            RestoreBar();
        if (mode == TextMode.Drawn)
            _textArea.RemoveAllChildren();
    }

    /// <summary>Returns the button panel to its fixed place above the window's bottom edge.</summary>
    private void RestoreBar()
    {
        if (_bar.Parent == this)
            return;

        _bar.Orphan();
        AddChild(_bar);
    }

    /// <summary>Applies the mode the last layout chose and keeps the scroll area on the current shot.</summary>
    private void SyncText()
    {
        if (_chapter == null)
            return;

        if (_wantedMode != _textMode)
            SetTextMode(_wantedMode);

        if (_textMode == TextMode.Drawn)
            return;

        var ui = UIScale;
        var compact = _textMode == TextMode.Compact;
        SetMarginLeft(_textArea, _layout.PlateLeft / ui);
        SetMarginRight(_textArea, -_layout.PlateLeft / ui);
        SetMarginTop(_textArea, _layout.PlateAnchor / ui);
        SetMarginBottom(_textArea, compact ? -CompactEdge : -_barReserve);

        var shown = (_chapter.Id, _shotIndex, _textMode);
        if (_shownText == shown)
            return;

        _shownText = shown;
        _bar.Orphan();
        _textArea.RemoveAllChildren();

        // A new scroll area per shot always opens at the top of the text.
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
        var shot = _chapter.Shots[_shotIndex];
        box.AddChild(SWConsoleStyle.Wrap(shot.Title, SWConsoleStyle.Brass));
        box.AddChild(SWConsoleStyle.Wrap(shot.Text, SWConsoleStyle.TextBright));
        box.AddChild(SWConsoleStyle.Wrap(shot.Note, SWConsoleStyle.TextDim));
        box.AddChild(SWConsoleStyle.Wrap(ManualHint, SWConsoleStyle.TextDim));
        if (compact)
            box.AddChild(_bar);
        else
            AddChild(_bar);

        scroll.AddChild(box);
        _textArea.AddChild(scroll);
    }
}
