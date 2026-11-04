using System;
using System.Collections.Generic;
using System.Numerics;
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

namespace Content.Client._SW._Senate.Intro;

/// <summary>Personal closing card of the intro: the role the player is most likely to receive.</summary>
public sealed record SWSenateIntroRole(string Title, string FirstStep);

/// <summary>
/// Pre-round introduction to the Senate mode, staged as one camera walk: Coruscant, the Senate hall, the court and
/// the player's desk. Art is pixel art on an integer-scaled canvas 270 art pixels high; text is drawn at UI resolution.
/// Missing art files are replaced by labelled development placeholders, never presented as final art.
/// </summary>
public sealed partial class SWSenateIntro : LayoutContainer
{
    public const float Duration = 62f;

    private static readonly Color Backdrop = Color.FromHex("#0A0F16");
    private static readonly Color Ink = SWConsoleStyle.Brass;
    private static readonly Color Bright = SWConsoleStyle.TextBright;
    private static readonly Color Dim = SWConsoleStyle.TextDim;
    private static readonly Color Accent = SWConsoleStyle.Accent;

    private static Font? _labelFont;
    private static Font? _textFont;
    private static Font? _smallFont;
    private static Font? _roleFont;

    private readonly SWSenateIntroRole? _role;
    private readonly Label _status;
    private readonly Button _ready;
    private readonly CheckBox _autoplay;
    private readonly bool _reducedMotion;
    private float _elapsed;
    private bool _finished;
    private bool _updatingReady;

