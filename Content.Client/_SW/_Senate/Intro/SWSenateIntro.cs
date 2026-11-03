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
    private float _elapsed;
    private bool _finished;
    private bool _updatingReady;

    public event Action? Finished;
    public event Action<bool>? ReadyToggled;
    public event Action<bool>? AutoplayToggled;

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>($"/Fonts/NotoSans/NotoSans-{variant}.ttf"), size);

    public SWSenateIntro(SWSenateIntroRole? role, bool autoplay)
    {
        _role = role;
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
            var x = (star.X * size.X + _elapsed * (4f + star.Z * 10f) * scale) % size.X;
            var y = star.Y * size.Y;
            var twinkle = 0.45f + 0.55f * MathF.Abs(MathF.Sin(_elapsed * (0.6f + star.Z * 1.6f) + star.X * 40f));
            var radius = (0.6f + star.Z * 1.2f) * scale;
            handle.DrawCircle(new Vector2(x, y), radius, Bright.WithAlpha(alpha * twinkle * (0.25f + star.Z * 0.45f)));
        }
    }

    private void DrawTitle(DrawingHandleScreen handle, Vector2 size, float scale)
    {
        var alpha = Window(SceneTitle, SceneStructure, 0.8f, 0.8f);
        if (alpha <= 0f)
            return;

        var center = new Vector2(size.X / 2, size.Y * 0.38f);
        var grow = Ease(Phase(0.3f, 1.6f));
        var radius = 74f * scale * (0.6f + 0.4f * grow);
        DrawSeal(handle, center, radius, alpha * grow);
        DrawArc(handle, center, radius * 1.32f, -MathF.PI / 2, MathF.Tau * Ease(Phase(0.6f, 2.2f)), Ink.WithAlpha(alpha * 0.8f), scale);
        DrawArc(handle, center, radius * 1.46f, MathF.PI / 2, MathF.Tau * Ease(Phase(1.0f, 2.4f)), Ink.WithAlpha(alpha * 0.35f), scale);

        var title = "ГАЛАКТИЧЕСКИЙ СЕНАТ";
        var titleAlpha = alpha * Ease(Phase(1.6f, 1.2f));
        DrawCentered(handle, _titleFont!, scale, title, size.X / 2, center.Y + radius * 1.7f, Bright.WithAlpha(titleAlpha));
        var subtitle = Typed("Созыв начинается", Phase(2.6f, 1.4f));
        DrawCentered(handle, _boldFont!, scale, subtitle, size.X / 2, center.Y + radius * 1.7f + 52f * scale, Ink.WithAlpha(alpha));
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

        var top = new Vector2(size.X / 2, size.Y * 0.15f);
        var junction = size.Y * 0.25f;
        var branchY = size.Y * 0.34f;
        var spread = MathF.Min(size.X * 0.29f, 400f * scale);
        var branchX = new[] { size.X / 2 - spread, size.X / 2, size.X / 2 + spread };
        var nodeSize = new Vector2(MathF.Min(230f * scale, spread * 0.9f), 52f * scale);

        // Connectors grow from the Chancellor down to every branch.
        var line = Ease(Phase(SceneStructure + 0.8f, 2.4f));
        var lineColor = Ink.WithAlpha(alpha * 0.85f);
        var stem = Math.Clamp(line * 3f, 0f, 1f);
        handle.DrawLine(top + new Vector2(0, nodeSize.Y / 2), new Vector2(top.X, top.Y + nodeSize.Y / 2 + (junction - top.Y - nodeSize.Y / 2) * stem), lineColor);
        var bar = Math.Clamp(line * 3f - 1f, 0f, 1f);
        if (bar > 0f)
        {
            handle.DrawLine(new Vector2(top.X, junction), new Vector2(top.X - spread * bar, junction), lineColor);
            handle.DrawLine(new Vector2(top.X, junction), new Vector2(top.X + spread * bar, junction), lineColor);
        }

        var drop = Math.Clamp(line * 3f - 2f, 0f, 1f);
        for (var i = 0; i < 3; i++)
        {
            if (drop > 0f)
                handle.DrawLine(new Vector2(branchX[i], junction), new Vector2(branchX[i], junction + (branchY - nodeSize.Y / 2 - junction) * drop), lineColor);
        }

        // Signal pulses travel along the connectors once the structure stands.
        if (line >= 1f)
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
            var count = 20;
            var gap = MathF.Min(14f * scale, (nodeSize.X + 60f * scale) / count);
            var startX = branchX[0] - gap * (count - 1) / 2;
            var y = branchY + nodeSize.Y / 2 + 18f * scale;
            for (var i = 0; i < count; i++)
            {
                var lit = Ease(Phase(SceneSenate + 0.8f + i * 0.12f, 0.4f));
                var color = Color.InterpolateBetween(Edge, Accent, lit).WithAlpha(sectors * alpha);
                handle.DrawCircle(new Vector2(startX + gap * i, y), 3.2f * scale, color);
            }

            DrawCentered(handle, _regularFont!, scale, "20 секторов · 20 голосов", branchX[0], y + 10f * scale, Dim.WithAlpha(sectors * alpha * Ease(Phase(SceneSenate + 3.2f, 0.6f))));
        }

        // An outer ring marks the people around power.
        var around = Window(SceneAround, SceneRhythm);
        if (around > 0f)
        {
            var center = new Vector2(size.X / 2, (top.Y + branchY) / 2);
            var radiusX = spread + nodeSize.X * 0.75f;
            var radiusY = (branchY - top.Y) * 1.05f;
            DrawEllipse(handle, center, radiusX, radiusY, Ease(Phase(SceneAround + 0.2f, 1.6f)), Accent.WithAlpha(around * alpha * 0.55f));
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
            var glow = 0.25f + 0.15f * MathF.Sin(_elapsed * 3f);
            var pad = 6f * scale;
            handle.DrawRect(new UIBox2(box.TopLeft - new Vector2(pad), box.BottomRight + new Vector2(pad)), Accent.WithAlpha(alpha * glow));
        }

        handle.DrawRect(box, Panel.WithAlpha(alpha * 0.96f));
        handle.DrawRect(box, (focused ? Accent : Edge).WithAlpha(alpha * shade), false);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + 3f * scale), Ink.WithAlpha(alpha * shade));
        DrawCentered(handle, _boldFont!, scale, title, center.X, box.Top + 9f * scale, Bright.WithAlpha(alpha * shade));
        DrawCentered(handle, _regularFont!, scale, subtitle, center.X, box.Top + 29f * scale, Dim.WithAlpha(alpha * shade));
    }

    private void DrawCards(DrawingHandleScreen handle, Vector2 size, float scale, Scene scene, float alpha)
    {
        if (scene.Cards.Length == 0)
            return;

        var margin = 32f * scale;
        var gap = 16f * scale;
        var count = scene.Cards.Length;
        var width = MathF.Min(300f * scale, (size.X - margin * 2 - gap * (count - 1)) / count);
        var total = width * count + gap * (count - 1);
        var left = (size.X - total) / 2;
        var top = size.Y * 0.52f;
        var padding = 14f * scale;
        for (var i = 0; i < count; i++)
        {
            var card = scene.Cards[i];
            var appear = Ease(Phase(scene.Start + 0.5f + i * 0.35f, 0.7f));
            if (appear <= 0f)
                continue;

            var a = alpha * appear;
            var lines = Wrap(handle, _regularFont!, scale, card.Text, width - padding * 2);
            var lineHeight = _regularFont!.GetLineHeight(scale);
            var height = padding * 2 + 26f * scale + lines.Count * lineHeight;
            var origin = new Vector2(left + i * (width + gap), top + (1f - appear) * 28f * scale);
            var box = UIBox2.FromDimensions(origin, new Vector2(width, height));
            handle.DrawRect(box, Panel.WithAlpha(a * 0.95f));
            handle.DrawRect(box, Edge.WithAlpha(a), false);
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + 3f * scale, box.Bottom), Ink.WithAlpha(a));
            handle.DrawString(_boldFont!, origin + new Vector2(padding, padding), card.Title.ToUpperInvariant(), scale, Ink.WithAlpha(a));
            var y = origin.Y + padding + 26f * scale;
            foreach (var text in lines)
            {
                handle.DrawString(_regularFont!, new Vector2(origin.X + padding, y), text, scale, Bright.WithAlpha(a));
                y += lineHeight;
            }
        }
    }

    private void DrawCaption(DrawingHandleScreen handle, Vector2 size, float scale, string caption, float start, float alpha)
    {
        var maxWidth = MathF.Min(size.X - 64f * scale, 880f * scale);
        var text = Typed(caption, Phase(start + 0.2f, caption.Length / 55f));
        var lines = Wrap(handle, _boldFont!, scale, text, maxWidth);
        var lineHeight = _boldFont!.GetLineHeight(scale);
        var y = size.Y - 84f * scale - lines.Count * lineHeight;
        foreach (var line in lines)
        {
            DrawCentered(handle, _boldFont!, scale, line, size.X / 2, y, Bright.WithAlpha(alpha));
            y += lineHeight;
        }
    }

    private void DrawRhythm(DrawingHandleScreen handle, Vector2 size, float scale)
    {
        var alpha = Window(SceneRhythm, SceneRole);
        if (alpha <= 0f)
            return;

        DrawCentered(handle, _boldFont!, scale, "ТРИ ДНЯ СОЗЫВА", size.X / 2, size.Y * 0.2f, Ink.WithAlpha(alpha));
        var width = MathF.Min(240f * scale, (size.X - 96f * scale) / 3);
        var gap = 18f * scale;
        var left = (size.X - width * 3 - gap * 2) / 2;
        var top = size.Y * 0.28f;
        var height = 46f * scale;
        string[] days = { "ДЕНЬ I", "ДЕНЬ II", "ДЕНЬ III" };
        for (var i = 0; i < 3; i++)
        {
            var origin = new Vector2(left + i * (width + gap), top);
            var box = UIBox2.FromDimensions(origin, new Vector2(width, height));
            var fill = Ease(Phase(SceneRhythm + 0.5f + i * 0.9f, 1.1f));
            handle.DrawRect(box, Panel.WithAlpha(alpha));
            handle.DrawRect(new UIBox2(box.Left, box.Bottom - 4f * scale, box.Left + width * fill, box.Bottom), Accent.WithAlpha(alpha));
            handle.DrawRect(box, Edge.WithAlpha(alpha), false);
            DrawCentered(handle, _boldFont!, scale, days[i], origin.X + width / 2, origin.Y + 6f * scale, Bright.WithAlpha(alpha));
            DrawCentered(handle, _regularFont!, scale, "07:00 — 21:00", origin.X + width / 2, origin.Y + 25f * scale, Dim.WithAlpha(alpha));
        }

        // Keyword chips pop in one by one.
        var chipTop = top + height + 40f * scale;
        var chipGap = 10f * scale;
        var chipPadding = 12f * scale;
        var widths = new float[RhythmWords.Length];
        var rowWidth = 0f;
        for (var i = 0; i < RhythmWords.Length; i++)
        {
            widths[i] = handle.GetDimensions(_boldFont!, RhythmWords[i], scale).X + chipPadding * 2;
            rowWidth += widths[i] + (i > 0 ? chipGap : 0f);
        }

        var x = (size.X - rowWidth) / 2;
        for (var i = 0; i < RhythmWords.Length; i++)
        {
            var pop = Ease(Phase(SceneRhythm + 2.4f + i * 0.3f, 0.45f));
            if (pop > 0f)
            {
                var box = UIBox2.FromDimensions(new Vector2(x, chipTop + (1f - pop) * 12f * scale), new Vector2(widths[i], 30f * scale));
                handle.DrawRect(box, SWConsoleStyle.PillDim.WithAlpha(alpha * pop));
                handle.DrawRect(box, Ink.WithAlpha(alpha * pop * 0.7f), false);
                handle.DrawString(_boldFont!, box.TopLeft + new Vector2(chipPadding, 6f * scale), RhythmWords[i], scale, Bright.WithAlpha(alpha * pop));
            }

            x += widths[i] + chipGap;
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
        var width = MathF.Min(560f * scale, size.X - 64f * scale);
        var padding = 24f * scale;
        var lines = Wrap(handle, _boldFont!, scale, step, width - padding * 2);
        var lineHeight = _boldFont!.GetLineHeight(scale);
        var height = padding * 2 + 86f * scale + lines.Count * lineHeight;
        var appear = Ease(Phase(SceneRole, 0.9f));
        var origin = new Vector2((size.X - width) / 2, size.Y * 0.3f + (1f - appear) * 24f * scale);
        var box = UIBox2.FromDimensions(origin, new Vector2(width, height));

        var glow = 0.18f + 0.1f * MathF.Sin(_elapsed * 2.4f);
        var pad = 8f * scale;
        handle.DrawRect(new UIBox2(box.TopLeft - new Vector2(pad), box.BottomRight + new Vector2(pad)), Ink.WithAlpha(alpha * glow));
        handle.DrawRect(box, Panel.WithAlpha(alpha));
        handle.DrawRect(box, Ink.WithAlpha(alpha), false);
        DrawCentered(handle, _boldFont!, scale, "ВАША РОЛЬ", size.X / 2, origin.Y + padding, Ink.WithAlpha(alpha));
        DrawCentered(handle, _titleFont!, scale, title, size.X / 2, origin.Y + padding + 24f * scale, Bright.WithAlpha(alpha));
        var y = origin.Y + padding + 86f * scale;
        foreach (var line in lines)
        {
            DrawCentered(handle, _boldFont!, scale, line, size.X / 2, y, Dim.WithAlpha(alpha));
            y += lineHeight;
        }

        var motto = Typed("Да здравствует Республика.", Phase(SceneRole + 1.6f, 1.4f));
        DrawCentered(handle, _boldFont!, scale, motto, size.X / 2, box.Bottom + 40f * scale, Ink.WithAlpha(alpha));
    }

    // ---- primitives -----------------------------------------------------

    private static void DrawSeal(DrawingHandleScreen handle, Vector2 center, float radius, float alpha)
    {
        if (alpha <= 0f)
            return;

        var ink = Ink.WithAlpha(alpha);
        handle.DrawCircle(center, radius, ink);
        handle.DrawCircle(center, radius * 0.83f, SWConsoleStyle.Navigation.WithAlpha(alpha));
        handle.DrawCircle(center, radius * 0.34f, ink);
        var spoke = new Vector2[4];
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI / 4;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var side = new Vector2(-direction.Y, direction.X) * radius * 0.095f;
            spoke[0] = center + direction * radius * 0.29f - side;
            spoke[1] = center + direction * radius * 0.88f - side;
            spoke[2] = center + direction * radius * 0.88f + side;
            spoke[3] = center + direction * radius * 0.29f + side;
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, spoke, ink);
        }

        handle.DrawCircle(center, radius * 0.2f, SWConsoleStyle.Navigation.WithAlpha(alpha));
    }

    private static void DrawArc(DrawingHandleScreen handle, Vector2 center, float radius, float from, float sweep, Color color, float scale)
    {
        if (sweep <= 0f)
            return;

        var segments = Math.Max(2, (int) (sweep / MathF.Tau * 96));
        var previous = center + new Vector2(MathF.Cos(from), MathF.Sin(from)) * radius;
        for (var i = 1; i <= segments; i++)
        {
            var angle = from + sweep * i / segments;
            var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            handle.DrawLine(previous, point, color);
            previous = point;
        }

        var tip = center + new Vector2(MathF.Cos(from + sweep), MathF.Sin(from + sweep)) * radius;
        handle.DrawCircle(tip, 2.5f * scale, color);
    }

    private static void DrawEllipse(DrawingHandleScreen handle, Vector2 center, float radiusX, float radiusY, float progress, Color color)
    {
        if (progress <= 0f)
            return;

        const int segments = 120;
        var steps = Math.Max(2, (int) (segments * progress));
        var previous = center + new Vector2(0, -radiusY);
        for (var i = 1; i <= steps; i++)
        {
            var angle = -MathF.PI / 2 + MathF.Tau * i / segments;
            var point = center + new Vector2(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusY);
            // A dashed outline: every other segment is skipped.
            if (i % 2 == 0)
                handle.DrawLine(previous, point, color);
            previous = point;
        }
    }

    private static void DrawCentered(DrawingHandleScreen handle, Font font, float scale, string text, float centerX, float y, Color color)
    {
        if (text.Length == 0 || color.A <= 0f)
            return;

        var width = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, new Vector2(centerX - width / 2, y), text, scale, color);
    }

    private static List<string> Wrap(DrawingHandleScreen handle, Font font, float scale, string text, float maxWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && handle.GetDimensions(font, candidate, scale).X > maxWidth)
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
