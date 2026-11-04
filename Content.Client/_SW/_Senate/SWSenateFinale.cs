using System;
using System.Numerics;
using Content.Shared._SW._Senate;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._SW._Senate;

/// <summary>
/// Senate outro: a short pixel-art chronicle of the finished convocation, built from the frozen round history.
/// Facts are shown in a separate panel under the panorama and are never baked into the art. The control only asks to
/// finish; the round-end controller removes it outside the UI tree walk and opens the full report.
/// </summary>
public sealed partial class SWSenateFinale : LayoutContainer
{
    private static readonly Color Backdrop = Color.FromHex("#0A0F16");

    private static Font? _titleFont;
    private static Font? _noteFont;
    private static Font? _textFont;
    private static Font? _largeFont;

    private readonly Scene[] _scenes;
    private readonly float _total;
    private readonly bool _reducedMotion;

    private readonly PanelContainer _panel;
    private readonly BoxContainer _content;
    private readonly Label _title;
    private readonly Label _note;
    private readonly Label _more;
    private readonly ClipArea _clip;
    private readonly BoxContainer _rows;
    private readonly Label _status;
    private readonly Button _pause;

    private float _elapsed;
    private bool _paused;
    private bool _finished;
    private int _scene = -1;
    private int _page = -1;
    private string? _pageMore;
    private int _fitDelay;

