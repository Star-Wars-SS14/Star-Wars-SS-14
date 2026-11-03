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
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._SW._Senate.Intro;

/// <summary>Personal closing card of the intro: the role the player is most likely to receive.</summary>
public sealed record SWSenateIntroRole(string Title, string FirstStep);

/// <summary>
/// Animated pre-round introduction to the Senate mode: the structure of power, the roles and the rhythm of a round.
/// Everything is drawn procedurally, so no textures are required.
/// </summary>
public sealed class SWSenateIntro : LayoutContainer
{
    public const float Duration = 56f;

    private const float SceneTitle = 0f;
    private const float SceneStructure = 6f;
    private const float SceneSenate = 14f;
    private const float SceneExecutive = 23f;
    private const float SceneCourt = 31f;
    private const float SceneAround = 38f;
    private const float SceneRhythm = 44f;
    private const float SceneRole = 50f;

    private static readonly Color Backdrop = Color.FromHex("#0A0F16");
    private static readonly Color Ink = SWConsoleStyle.Brass;
    private static readonly Color Bright = SWConsoleStyle.TextBright;
    private static readonly Color Dim = SWConsoleStyle.TextDim;
    private static readonly Color Accent = SWConsoleStyle.Accent;
    private static readonly Color Panel = SWConsoleStyle.BgPanel;
    private static readonly Color Edge = SWConsoleStyle.Edge;

    private sealed record Card(string Title, string Text);

    private sealed record Scene(float Start, float End, int Focus, string Caption, Card[] Cards);

    private static readonly Scene[] Scenes =
    {
        new(SceneStructure, SceneSenate, 0,
            "Республикой управляют три ветви власти. Над ними стоит Верховный канцлер, а держит их в равновесии закон.",
            Array.Empty<Card>()),
        new(SceneSenate, SceneExecutive, 1,
            "Законодательная власть. Двадцать секторов, двадцать голосов. Закон начинается с проекта сенатора.",
            new[]
            {
                new Card("Сенатор", "Представляет сектор и голосует от его имени. Вносит законопроекты, работает в комитете, решает дилеммы своих миров."),
                new Card("Спикер Сената", "Ведёт парламентский календарь: назначает заседания и доводит голосования до конца."),
            }),
        new(SceneExecutive, SceneCourt, 2,
            "Исполнительная власть. Закон меняет правила, но грузы, деньги и люди сами не придут.",
            new[]
            {
                new Card("Верховный канцлер", "Подписывает законы или накладывает вето, утверждает бюджет, может получить чрезвычайные полномочия, но рискует легитимностью."),
                new Card("Министр", "Возглавляет ведомство: финансы, оборону, экономику, здравоохранение, безопасность или юстицию. Ведёт программы и операции."),
            }),
        new(SceneCourt, SceneAround, 3,
            "Судебная власть. Суд рассматривает дела, проверяет законы на конституционность и преследует коррупцию.",
            new[]
            {
                new Card("Верховный судья", "Руководит судом, решает при равенстве голосов, начинает конституционную проверку."),
                new Card("Судья Республики", "Рассматривает дела и голосует по вердиктам. Назначается с одобрения Сената."),
                new Card("Обвинитель", "Ведёт линию обвинения в судебных делах."),
                new Card("Защитник", "Ведёт линию защиты в судебных делах."),
            }),
        new(SceneAround, SceneRhythm, 4,
            "Вокруг власти. Без них Сенат не услышат, не защитят и не вылечат.",
            new[]
            {
                new Card("Корреспондент Голонета", "Освещает заседания и скандалы. То, что попало в эфир, слышит вся Республика."),
                new Card("Пристав Сената", "Сопровождает заседания и поддерживает порядок в зале."),
                new Card("Сенатская Гвардия", "Охраняет помещения и должностных лиц, исполняет ордера."),
                new Card("Сенатский врач", "Медицинское сопровождение созыва."),
            }),
    };

    private static readonly string[] BranchTitles = { "СЕНАТ", "КАБИНЕТ", "СУД" };
    private static readonly string[] BranchSubtitles = { "законодательная власть", "исполнительная власть", "судебная власть" };
    private static readonly string[] RhythmWords = { "Законы", "Бюджет к 09:00", "Дилеммы", "Кризисы", "Суд", "Тайные амбиции" };

