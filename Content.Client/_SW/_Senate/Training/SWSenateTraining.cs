using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;
using TextMode = Content.Client._SW._Senate.Training.SWTrainingTextMode;

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
    private const float CompactEdge = SWTrainingPageLayout.CompactEdge;

    private const string ManualHint = "Этот кадр не сменится сам. «Пропустить» показывает следующий шаг или кадр, «Назад» возвращает предыдущий.";

    private readonly string? _recommendedJob;
    private readonly bool _reducedMotion;

    private readonly BoxContainer _menu;
    private readonly Label _menuHeading;
    private readonly BoxContainer _rows;
    private readonly Label _menuStatus;
    private readonly Button _menuBack;

    private readonly PanelContainer _textArea;
    private readonly PanelContainer _panelArea;
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
    private (string Chapter, int Shot, int Stage, TextMode Mode)? _shownText;
    private SWTrainingPanelState? _shownPanel;
    private PanelHost? _panelHost;
    private Control? _panelTarget;

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

        _panelArea = new PanelContainer { Visible = false, RectClipContent = true };
        AddChild(_panelArea);
        SetAnchorPreset(_panelArea, LayoutPreset.TopLeft);

        // Clock, remarks, pointer and popups belong above the illustration, which is a child control.
        var topLayer = new TopLayer(this);
        AddChild(topLayer);
        SetAnchorPreset(topLayer, LayoutPreset.Wide);

        _bar =new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = (int) BarSeparation, Visible = false };
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
        _previous = AddBarButton("Назад", StepBack);
        _pause = AddBarButton("Пауза", () => _paused = !_paused);
        AddBarButton("Пропустить", StepForward);
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
        SyncPanelArea();
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
            "Сначала посмотрите общий выпуск, затем главы своего занятия.",
            SWConsoleStyle.TextDim));

        foreach (var role in SWSenateTrainingScript.Roles)
        {
            if (role.JobId != null && role.JobId == _recommendedJob)
                AddRoleRow(role, "Выбрано в настройках персонажа.");
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
            ClearPanelArea();
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

    // ---- stages -----------------------------------------------------------

    private SWTrainingStage[]? CurrentStages => _chapter?.Shots[_shotIndex].Stages;

    private float StageStart(SWTrainingStage stage) => SWSenateTrainingScript.StageStart(stage, _reducedMotion);

    /// <summary>The stage follows from the shot's time alone, so every control below keeps it consistent.</summary>
    private int CurrentStageIndex() =>
        CurrentStages is { } stages ? SWSenateTrainingScript.StageIndexAt(stages, _shotTime, _reducedMotion) : 0;

    private SWTrainingStage? CurrentStage => CurrentStages is { } stages ? stages[CurrentStageIndex()] : null;

    /// <summary>While the player holds the time, the buttons walk through the stages instead of skipping them.</summary>
    private bool StepsThroughStages => _paused || _textMode != TextMode.Drawn;

    private void StepBack()
    {
        if (_chapter == null)
            return;

        _chapterDone = false;
        _pending = null;
        var stages = CurrentStages;
        var index = CurrentStageIndex();
        if (StepsThroughStages && stages != null && index > 0)
        {
            _shotTime = SWSenateTrainingScript.BackWithinShot(stages, index, _reducedMotion);
            return;
        }

        _shotIndex = Math.Max(0, _shotIndex - 1);
        _shotTime = 0f;
        if (StepsThroughStages && CurrentStages is { } previous)
            _shotTime = StageStart(previous[^1]);
    }

    private void StepForward()
    {
        if (_chapter == null || _chapterDone)
            return;

        var stages = CurrentStages;
        var index = CurrentStageIndex();
        if (StepsThroughStages && stages != null && index + 1 < stages.Length)
        {
            _shotTime = StageStart(stages[index + 1]);
            return;
        }

        NextShot();
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
        if (!_chapterDone)
        {
            // The same rules the contract plays through: a pause keeps the time, no frame passes over a stage, and the
            // manual mode waits in its stage, short of the closing fade, for the player.
            var (time, ended) = SWSenateTrainingScript.Advance(_chapter, _chapter.Shots[_shotIndex], _shotTime, args.DeltaSeconds,
                _reducedMotion, manual, _paused);
            _shotTime = time;
            if (ended)
                NextShot();
        }

        _previous.Disabled = _shotIndex == 0 && (!StepsThroughStages || CurrentStageIndex() == 0);
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

        var stage = CurrentStage;
        var shown = (_chapter.Id, _shotIndex, CurrentStageIndex(), _textMode);
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
        if (shot.Note.Length > 0)
            box.AddChild(SWConsoleStyle.Wrap(shot.Note, SWConsoleStyle.TextDim));
        if (stage != null && CurrentStages is { } stages)
            AddStageToScroll(box, stages, CurrentStageIndex());
        box.AddChild(SWConsoleStyle.Wrap(ManualHint, SWConsoleStyle.TextDim));
        if (compact)
            box.AddChild(_bar);
        else
            AddChild(_bar);

        scroll.AddChild(box);
        _textArea.AddChild(scroll);
    }

    /// <summary>
    /// In the scroll modes everything the stage says joins the text, whether or not a small picture is shown above it,
    /// so the text does not depend on the picture and cannot go stale when a resize adds or removes it.
    /// </summary>
    private void AddStageToScroll(BoxContainer box, SWTrainingStage[] stages, int index)
    {
        var stage = stages[index];
        // The same words as on the picture: game time, remarks, the popup for the whole stage, and the action in words.
        foreach (var (text, tone) in SWTrainingPageLayout.StageScrollLines(stages, index))
        {
            var color = tone switch
            {
                SWTrainingScrollTone.Teaching or SWTrainingScrollTone.Popup => SWConsoleStyle.Brass,
                SWTrainingScrollTone.Action => SWConsoleStyle.Accent,
                _ => SWConsoleStyle.TextBright,
            };
            box.AddChild(SWConsoleStyle.Wrap(text, color));
        }

        if (stage.Panel == null)
            return;

        box.AddChild(SWTrainingUiPanels.Build(stage.Panel, out _, stage.Pointer != SWTrainingPointer.None));
    }

    // ---- illustration area ------------------------------------------------

    private void ClearPanelArea()
    {
        _panelArea.Visible = false;
        _panelArea.RemoveAllChildren();
        _shownPanel = null;
        _panelHost = null;
        _panelTarget = null;
    }

    /// <summary>Keeps the illustration in its part of the frame; it never reaches the figures or the text zone.</summary>
    private void SyncPanelArea()
    {
        var stage = CurrentStage;
        var panel = stage?.Panel;
        if (_chapter == null || stage == null || panel == null || _layoutKey is not { } key || _textMode != TextMode.Drawn || _layout.Mode != TextMode.Drawn || _layout.Pixel <= 0f)
        {
            ClearPanelArea();
            return;
        }

        var ui = UIScale;
        // The same rectangle the layout kept the clock and the remarks clear of.
        var rect = SWTrainingPageLayout.PanelRectOnScreen(stage.Visual.Scene, panel, _layout.Canvas, _layout.Pixel, ui);
        SetMarginLeft(_panelArea, rect.Left / ui);
        SetMarginRight(_panelArea, rect.Right / ui);
        SetMarginTop(_panelArea, rect.Top / ui);
        SetMarginBottom(_panelArea, rect.Bottom / ui);
        _panelArea.Visible = true;

        // The target must be visible without scrolling. If this window cannot hold the panel, the chapter moves to
        // the scroll area, where the action is also said in words.
        if (_panelHost is { NeededHeight: > 0f } host && host.Height > 1f && host.NeededHeight > host.Height + 1f)
        {
            _overflowFor = (key.Chapter, key.Size, key.Ui);
            _layoutKey = null;
            return;
        }

        // Stages with the same state keep their controls, so the pointer's target does not jump.
        if (panel == _shownPanel)
            return;

        _shownPanel = panel;
        _panelArea.RemoveAllChildren();
        _panelHost = new PanelHost(SWTrainingUiPanels.Build(panel, out _panelTarget));
        _panelArea.AddChild(_panelHost);
    }

    /// <summary>Measures the illustration at its full height, so a window too low for it can be recognised.</summary>
    private sealed class PanelHost : Container
    {
        private readonly Control _content;

        public float NeededHeight { get; private set; }

        public PanelHost(Control content)
        {
            _content = content;
            HorizontalExpand = true;
            VerticalExpand = true;
            AddChild(content);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _content.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
            NeededHeight = _content.DesiredSize.Y;
            return new Vector2(_content.DesiredSize.X, MathF.Min(NeededHeight, availableSize.Y));
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            _content.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
            return finalSize;
        }
    }

    /// <summary>Draws above the illustration. It takes no input.</summary>
    private sealed class TopLayer : Control
    {
        private readonly SWSenateTraining _owner;

        public TopLayer(SWSenateTraining owner)
        {
            _owner = owner;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            _owner.DrawTop(handle);
        }
    }
}