    /// <summary>Raised once, when the outro ends, is skipped or Escape is pressed.</summary>
    public event Action? Finished;
    public event Action<bool>? PauseToggled;
    public event Action? Replayed;

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>($"/Fonts/NotoSans/NotoSans-{variant}.ttf"), size);

    public SWSenateFinale(SWSenateRoundHistory history, TimeSpan duration, bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        _scenes = BuildScenes(history, duration);
        _total = _scenes.Length == 0 ? 0f : MathF.Min(MaxDuration, _scenes[^1].End);
        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        _titleFont ??= LoadFont("Bold", 16);
        _noteFont ??= LoadFont("Regular", 12);
        _textFont ??= LoadFont("Regular", 14);
        _largeFont ??= LoadFont("Bold", 17);
        LoadArt();

        _panel = new PanelContainer
        {
            RectClipContent = true,
            MouseFilter = MouseFilterMode.Ignore,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0E1621"),
                BorderColor = SWConsoleStyle.Edge,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 16,
                ContentMarginRightOverride = 16,
                ContentMarginTopOverride = 10,
                ContentMarginBottomOverride = 10,
            },
        };
        AddChild(_panel);
        SetAnchorPreset(_panel, LayoutPreset.TopLeft);

        _content = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6, MouseFilter = MouseFilterMode.Ignore };
        _panel.AddChild(_content);
        // Header labels clip instead of widening the panel; long text belongs to the full report.
        _title = new Label { FontOverride = _titleFont, FontColorOverride = SWConsoleStyle.Brass, ClipText = true, HorizontalExpand = true };
        _content.AddChild(_title);
        var subtitle = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 12 };
        _note = new Label { FontOverride = _noteFont, FontColorOverride = SWConsoleStyle.TextDim, ClipText = true, HorizontalExpand = true, SizeFlagsStretchRatio = 3 };
        _more = new Label { FontOverride = _noteFont, FontColorOverride = SWConsoleStyle.Accent, ClipText = true, HorizontalExpand = true, SizeFlagsStretchRatio = 2, Align = Label.AlignMode.Right };
        subtitle.AddChild(_note);
        subtitle.AddChild(_more);
        _content.AddChild(subtitle);
        _clip = new ClipArea();
        _content.AddChild(_clip);
        _rows = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, MouseFilter = MouseFilterMode.Ignore };
        _clip.AddChild(_rows);

        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10 };
        AddChild(bar);
        SetAnchorPreset(bar, LayoutPreset.BottomWide);
        SetMarginLeft(bar, 24);
        SetMarginRight(bar, -24);
        SetMarginTop(bar, -56);
        SetMarginBottom(bar, -16);
        // The status label takes the slack and clips, so the buttons never fall off a narrow window.
        _status = new Label { VAlign = Label.VAlignMode.Center, FontColorOverride = SWConsoleStyle.TextDim, HorizontalExpand = true, ClipText = true };
        bar.AddChild(_status);
        _pause = new Button { Text = "Пауза" };
        _pause.OnPressed += _ => TogglePause();
        bar.AddChild(_pause);
        var replay = new Button { Text = "С начала" };
        replay.OnPressed += _ => Restart();
        bar.AddChild(replay);
        var report = new Button { Text = "Полный отчёт" };
        report.OnPressed += _ => Finish();
        bar.AddChild(report);
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
        Finish();
    }

    private void TogglePause()
    {
        if (_finished)
            return;

        _paused = !_paused;
        _pause.Text = _paused ? "Продолжить" : "Пауза";
        PauseToggled?.Invoke(_paused);
    }

    /// <summary>Plays the outro again from the first scene; the music restarts with it.</summary>
    public void Restart()
    {
        if (_finished)
            return;

        _elapsed = 0f;
        _scene = -1;
        _page = -1;
        if (_paused)
        {
            _paused = false;
            _pause.Text = "Пауза";
        }

        Replayed?.Invoke();
    }

    /// <summary>Asks to close the outro and open the full report. Safe to call more than once.</summary>
    public void Finish()
    {
        if (_finished)
            return;

        _finished = true;
        Finished?.Invoke();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_finished)
            return;

        UpdateLayout();
        if (!_paused)
            _elapsed += args.DeltaSeconds;

        if (_scenes.Length == 0 || _elapsed >= _total)
        {
            _elapsed = _total;
            Finish();
            return;
        }

        UpdatePage();
        FitRows();
    }

    // ---- fact panel -----------------------------------------------------

    private void UpdatePage()
    {
        var sceneIndex = SceneAt(_elapsed);
        var scene = _scenes[sceneIndex];
        var pageLength = scene.Length / scene.Pages.Length;
        var pageIndex = Math.Clamp((int) ((_elapsed - scene.Start) / pageLength), 0, scene.Pages.Length - 1);
        // Children of this control are rebuilt here, before the engine walks them for this frame.
        if (sceneIndex != _scene || pageIndex != _page)
        {
            _scene = sceneIndex;
            _page = pageIndex;
            BuildPage(scene.Pages[pageIndex]);
        }

        var start = scene.Start + pageLength * pageIndex;
        var end = Math.Min(start + pageLength, _total);
        _content.Modulate = Color.White.WithAlpha(FadeWindow(start, end, 0.5f, 0.4f));

        var status = $"{(_paused ? "Пауза · " : string.Empty)}Хроника созыва · {sceneIndex + 1} из {_scenes.Length}";
        if (_status.Text != status)
            _status.Text = status;
    }

    private void BuildPage(Page page)
    {
        _title.Text = page.Title;
        _note.Text = page.Note;
        _pageMore = page.More;
        _more.Text = page.More ?? string.Empty;
        _rows.RemoveAllChildren();
        foreach (var row in page.Rows)
            _rows.AddChild(MakeRow(row));
        _fitDelay = 2;
    }

    private static Control MakeRow(Row row)
    {
        var label = new SWConsoleStyle.WrappingRichTextLabel(PanelWidth - 32)
        {
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        label.Stylesheet = new Stylesheet(new StyleRule[]
        {
            StylesheetHelpers.Element<RichTextLabel>().Prop("font", row.Large ? _largeFont! : _textFont!),
        });
        // Literal text only: server strings are never parsed as markup.
        var message = new FormattedMessage();
        message.PushColor(row.Color);
        message.AddText(row.Text);
        message.Pop();
        if (row.Tail != null)
        {
            message.PushColor(SWConsoleStyle.TextDim);
            message.AddText(row.Tail);
            message.Pop();
        }

        label.SetMessage(message);
        if (row.Bar == null)
            return label;

        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2, MouseFilter = MouseFilterMode.Ignore };
        box.AddChild(label);
        box.AddChild(new ComparisonBar(row.Bar));
        return box;
    }

    /// <summary>
    /// Hides the first row that does not fit and every row after it, so a cramped panel shows a shorter selection
    /// instead of overflowing; the full list stays in the report.
    /// </summary>
    private void FitRows()
    {
        // Positions are from the previous layout pass; wait until the rebuilt or re-shown rows have been laid out.
        if (_fitDelay > 0)
        {
            _fitDelay--;
            return;
        }

        var limit = _clip.Height + 0.5f;
        var hiding = false;
        foreach (var child in _rows.Children)
        {
            if (!child.Visible)
            {
                hiding = true;
                continue;
            }

            if (!hiding && child.Height > 0f && child.Position.Y + child.Height > limit)
                hiding = true;
            if (hiding)
                child.Visible = false;
        }

        var more = hiding ? _pageMore ?? "Остальное — в полном отчёте" : _pageMore ?? string.Empty;
        if (_more.Text != more)
            _more.Text = more;
    }

    private void ShowAllRows()
    {
        _fitDelay = 2;
        foreach (var child in _rows.Children)
            child.Visible = true;
    }

    /// <summary>Measures its content without passing its height up, so the panel keeps its size and clips.</summary>
    private sealed class ClipArea : Control
    {
        public ClipArea()
        {
            RectClipContent = true;
            VerticalExpand = true;
            HorizontalExpand = true;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            foreach (var child in Children)
                child.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
            return Vector2.Zero;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            foreach (var child in Children)
                child.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(finalSize.X, MathF.Max(finalSize.Y, child.DesiredSize.Y))));
            return finalSize;
        }
    }

    /// <summary>Start and end of a global indicator against its own valid bounds.</summary>
    private sealed class ComparisonBar : Control
    {
        private readonly SWSenateHistoryStat _stat;

        public ComparisonBar(SWSenateHistoryStat stat)
        {
            _stat = stat;
            MinHeight = 12;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var width = PixelWidth;
            var range = _stat.Max - _stat.Min;
            var start = Math.Clamp((_stat.Start - _stat.Min) / range, 0f, 1f) * width;
            var end = Math.Clamp((_stat.End - _stat.Min) / range, 0f, 1f) * width;
            var ui = UIScale;
            handle.DrawRect(new UIBox2(0, 0, width, 3 * ui), SWConsoleStyle.Edge);
            handle.DrawRect(new UIBox2(0, 0, start, 3 * ui), SWConsoleStyle.TextDim);
            handle.DrawRect(new UIBox2(0, 6 * ui, width, 10 * ui), SWConsoleStyle.Edge);
            handle.DrawRect(new UIBox2(0, 6 * ui, end, 10 * ui), SWConsoleStyle.Brass);
        }
    }
}