    private static Font? _titleFont;
    private static Font? _boldFont;
    private static Font? _regularFont;

    private readonly SWSenateIntroRole? _role;
    private readonly Vector3[] _stars;
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
        _titleFont ??= LoadFont("Bold", 34);
        _boldFont ??= LoadFont("Bold", 15);
        _regularFont ??= LoadFont("Regular", 13);

        var random = new System.Random(1977);
        _stars = new Vector3[170];
        for (var i = 0; i < _stars.Length; i++)
            _stars[i] = new Vector3((float) random.NextDouble(), (float) random.NextDouble(), (float) random.NextDouble());

        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 12 };
        AddChild(bar);
        SetAnchorPreset(bar, LayoutPreset.BottomWide);
        SetMarginLeft(bar, 24);
        SetMarginRight(bar, -24);
        SetMarginTop(bar, -56);
        SetMarginBottom(bar, -16);

        _status = new Label { VAlign = Label.VAlignMode.Center, FontColorOverride = Dim, MinWidth = 220 };
        bar.AddChild(_status);
        _ready = new Button { ToggleMode = true, Text = "Не готов", MinWidth = 120 };
        _ready.OnToggled += args =>
        {
            if (!_updatingReady)
                ReadyToggled?.Invoke(args.Pressed);
        };
        bar.AddChild(_ready);
        bar.AddChild(new Control { HorizontalExpand = true });
        _autoplay = new CheckBox { Text = "Показывать перед раундом", Pressed = autoplay };
        _autoplay.OnToggled += args => AutoplayToggled?.Invoke(args.Pressed);
        bar.AddChild(_autoplay);
        var replay = new Button { Text = "С начала", MinWidth = 110 };
        replay.OnPressed += _ => Restart();
        bar.AddChild(replay);
        var skip = new Button { Text = "Пропустить", MinWidth = 120 };
        skip.OnPressed += _ => Complete();
        bar.AddChild(skip);
    }

    /// <summary>Mirrors the lobby countdown and ready state, so the intro never hides them.</summary>
    public void UpdateLobbyStatus(string countdown, bool ready, bool canReady)
    {
        _status.Text = countdown;
        _ready.Visible = canReady;
        _updatingReady = true;
        _ready.Pressed = ready;
        _updatingReady = false;
        _ready.Text = ready ? "Готов" : "Не готов";
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

    private static float Ease(float value)
    {
        value = Clamp01(value);
        return 1f - MathF.Pow(1f - value, 3f);
    }

    /// <summary>Progress of a segment from <paramref name="start"/> lasting <paramref name="length"/> seconds.</summary>
    private float Phase(float start, float length) => Clamp01((_elapsed - start) / length);

    /// <summary>Visibility of a window with short fades on both ends.</summary>
    private float Window(float start, float end, float fadeIn = 0.6f, float fadeOut = 0.5f) =>
        MathF.Min(Phase(start, fadeIn), 1f - Phase(end - fadeOut, fadeOut));

    private static string Typed(string text, float progress)
    {
        var count = (int) MathF.Round(text.Length * Clamp01(progress));
        return count >= text.Length ? text : text[..count];
    }

    // ---- layout ---------------------------------------------------------

    /// <summary>Vertical space kept free for the control bar at the bottom of the screen.</summary>
    private const float BarReserve = 72f;
    private const float SideMargin = 32f;
    private const int CaptionMaxLines = 2;

    private float SafeBottom(Vector2 size, float scale) => size.Y - BarReserve * scale;

    private float CaptionTop(Vector2 size, float scale) =>
        SafeBottom(size, scale) - 12f * scale - CaptionMaxLines * _boldFont!.GetLineHeight(scale);

    private float ContentWidth(Vector2 size, float scale) => MathF.Max(1f, size.X - SideMargin * 2 * scale);

    /// <summary>Vertical slide used for entrances; disabled under reduced motion.</summary>
    private float Slide(float appear, float distance, float scale) =>
        _reducedMotion ? 0f : (1f - appear) * distance * scale;

    // ---- drawing --------------------------------------------------------

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = PixelSize;
        var scale = UIScale;
        var backdrop = Math.Clamp(_elapsed / 0.8f, 0f, 1f) * 0.97f;
        handle.DrawRect(new UIBox2(Vector2.Zero, size), Backdrop.WithAlpha(backdrop));

        DrawStars(handle, size, scale);
        DrawTitle(handle, size, scale);
        DrawStructure(handle, size, scale);
        foreach (var scene in Scenes)
        {
            var alpha = Window(scene.Start, scene.End);
            if (alpha <= 0f)
                continue;

            DrawCards(handle, size, scale, scene, alpha);
            DrawCaption(handle, size, scale, scene.Caption, scene.Start, alpha);
        }

        DrawRhythm(handle, size, scale);
        DrawRole(handle, size, scale);

        var outro = Phase(Duration - 1.5f, 1.5f);
        if (outro > 0f)
            handle.DrawRect(new UIBox2(Vector2.Zero, size), Backdrop.WithAlpha(outro));
    }

    private void DrawStars(DrawingHandleScreen handle, Vector2 size, float scale)
    {
        var alpha = Clamp01(_elapsed / 1.5f);
        foreach (var star in _stars)
        {
            var drift = _reducedMotion ? 0f : _elapsed * (4f + star.Z * 10f) * scale;
            var x = (star.X * size.X + drift) % size.X;
            var y = star.Y * SafeBottom(size, scale);
            var twinkle = _reducedMotion ? 0.8f : 0.45f + 0.55f * MathF.Abs(MathF.Sin(_elapsed * (0.6f + star.Z * 1.6f) + star.X * 40f));
            var radius = (0.6f + star.Z * 1.2f) * scale;
            handle.DrawCircle(new Vector2(x, y), radius, Bright.WithAlpha(alpha * twinkle * (0.25f + star.Z * 0.45f)));
        }
    }

    private void DrawTitle(DrawingHandleScreen handle, Vector2 size, float scale)
    {
        var alpha = Window(SceneTitle, SceneStructure, 0.8f, 0.8f);
        if (alpha <= 0f)
            return;

        var width = ContentWidth(size, scale);
        var grow = _reducedMotion ? 1f : Ease(Phase(0.3f, 1.6f));
        var radius = MathF.Min(74f * scale, SafeBottom(size, scale) * 0.13f) * (0.6f + 0.4f * grow);
        var center = new Vector2(size.X / 2, SafeBottom(size, scale) * 0.36f);
        DrawSeal(handle, center, radius, alpha * Ease(Phase(0.3f, 1.2f)));

        var titleY = center.Y + radius + 36f * scale;
        var titleAlpha = alpha * Ease(Phase(1.6f, 1.2f));
        DrawFitted(handle, _titleFont!, scale, "ГАЛАКТИЧЕСКИЙ СЕНАТ", size.X / 2, titleY, width, Bright.WithAlpha(titleAlpha));
        var subtitle = Typed("Созыв начинается", Phase(2.6f, 1.4f));
        DrawCentered(handle, _boldFont!, scale, subtitle, size.X / 2, titleY + _titleFont!.GetLineHeight(scale) + 12f * scale, width, Ink.WithAlpha(alpha));
    }

    private void DrawStructure(DrawingHandleScreen handle, Vector2 size, float scale)
    {
        var alpha = Window(SceneStructure, SceneRhythm, 0.8f, 0.8f);
        if (alpha <= 0f)
            return;

        var focus = 0;
        foreach (var scene in Scenes)
        {
            if (_elapsed >= scene.Start && _elapsed < scene.End)
                focus = scene.Focus;
        }

        var nodeHeight = 52f * scale;
        var top = new Vector2(size.X / 2, MathF.Max(24f * scale + nodeHeight / 2, size.Y * 0.13f));
        var branchY = MathF.Max(top.Y + nodeHeight * 2f, size.Y * 0.32f);
        var junction = (top.Y + nodeHeight / 2 + branchY - nodeHeight / 2) / 2;
        var spread = MathF.Min(ContentWidth(size, scale) / 3, 400f * scale);
        var branchX = new[] { size.X / 2 - spread, size.X / 2, size.X / 2 + spread };
        var nodeSize = new Vector2(MathF.Min(240f * scale, spread - 16f * scale), nodeHeight);

        // Connectors grow from the Chancellor down to every branch.
        var line = _reducedMotion ? Phase(SceneStructure + 0.8f, 0.6f) : Ease(Phase(SceneStructure + 0.8f, 2.4f));
        var lineColor = Ink.WithAlpha(alpha * 0.85f * (_reducedMotion ? line : 1f));
        var lineGrow = _reducedMotion ? 1f : line;
        var stem = Math.Clamp(lineGrow * 3f, 0f, 1f);
        handle.DrawLine(top + new Vector2(0, nodeSize.Y / 2), new Vector2(top.X, top.Y + nodeSize.Y / 2 + (junction - top.Y - nodeSize.Y / 2) * stem), lineColor);
        var bar = Math.Clamp(lineGrow * 3f - 1f, 0f, 1f);
        if (bar > 0f)
        {
            handle.DrawLine(new Vector2(top.X, junction), new Vector2(top.X - spread * bar, junction), lineColor);
            handle.DrawLine(new Vector2(top.X, junction), new Vector2(top.X + spread * bar, junction), lineColor);
        }

        var drop = Math.Clamp(lineGrow * 3f - 2f, 0f, 1f);
        for (var i = 0; i < 3; i++)
        {
            if (drop > 0f)
                handle.DrawLine(new Vector2(branchX[i], junction), new Vector2(branchX[i], junction + (branchY - nodeSize.Y / 2 - junction) * drop), lineColor);
        }

        if (line >= 1f && !_reducedMotion)
        {
            var pulse = (_elapsed * 0.45f) % 1f;
            for (var i = 0; i < 3; i++)
            {
                var path = PulsePoint(top with { Y = top.Y + nodeSize.Y / 2 }, junction, branchX[i], branchY - nodeSize.Y / 2, pulse);
                handle.DrawCircle(path, 3f * scale, Accent.WithAlpha(alpha * 0.9f));
            }
        }

        var chancellorFocus = focus == 0 || focus == 2;
        DrawNode(handle, top, nodeSize, "ВЕРХОВНЫЙ КАНЦЛЕР", "глава исполнительной власти",
            alpha * Ease(Phase(SceneStructure, 0.8f)), chancellorFocus, focus == 2, scale);

        for (var i = 0; i < 3; i++)
        {
            var appear = Ease(Phase(SceneStructure + 2.4f + i * 0.35f, 0.7f));
            var active = focus == 0 || focus == i + 1;
            DrawNode(handle, new Vector2(branchX[i], branchY), nodeSize, BranchTitles[i], BranchSubtitles[i],
                alpha * appear, active, focus == i + 1, scale);
        }

        // Twenty sectors light up beneath the Senate while it is in focus.
        var sectors = Window(SceneSenate, SceneExecutive);
        if (sectors > 0f)
        {
            const int count = 20;
            var gap = MathF.Min(14f * scale, nodeSize.X / count);
            var startX = branchX[0] - gap * (count - 1) / 2;
            var y = branchY + nodeSize.Y / 2 + 16f * scale;
            for (var i = 0; i < count; i++)
            {
                var lit = _reducedMotion ? 1f : Ease(Phase(SceneSenate + 0.8f + i * 0.12f, 0.4f));
                var color = Color.InterpolateBetween(Edge, Accent, lit).WithAlpha(sectors * alpha);
                handle.DrawCircle(new Vector2(startX + gap * i, y), 3.2f * scale, color);
            }

            DrawCentered(handle, _regularFont!, scale, "20 секторов, 20 голосов", branchX[0], y + 10f * scale, nodeSize.X,
                Dim.WithAlpha(sectors * alpha * Ease(Phase(SceneSenate + 3.2f, 0.6f))));
        }
    }

    private static Vector2 PulsePoint(Vector2 start, float junction, float targetX, float targetY, float t)
    {
        var first = junction - start.Y;
        var second = MathF.Abs(targetX - start.X);
        var third = targetY - junction;
        var total = first + second + third;
        var distance = t * total;
        if (distance <= first)
            return new Vector2(start.X, start.Y + distance);
        distance -= first;
        if (distance <= second)
            return new Vector2(start.X + MathF.Sign(targetX - start.X) * distance, junction);
        distance -= second;
        return new Vector2(targetX, junction + distance);
    }

    private void DrawNode(DrawingHandleScreen handle, Vector2 center, Vector2 nodeSize, string title, string subtitle,
        float alpha, bool active, bool focused, float scale)
    {
        if (alpha <= 0f)
            return;

        var shade = active ? 1f : 0.35f;
        var box = UIBox2.FromDimensions(center - nodeSize / 2, nodeSize);
        if (focused)
        {
            var glow = _reducedMotion ? 0.3f : 0.25f + 0.15f * MathF.Sin(_elapsed * 3f);
            var pad = 6f * scale;
            handle.DrawRect(new UIBox2(box.TopLeft - new Vector2(pad), box.BottomRight + new Vector2(pad)), Accent.WithAlpha(alpha * glow));
        }

        var inner = nodeSize.X - 16f * scale;
        handle.DrawRect(box, Panel.WithAlpha(alpha * 0.96f));
        handle.DrawRect(box, (focused ? Accent : Edge).WithAlpha(alpha * shade), false);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + 3f * scale), Ink.WithAlpha(alpha * shade));
        DrawFitted(handle, _boldFont!, scale, title, center.X, box.Top + 9f * scale, inner, Bright.WithAlpha(alpha * shade));
        DrawCentered(handle, _regularFont!, scale, subtitle, center.X, box.Top + 29f * scale, inner, Dim.WithAlpha(alpha * shade));
    }

    private void DrawCards(DrawingHandleScreen handle, Vector2 size, float scale, Scene scene, float alpha)
    {
        if (scene.Cards.Length == 0)
            return;

        var available = ContentWidth(size, scale);
        var gap = 16f * scale;
        var padding = 14f * scale;
        var minWidth = 200f * scale;
        var count = scene.Cards.Length;
        // Use as many columns as fit at a readable width; the rest wrap to a second row.
        var columns = Math.Clamp((int) ((available + gap) / (minWidth + gap)), 1, count);
        var rows = (count + columns - 1) / columns;
        var width = MathF.Min(300f * scale, (available - gap * (columns - 1)) / columns);
        var regularLine = _regularFont!.GetLineHeight(scale);
        var boldLine = _boldFont!.GetLineHeight(scale);

        var regionTop = size.Y * 0.5f;
        var regionBottom = CaptionTop(size, scale) - 12f * scale;
        var rowHeight = MathF.Max(padding * 2 + boldLine + regularLine, (regionBottom - regionTop - gap * (rows - 1)) / rows);

        for (var i = 0; i < count; i++)
        {
            var card = scene.Cards[i];
            var appear = Ease(Phase(scene.Start + 0.5f + i * 0.35f, 0.7f));
            if (appear <= 0f)
                continue;

            var row = i / columns;
            var column = i % columns;
            var inRow = Math.Min(columns, count - row * columns);
            var rowLeft = (size.X - (width * inRow + gap * (inRow - 1))) / 2;
            var a = alpha * appear;
            var textWidth = width - padding * 2;
            var titleLines = Fit(handle, _boldFont!, scale, card.Title.ToUpperInvariant(), textWidth, 2);
            var bodyMax = Math.Max(1, (int) ((rowHeight - padding * 2 - titleLines.Count * boldLine - 6f * scale) / regularLine));
            var lines = Fit(handle, _regularFont!, scale, card.Text, textWidth, bodyMax);
            var height = MathF.Min(rowHeight, padding * 2 + titleLines.Count * boldLine + 6f * scale + lines.Count * regularLine);
            var origin = new Vector2(rowLeft + column * (width + gap), regionTop + row * (rowHeight + gap) + Slide(appear, 28f, scale));
            var box = UIBox2.FromDimensions(origin, new Vector2(width, height));
            handle.DrawRect(box, Panel.WithAlpha(a * 0.95f));
            handle.DrawRect(box, Edge.WithAlpha(a), false);
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + 3f * scale, box.Bottom), Ink.WithAlpha(a));
            var y = origin.Y + padding;
            foreach (var text in titleLines)
            {
                handle.DrawString(_boldFont!, new Vector2(origin.X + padding, y), text, scale, Ink.WithAlpha(a));
                y += boldLine;
            }

            y += 6f * scale;
            foreach (var text in lines)
            {
                handle.DrawString(_regularFont!, new Vector2(origin.X + padding, y), text, scale, Bright.WithAlpha(a));
                y += regularLine;
            }
        }
    }

    private void DrawCaption(DrawingHandleScreen handle, Vector2 size, float scale, string caption, float start, float alpha)
    {
        var maxWidth = MathF.Min(ContentWidth(size, scale), 880f * scale);
        var progress = _reducedMotion ? 1f : Phase(start + 0.2f, caption.Length / 55f);
        // Wrap the full caption first, so lines do not jump while the text is being revealed.
        var lines = Fit(handle, _boldFont!, scale, caption, maxWidth, CaptionMaxLines);
        var visible = (int) MathF.Round(caption.Length * progress);
        var lineHeight = _boldFont!.GetLineHeight(scale);
        var y = CaptionTop(size, scale) + (CaptionMaxLines - lines.Count) * lineHeight;
        var shown = 0;
        foreach (var line in lines)
        {
            var take = Math.Clamp(visible - shown, 0, line.Length);
            shown += line.Length + 1;
            var left = (size.X - handle.GetDimensions(_boldFont!, line, scale).X) / 2;
            if (take > 0)
                handle.DrawString(_boldFont!, new Vector2(left, y), line[..take], scale, Bright.WithAlpha(alpha));
            y += lineHeight;
        }
    }

    private void DrawRhythm(DrawingHandleScreen handle, Vector2 size, float scale)
    {
        var alpha = Window(SceneRhythm, SceneRole);
        if (alpha <= 0f)
            return;

        var available = ContentWidth(size, scale);
        DrawCentered(handle, _boldFont!, scale, "ТРИ ДНЯ СОЗЫВА", size.X / 2, size.Y * 0.18f, available, Ink.WithAlpha(alpha));
        var gap = 18f * scale;
        var width = MathF.Min(240f * scale, (available - gap * 2) / 3);
        var left = (size.X - width * 3 - gap * 2) / 2;
        var top = size.Y * 0.26f;
        var height = 46f * scale;
        string[] days = { "ДЕНЬ I", "ДЕНЬ II", "ДЕНЬ III" };
        for (var i = 0; i < 3; i++)
        {
            var origin = new Vector2(left + i * (width + gap), top);
            var box = UIBox2.FromDimensions(origin, new Vector2(width, height));
            var fill = _reducedMotion ? 1f : Ease(Phase(SceneRhythm + 0.5f + i * 0.9f, 1.1f));
            handle.DrawRect(box, Panel.WithAlpha(alpha));
            handle.DrawRect(new UIBox2(box.Left, box.Bottom - 4f * scale, box.Left + width * fill, box.Bottom), Accent.WithAlpha(alpha));
            handle.DrawRect(box, Edge.WithAlpha(alpha), false);
            DrawCentered(handle, _boldFont!, scale, days[i], origin.X + width / 2, origin.Y + 6f * scale, width - 12f * scale, Bright.WithAlpha(alpha));
            DrawCentered(handle, _regularFont!, scale, "07:00 — 21:00", origin.X + width / 2, origin.Y + 25f * scale, width - 12f * scale, Dim.WithAlpha(alpha));
        }

        // Keyword chips pop in one by one and wrap onto further rows on narrow screens.
        var chipGap = 10f * scale;
        var chipPadding = 12f * scale;
        var chipHeight = 30f * scale;
        var widths = new float[RhythmWords.Length];
        for (var i = 0; i < RhythmWords.Length; i++)
            widths[i] = MathF.Min(available, handle.GetDimensions(_boldFont!, RhythmWords[i], scale).X + chipPadding * 2);

        var rowStarts = new List<int> { 0 };
        var rowWidths = new List<float> { 0f };
        for (var i = 0; i < RhythmWords.Length; i++)
        {
            var current = rowWidths[rowWidths.Count - 1];
            var next = current + (current > 0f ? chipGap : 0f) + widths[i];
            if (current > 0f && next > available)
            {
                rowStarts.Add(i);
                rowWidths.Add(widths[i]);
            }
            else
            {
                rowWidths[rowWidths.Count - 1] = next;
            }
        }

        var chipTop = top + height + 36f * scale;
        for (var row = 0; row < rowStarts.Count; row++)
        {
            var end = row + 1 < rowStarts.Count ? rowStarts[row + 1] : RhythmWords.Length;
            var x = (size.X - rowWidths[row]) / 2;
            var y = chipTop + row * (chipHeight + chipGap);
            for (var i = rowStarts[row]; i < end; i++)
            {
                var pop = Ease(Phase(SceneRhythm + 2.4f + i * 0.3f, 0.45f));
                if (pop > 0f)
                {
                    var box = UIBox2.FromDimensions(new Vector2(x, y + Slide(pop, 12f, scale)), new Vector2(widths[i], chipHeight));
                    handle.DrawRect(box, SWConsoleStyle.PillDim.WithAlpha(alpha * pop));
                    handle.DrawRect(box, Ink.WithAlpha(alpha * pop * 0.7f), false);
                    var label = Ellipsize(handle, _boldFont!, scale, RhythmWords[i], widths[i] - chipPadding * 2);
                    handle.DrawString(_boldFont!, box.TopLeft + new Vector2(chipPadding, 6f * scale), label, scale, Bright.WithAlpha(alpha * pop));
                }

                x += widths[i] + chipGap;
            }
        }

        DrawCaption(handle, size, scale,
            "Каждое решение стоит денег, голосов или репутации. И у каждого участника есть собственные амбиции.",
            SceneRhythm, alpha);
    }

    private void DrawRole(DrawingHandleScreen handle, Vector2 size, float scale)
    {
        var alpha = MathF.Min(Phase(SceneRole, 0.8f), 1f - Phase(Duration - 1.5f, 1.5f));
        if (alpha <= 0f)
            return;

        var title = _role?.Title ?? "Должность не выбрана";
        var step = _role?.FirstStep ?? "Выберите должности в настройках персонажа, и Республика найдёт вам дело.";
        var width = MathF.Min(560f * scale, ContentWidth(size, scale));
        var padding = 24f * scale;
        var inner = width - padding * 2;
        var lineHeight = _boldFont!.GetLineHeight(scale);
        var titleHeight = _titleFont!.GetLineHeight(scale);
        var lines = Fit(handle, _boldFont!, scale, step, inner, 4);
        var height = padding * 2 + lineHeight + 8f * scale + titleHeight + 12f * scale + lines.Count * lineHeight;
        var appear = Ease(Phase(SceneRole, 0.9f));
        var mottoSpace = 40f * scale + lineHeight;
        var originY = MathF.Min(size.Y * 0.28f, SafeBottom(size, scale) - height - mottoSpace);
        var origin = new Vector2((size.X - width) / 2, MathF.Max(16f * scale, originY) + Slide(appear, 24f, scale));
        var box = UIBox2.FromDimensions(origin, new Vector2(width, height));

        var glow = _reducedMotion ? 0.22f : 0.18f + 0.1f * MathF.Sin(_elapsed * 2.4f);
        var pad = 8f * scale;
        handle.DrawRect(new UIBox2(box.TopLeft - new Vector2(pad), box.BottomRight + new Vector2(pad)), Ink.WithAlpha(alpha * glow));
        handle.DrawRect(box, Panel.WithAlpha(alpha));
        handle.DrawRect(box, Ink.WithAlpha(alpha), false);
        var y = origin.Y + padding;
        DrawCentered(handle, _boldFont!, scale, "ВАША РОЛЬ", size.X / 2, y, inner, Ink.WithAlpha(alpha));
        y += lineHeight + 8f * scale;
        DrawFitted(handle, _titleFont!, scale, title, size.X / 2, y, inner, Bright.WithAlpha(alpha));
        y += titleHeight + 12f * scale;
        foreach (var line in lines)
        {
            DrawCentered(handle, _boldFont!, scale, line, size.X / 2, y, inner, Dim.WithAlpha(alpha));
            y += lineHeight;
        }

        var motto = Typed("Да здравствует Республика.", _reducedMotion ? 1f : Phase(SceneRole + 1.6f, 1.4f));
        DrawCentered(handle, _boldFont!, scale, motto, size.X / 2, box.Bottom + 28f * scale, ContentWidth(size, scale), Ink.WithAlpha(alpha));
    }

    // ---- primitives -----------------------------------------------------

    private static readonly Vector2[] SealSpoke = new Vector2[4];

    private static void DrawSeal(DrawingHandleScreen handle, Vector2 center, float radius, float alpha)
    {
        if (alpha <= 0f)
            return;

        var ink = Ink.WithAlpha(alpha);
        handle.DrawCircle(center, radius, ink);
        handle.DrawCircle(center, radius * 0.83f, SWConsoleStyle.Navigation.WithAlpha(alpha));
        handle.DrawCircle(center, radius * 0.34f, ink);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI / 4;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var side = new Vector2(-direction.Y, direction.X) * radius * 0.095f;
            SealSpoke[0] = center + direction * radius * 0.29f - side;
            SealSpoke[1] = center + direction * radius * 0.88f - side;
            SealSpoke[2] = center + direction * radius * 0.88f + side;
            SealSpoke[3] = center + direction * radius * 0.29f + side;
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, SealSpoke, ink);
        }

        handle.DrawCircle(center, radius * 0.2f, SWConsoleStyle.Navigation.WithAlpha(alpha));
    }

    // ---- text -----------------------------------------------------------

    private const string Ellipsis = "…";

    private static float Width(DrawingHandleScreen handle, Font font, float scale, string text) =>
        handle.GetDimensions(font, text, scale).X;

    /// <summary>Draws one centred line, ellipsized to <paramref name="maxWidth"/>.</summary>
    private static void DrawCentered(DrawingHandleScreen handle, Font font, float scale, string text, float centerX, float y, float maxWidth, Color color)
    {
        if (text.Length == 0 || color.A <= 0f)
            return;

        text = Ellipsize(handle, font, scale, text, maxWidth);
        var width = Width(handle, font, scale, text);
        handle.DrawString(font, new Vector2(centerX - width / 2, y), text, scale, color);
    }

    /// <summary>Draws one centred line, first shrinking the font down to 70% and only then ellipsizing.</summary>
    private static void DrawFitted(DrawingHandleScreen handle, Font font, float scale, string text, float centerX, float y, float maxWidth, Color color)
    {
        if (text.Length == 0 || color.A <= 0f)
            return;

        var width = Width(handle, font, scale, text);
        var fitted = width > maxWidth ? MathF.Max(scale * 0.7f, scale * maxWidth / width) : scale;
        // Keep the shrunken line vertically centred in the slot of the full-size line.
        var offset = (font.GetLineHeight(scale) - font.GetLineHeight(fitted)) / 2;
        DrawCentered(handle, font, fitted, text, centerX, y + offset, maxWidth, color);
    }

    /// <summary>Shortens a single line with an ellipsis so that it fits <paramref name="maxWidth"/>.</summary>
    private static string Ellipsize(DrawingHandleScreen handle, Font font, float scale, string text, float maxWidth)
    {
        if (Width(handle, font, scale, text) <= maxWidth)
            return text;

        var length = text.Length;
        while (length > 0 && Width(handle, font, scale, text[..length].TrimEnd() + Ellipsis) > maxWidth)
            length--;
        return length == 0 ? string.Empty : text[..length].TrimEnd() + Ellipsis;
    }

    /// <summary>Wraps text to <paramref name="maxWidth"/> and limits it to <paramref name="maxLines"/>, ellipsizing the last line.</summary>
    private static List<string> Fit(DrawingHandleScreen handle, Font font, float scale, string text, float maxWidth, int maxLines)
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
            while (Width(handle, font, scale, word) > maxWidth && word.Length > 1)
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                var take = word.Length - 1;
                while (take > 1 && Width(handle, font, scale, word[..take] + "-") > maxWidth)
                    take--;
                lines.Add(word[..take] + "-");
                word = word[take..];
            }

            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Width(handle, font, scale, candidate) > maxWidth)
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