    public event Action? Finished;
    public event Action? Replayed;
    public event Action<bool>? ReadyToggled;
    public event Action<bool>? AutoplayToggled;

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>($"/Fonts/NotoSans/NotoSans-{variant}.ttf"), size);

    public SWSenateIntro(SWSenateIntroRole? role, bool autoplay, bool reducedMotion)
    {
        _role = role;
        _reducedMotion = reducedMotion;
        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        _labelFont ??= LoadFont("Bold", 12);
        _textFont ??= LoadFont("Bold", 16);
        _smallFont ??= LoadFont("Regular", 13);
        _roleFont ??= LoadFont("Bold", 24);
        LoadArt();

        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10 };
        AddChild(bar);
        SetAnchorPreset(bar, LayoutPreset.BottomWide);
        SetMarginLeft(bar, 24);
        SetMarginRight(bar, -24);
        SetMarginTop(bar, -56);
        SetMarginBottom(bar, -16);

        // The status label takes the slack and clips, so the buttons never fall off a narrow window.
        _status = new Label { VAlign = Label.VAlignMode.Center, FontColorOverride = Dim, HorizontalExpand = true, ClipText = true };
        bar.AddChild(_status);
        _ready = new Button { ToggleMode = true, Text = "Не готов" };
        _ready.OnToggled += args =>
        {
            if (!_updatingReady)
                ReadyToggled?.Invoke(args.Pressed);
        };
        bar.AddChild(_ready);
        _autoplay = new CheckBox { Text = "Показывать перед раундом", Pressed = autoplay };
        _autoplay.OnToggled += args => AutoplayToggled?.Invoke(args.Pressed);
        bar.AddChild(_autoplay);
        var replay = new Button { Text = "С начала" };
        replay.OnPressed += _ => Restart();
        bar.AddChild(replay);
        var skip = new Button { Text = "Пропустить" };
        skip.OnPressed += _ => Complete();
        bar.AddChild(skip);
    }

    /// <summary>Mirrors the lobby countdown and ready state, so the intro never hides them.</summary>
    public void UpdateLobbyStatus(string countdown, bool ready, bool canReady)
    {
        if (_status.Text != countdown)
            _status.Text = countdown;
        _ready.Visible = canReady;
        if (_ready.Pressed != ready)
        {
            _updatingReady = true;
            _ready.Pressed = ready;
            _updatingReady = false;
        }

        var readyText = ready ? "Готов" : "Не готов";
        if (_ready.Text != readyText)
            _ready.Text = readyText;
        FitBar();
    }

    /// <summary>Shortens and then hides the autoplay checkbox, so the buttons stay on narrow windows.</summary>
    private void FitBar()
    {
        var label = Width < 720f ? "Автопоказ" : "Показывать перед раундом";
        if (_autoplay.Text != label)
            _autoplay.Text = label;
        _autoplay.Visible = Width >= 480f;
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
        Complete();
    }

    /// <summary>Plays the intro again from the first scene.</summary>
    public void Restart()
    {
        if (_finished)
            return;

        _elapsed = 0f;
        Replayed?.Invoke();
    }

    private void Complete()
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

        _elapsed += args.DeltaSeconds;
        if (_elapsed >= Duration)
            Complete();
    }

    // ---- timing helpers -------------------------------------------------

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static float EaseInOut(float value)
    {
        value = Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private float Phase(float start, float length) => length <= 0f ? (_elapsed >= start ? 1f : 0f) : Clamp01((_elapsed - start) / length);

    /// <summary>Visibility of a window with short fades on both ends.</summary>
    private float FadeWindow(float start, float end, float fadeIn = 0.6f, float fadeOut = 0.6f) =>
        MathF.Min(Phase(start, fadeIn), 1f - Phase(end - fadeOut, fadeOut));

    // ---- text -----------------------------------------------------------

    private const string Ellipsis = "…";

    private static float TextWidth(DrawingHandleScreen handle, Font font, float scale, string text) =>
        handle.GetDimensions(font, text, scale).X;

    /// <summary>Draws one left-aligned line, ellipsized to <paramref name="maxWidth"/>.</summary>
    private static void DrawLeft(DrawingHandleScreen handle, Font font, float scale, string text, float x, float y, float maxWidth, Color color)
    {
        if (text.Length == 0 || color.A <= 0f)
            return;

        handle.DrawString(font, new Vector2(x, y), Ellipsize(handle, font, scale, text, maxWidth), scale, color);
    }

    /// <summary>Draws one left-aligned line, first shrinking the font down to 70% and only then ellipsizing.</summary>
    private static void DrawLeftFitted(DrawingHandleScreen handle, Font font, float scale, string text, float x, float y, float maxWidth, Color color)
    {
        if (text.Length == 0 || color.A <= 0f)
            return;

        var width = TextWidth(handle, font, scale, text);
        var fitted = width > maxWidth && width > 0f ? MathF.Max(scale * 0.7f, scale * maxWidth / width) : scale;
        // Keep the shrunken line vertically centred in the slot of the full-size line.
        var offset = (font.GetLineHeight(scale) - font.GetLineHeight(fitted)) / 2f;
        DrawLeft(handle, font, fitted, text, x, y + offset, maxWidth, color);
    }

    /// <summary>Shortens a single line with an ellipsis so that it fits <paramref name="maxWidth"/>.</summary>
    private static string Ellipsize(DrawingHandleScreen handle, Font font, float scale, string text, float maxWidth)
    {
        if (TextWidth(handle, font, scale, text) <= maxWidth)
            return text;

        var length = text.Length;
        while (length > 0 && TextWidth(handle, font, scale, text[..length].TrimEnd() + Ellipsis) > maxWidth)
            length--;
        return length == 0 ? string.Empty : text[..length].TrimEnd() + Ellipsis;
    }

    /// <summary>Wraps text to <paramref name="maxWidth"/> and limits it to <paramref name="maxLines"/>, ellipsizing the last line.</summary>
    private static readonly Dictionary<(string Text, Font Font, float Scale, int Width, int Lines), List<string>> FitCache = new();

    private static List<string> Fit(DrawingHandleScreen handle, Font font, float scale, string text, float maxWidth, int maxLines)
    {
        var key = (text, font, scale, (int) maxWidth, maxLines);
        if (FitCache.TryGetValue(key, out var cached))
            return cached;

        if (FitCache.Count > 256)
            FitCache.Clear();
        var result = FitUncached(handle, font, scale, text, maxWidth, maxLines);
        FitCache[key] = result;
        return result;
    }

    private static List<string> FitUncached(DrawingHandleScreen handle, Font font, float scale, string text, float maxWidth, int maxLines)
    {
        var lines = Wrap(handle, font, scale, text, maxWidth);
        if (lines.Count <= maxLines)
            return lines;

        var kept = lines.GetRange(0, Math.Max(1, maxLines));
        kept[kept.Count - 1] = Ellipsize(handle, font, scale, kept[kept.Count - 1] + Ellipsis, maxWidth);
        return kept;
    }

    /// <summary>Word wrap that also splits a single word longer than the line.</summary>
    private static List<string> Wrap(DrawingHandleScreen handle, Font font, float scale, string text, float maxWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw;
            while (TextWidth(handle, font, scale, word) > maxWidth && word.Length > 1)
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                var take = word.Length - 1;
                while (take > 1 && TextWidth(handle, font, scale, word[..take] + "-") > maxWidth)
                    take--;
                lines.Add(word[..take] + "-");
                word = word[take..];
            }

            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && TextWidth(handle, font, scale, candidate) > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }
}
