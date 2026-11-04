using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Intro;

/// <summary>Staging of the intro: shots, camera moves, captions, place labels and the pixel-art canvas.</summary>
public sealed partial class SWSenateIntro
{
    /// <summary>Height of the pixel canvas in art pixels; the canvas is scaled by a whole number, at least 1.</summary>
    private const int CanvasHeight = 270;
    private const float BarReserve = 72f;
    private const float SideMargin = 24f;
    private const string ArtRoot = "/Textures/_SW/_Senate/Intro/";

    private enum Art
    {
        Sky,
        Shuttle,
        Rotunda,
        RotundaLights,
        Court,
        Drone,
        DeskBackground,
        DeskWindows,
        DeskForeground,
        DeskLampGlow,
        DeskScreen,
        Pillar,
    }

    /// <summary>Asset contract: file name and expected size in art pixels. Sprite sheets hold two frames side by side.</summary>
    private static readonly (Art Art, string File, int Width, int Height)[] ArtFiles =
    {
        (Art.Sky, "coruscant_sky.png", 720, 540),
        (Art.Shuttle, "shuttle.png", 64, 16),
        (Art.Rotunda, "rotunda.png", 1100, 460),
        (Art.RotundaLights, "rotunda_lights.png", 1100, 460),
        (Art.Court, "court.png", 720, 270),
        (Art.Drone, "holonet_drone.png", 32, 16),
        (Art.DeskBackground, "desk_background.png", 720, 270),
        (Art.DeskWindows, "desk_windows.png", 720, 270),
        (Art.DeskForeground, "desk_foreground.png", 720, 270),
        (Art.DeskLampGlow, "desk_lamp_glow.png", 720, 270),
        (Art.DeskScreen, "desk_screen.png", 720, 270),
        (Art.Pillar, "pillar.png", 120, 270),
    };

    /// <summary>Terminal screen on the desk, in desk art pixels; the role plate is anchored to it.</summary>
    private static readonly UIBox2 DeskScreenRect = new(176, 150, 232, 184);

    private sealed record CameraKey(float Time, float X, float Y);

    private sealed record Pin(float Start, float End, string Label, float X, float Y);

    private sealed record Caption(float Start, float End, string Label, string Text, string? Note = null);

    /// <summary>
    /// One continuous camera set-up. Camera keys are view centres in the base art's pixels. A shot that enters by a
    /// wipe starts when the wipe starts; the previous shot ends when it ends, and the pillar's centre divides them.
    /// </summary>
    private sealed record Shot(float Start, float End, Art Base, float FadeIn, bool WipeIn, CameraKey[] Camera, Pin[] Pins);

    // Wipes: a foreground pillar crosses the frame; the cut happens at the middle of the wipe.
    private const float WipeLength = 0.8f;
    private const float CourtWipe = 36.2f;
    private const float DeskWipe = 48.2f;

    private static readonly Shot[] Shots =
    {
        new(0f, 10.2f, Art.Sky, 0.8f, false,
            new[] { new CameraKey(1.5f, 360, 135), new CameraKey(10.2f, 360, 405) },
            Array.Empty<Pin>()),
        new(9.4f, CourtWipe + WipeLength, Art.Rotunda, 0.8f, false,
            new[]
            {
                new CameraKey(9.4f, 550, 135), new CameraKey(12.4f, 550, 300),
                new CameraKey(17.6f, 550, 300), new CameraKey(21.4f, 760, 310),
                new CameraKey(24.2f, 760, 310), new CameraKey(26.8f, 760, 140),
                new CameraKey(30.6f, 760, 140), new CameraKey(33.4f, 300, 140),
            },
            new[]
            {
                new Pin(12.6f, 17.8f, "СЕНАТОРЫ", 470, 330),
                new Pin(13.4f, 17.8f, "ГВАРДИЯ", 640, 372),
                new Pin(19.6f, 24.4f, "СПИКЕР", 760, 336),
                new Pin(26.8f, 31.0f, "КАНЦЛЕР", 808, 82),
                new Pin(33.4f, CourtWipe, "МИНИСТРЫ", 300, 112),
            }),
        new(CourtWipe, DeskWipe + WipeLength, Art.Court, 0f, true,
            new[] { new CameraKey(CourtWipe, 330, 135), new CameraKey(DeskWipe + 1f, 360, 135) },
            new[]
            {
                new Pin(38.0f, 47.8f, "СУДЬИ", 360, 92),
                new Pin(38.6f, 47.8f, "ОБВИНИТЕЛЬ", 165, 168),
                new Pin(39.2f, 47.8f, "ЗАЩИТНИК", 565, 170),
                new Pin(45.2f, 47.8f, "ГОЛОНЕТ", 548, 74),
            }),
        new(DeskWipe, Duration, Art.DeskBackground, 0f, true,
            new[] { new CameraKey(DeskWipe, 360, 135) },
            Array.Empty<Pin>()),
    };

    private static readonly Caption[] Captions =
    {
        new(2.5f, 9.4f, "КОРУСАНТ", "Сенат начинает созыв."),
        new(11.0f, 17.2f, "СЕНАТ", "20 секторов. Один голос у представленного сектора."),
        new(17.6f, 24.0f, "ЗАКОН", "Сенаторы вносят проекты. Спикер назначает заседания."),
        new(25.0f, 30.6f, "КАНЦЛЕР", "Подписывает принятые Сенатом проекты."),
        new(31.2f, 36.0f, "МИНИСТРЫ", "Ведут бюджет, программы и поставки."),
        new(38.0f, 43.0f, "СУД", "Суд может отменить неконституционный закон."),
        new(43.2f, 48.2f, "ГОЛОНЕТ", "Корреспонденты выводят заседания в эфир."),
        new(49.6f, 55.4f, "ТРИ ДНЯ", "Стандартный созыв — три рабочих дня.", "07:00–21:00, игровое время."),
    };

    private static readonly Vector2 ShuttleFrom = new(-40, 300);
    private static readonly Vector2 ShuttleTo = new(430, 392);
    private static readonly Vector2 DroneFrom = new(740, 30);
    /// <summary>Hover point of the drone (sprite top-left); the ГОЛОНЕТ label points at its body (548, 74).</summary>
    private static readonly Vector2 DroneHover = new(540, 66);

    private const float RoleStart = 55.6f;
    private const float OutroLength = 1.4f;

    private readonly Dictionary<Art, Texture> _art = new();

    // Per-frame canvas state.
    private int _pixel = 1;
    private float _viewWidth = 1f;
    private UIBox2 _canvas;
    private Vector2 _camera;
    private Vector2 _artSize;
    private float _clipLeft = float.NegativeInfinity;
    private float _clipRight = float.PositiveInfinity;
    private int _missingArt;

    private void LoadArt()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        foreach (var (art, file, _, _) in ArtFiles)
        {
            if (cache.TryGetResource<TextureResource>(new ResPath(ArtRoot + file), out var resource))
                _art[art] = resource.Texture;
            else
                _missingArt++;
        }
    }

    private static Vector2 ContractSize(Art art)
    {
        foreach (var entry in ArtFiles)
        {
            if (entry.Art == art)
                return new Vector2(entry.Width, entry.Height);
        }

        return new Vector2(1, 1);
    }

    private Vector2 ArtSize(Art art) =>
        _art.TryGetValue(art, out var texture) ? new Vector2(texture.Width, texture.Height) : ContractSize(art);

    // ---- frame ----------------------------------------------------------

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        if (size.X < 1f || size.Y < 1f)
            return;

        var ui = UIScale;
        handle.DrawRect(new UIBox2(Vector2.Zero, size), Backdrop.WithAlpha(Clamp01(_elapsed / 0.8f)));

        // Integer pixel-art scale, at least 1, chosen from the height left above the control bar.
        var room = MathF.Max(0f, size.Y - BarReserve * ui);
        _pixel = Math.Max(1, (int) MathF.Floor(room / CanvasHeight));
        _viewWidth = MathF.Max(1f, size.X / _pixel);
        var top = MathF.Floor(MathF.Max(0f, (room - CanvasHeight * _pixel) / 2));
        // On windows lower than one canvas plus the bar, the canvas is cut at the bar instead of running under it.
        var bottom = MathF.Max(top + 1f, MathF.Min(top + CanvasHeight * _pixel, room));
        _canvas = new UIBox2(0, top, size.X, bottom);

        for (var i = 0; i < Shots.Length; i++)
        {
            var shot = Shots[i];
            var alpha = ShotAlpha(shot);
            if (alpha <= 0f)
                continue;

            _clipLeft = shot.WipeIn && !_reducedMotion ? WipeCentre(shot.Start) : float.NegativeInfinity;
            _clipRight = i + 1 < Shots.Length && Shots[i + 1].WipeIn && !_reducedMotion
                ? WipeCentre(Shots[i + 1].Start)
                : float.PositiveInfinity;
            if (_clipRight <= _clipLeft)
                continue;

            BeginShot(shot);
            DrawShot(handle, shot, alpha);
            DrawPins(handle, shot, alpha, ui);
        }

        _clipLeft = float.NegativeInfinity;
        _clipRight = float.PositiveInfinity;
        DrawWipe(handle, CourtWipe);
        DrawWipe(handle, DeskWipe);

        if (_missingArt > 0)
            DrawPlaceholderTag(handle, ui);

        foreach (var caption in Captions)
            DrawCaption(handle, size, ui, caption);

        DrawRole(handle, size, ui);

        var outro = Phase(Duration - OutroLength, OutroLength);
        if (outro > 0f)
            handle.DrawRect(new UIBox2(Vector2.Zero, size), Backdrop.WithAlpha(outro));
    }

    private float ShotAlpha(Shot shot)
    {
        if (_elapsed < shot.Start || _elapsed >= shot.End)
            return 0f;

        // The incoming shot fades in over the outgoing one, which stays opaque, so a dissolve never dips to black.
        // Under reduced motion a wipe becomes such a dissolve.
        var fadeIn = shot.WipeIn ? (_reducedMotion ? WipeLength : 0f) : shot.FadeIn;
        return Phase(shot.Start, fadeIn);
    }

    /// <summary>Screen x of the pillar's centre during a wipe; beyond the edges before and after it.</summary>
    private float WipeCentre(float start)
    {
        var t = Phase(start, WipeLength);
        if (t <= 0f)
            return float.PositiveInfinity;
        if (t >= 1f)
            return float.NegativeInfinity;

        var pillar = ArtSize(Art.Pillar).X;
        var x = _viewWidth - (_viewWidth + pillar) * EaseInOut(t);
        return _canvas.Left + (MathF.Round(x) + pillar / 2) * _pixel;
    }

    private void BeginShot(Shot shot)
    {
        _artSize = ArtSize(shot.Base);
        var centre = CameraAt(shot.Camera);
        var halfW = _viewWidth / 2;
        var halfH = CanvasHeight / 2f;
        // Camera framing always uses the full canvas height; a cut-down canvas only crops the bottom.
        var x = _artSize.X <= _viewWidth ? _artSize.X / 2 : Math.Clamp(centre.X, halfW, _artSize.X - halfW);
        var y = _artSize.Y <= CanvasHeight ? _artSize.Y / 2 : Math.Clamp(centre.Y, halfH, _artSize.Y - halfH);
        // Snap to whole screen pixels, so scaled art pixels never shimmer during a pan.
        var left = MathF.Round((x - halfW) * _pixel) / _pixel;
        var topEdge = MathF.Round((y - halfH) * _pixel) / _pixel;
        _camera = new Vector2(left, topEdge);
    }

    private Vector2 CameraAt(CameraKey[] keys)
    {
        if (keys.Length == 0)
            return _artSize / 2;
        if (_elapsed <= keys[0].Time || keys.Length == 1)
            return new Vector2(keys[0].X, keys[0].Y);

        for (var i = 1; i < keys.Length; i++)
        {
            var from = keys[i - 1];
            var to = keys[i];
            if (_elapsed > to.Time)
                continue;

            var length = to.Time - from.Time;
            var t = length <= 0f ? 1f : (_elapsed - from.Time) / length;
            // Reduced motion: hold each framing and cut to the next one halfway through the move.
            t = _reducedMotion ? (t < 0.5f ? 0f : 1f) : EaseInOut(t);
            return new Vector2(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }

        var last = keys[^1];
        return new Vector2(last.X, last.Y);
    }

    private Vector2 ToScreen(Vector2 art) =>
        new(_canvas.Left + (art.X - _camera.X) * _pixel, _canvas.Top + (art.Y - _camera.Y) * _pixel);

    private UIBox2 ToScreen(UIBox2 art) => new(ToScreen(art.TopLeft), ToScreen(art.BottomRight));

    // ---- shots ----------------------------------------------------------

    private void DrawShot(DrawingHandleScreen handle, Shot shot, float alpha)
    {
        switch (shot.Base)
        {
            case Art.Sky:
                DrawLayer(handle, Art.Sky, alpha);
                DrawShuttle(handle, alpha);
                break;
            case Art.Rotunda:
                DrawLayer(handle, Art.Rotunda, alpha);
                var sweep = _reducedMotion ? Phase(10.6f, 0f) : EaseInOut(Phase(10.6f, 2.8f));
                DrawLayer(handle, Art.RotundaLights, alpha, clipRight: _artSize.X * sweep);
                break;
            case Art.Court:
                DrawLayer(handle, Art.Court, alpha);
                DrawDrone(handle, alpha);
                break;
            case Art.DeskBackground:
                DrawLayer(handle, Art.DeskBackground, alpha);
                DrawLayer(handle, Art.DeskWindows, alpha, tint: WindowLight());
                DrawLayer(handle, Art.DeskForeground, alpha);
                DrawLayer(handle, Art.DeskLampGlow, alpha * EaseInOut(Phase(RoleStart - 0.4f, _reducedMotion ? 0f : 0.8f)));
                DrawLayer(handle, Art.DeskScreen, alpha * Phase(RoleStart, _reducedMotion ? 0f : 0.3f));
                break;
        }
    }

    /// <summary>Window light runs from morning through evening while the three-day caption is shown.</summary>
    private Color WindowLight()
    {
        var morning = Color.FromHex("#E8B27A");
        var day = Color.FromHex("#CFE3F2");
        var evening = Color.FromHex("#9A6A86");
        var night = Color.FromHex("#3A4766");
        if (_reducedMotion)
            return day;

        var t = Phase(49.6f, 5.4f) * 3f;
        if (t < 1f) return Color.InterpolateBetween(morning, day, EaseInOut(t));
        if (t < 2f) return Color.InterpolateBetween(day, evening, EaseInOut(t - 1f));
        return Color.InterpolateBetween(evening, night, EaseInOut(t - 2f));
    }

    /// <summary>Draws a layer the size of the shot's base art, clipped to the canvas and optionally to an art x limit.</summary>
    private void DrawLayer(DrawingHandleScreen handle, Art art, float alpha, Color? tint = null, float clipRight = float.PositiveInfinity)
    {
        if (alpha <= 0f || clipRight <= 0f)
            return;

        var size = ArtSize(art);
        var screenLeft = MathF.Max(_canvas.Left, _clipLeft);
        var screenRight = MathF.Min(_canvas.Right, _clipRight);
        var x0 = MathF.Max(0f, _camera.X + (screenLeft - _canvas.Left) / _pixel);
        var y0 = MathF.Max(0f, _camera.Y);
        var x1 = MathF.Min(MathF.Min(size.X, clipRight), _camera.X + (screenRight - _canvas.Left) / _pixel);
        var y1 = MathF.Min(size.Y, _camera.Y + _canvas.Height / _pixel);
        if (x1 <= x0 || y1 <= y0)
            return;

        var region = new UIBox2(x0, y0, x1, y1);
        var colour = (tint ?? Color.White).WithAlpha(alpha);
        if (_art.TryGetValue(art, out var texture))
        {
            handle.DrawTextureRectRegion(texture, ToScreen(region), region, colour);
            return;
        }

        DrawPlaceholder(handle, art, region, colour);
    }

    private void DrawSprite(DrawingHandleScreen handle, Art art, Vector2 position, int frame, float alpha)
    {
        var sheet = ArtSize(art);
        var frameWidth = sheet.X / 2;
        var region = new UIBox2(frameWidth * frame, 0, frameWidth * (frame + 1), sheet.Y);
        var snapped = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        var dest = ToScreen(new UIBox2(snapped, snapped + new Vector2(frameWidth, sheet.Y)));
        // Crop the sprite to the visible part of the shot, cutting it at the edge instead of popping it.
        var visible = new UIBox2(MathF.Max(dest.Left, MathF.Max(_canvas.Left, _clipLeft)), MathF.Max(dest.Top, _canvas.Top),
            MathF.Min(dest.Right, MathF.Min(_canvas.Right, _clipRight)), MathF.Min(dest.Bottom, _canvas.Bottom));
        if (visible.Width <= 0 || visible.Height <= 0)
            return;

        region = new UIBox2(
            region.Left + (visible.Left - dest.Left) / _pixel, region.Top + (visible.Top - dest.Top) / _pixel,
            region.Right - (dest.Right - visible.Right) / _pixel, region.Bottom - (dest.Bottom - visible.Bottom) / _pixel);
        dest = visible;

        if (_art.TryGetValue(art, out var texture))
        {
            handle.DrawTextureRectRegion(texture, dest, region, Color.White.WithAlpha(alpha));
            return;
        }

        FillClipped(handle, dest, Color.FromHex(art == Art.Drone ? "#7C8493" : "#C9CED6").WithAlpha(alpha));
        var light = new UIBox2(dest.Right - 3 * _pixel, dest.Top + 2 * _pixel, dest.Right - _pixel, dest.Top + 4 * _pixel);
        FillClipped(handle, light, Color.FromHex(art == Art.Drone ? "#E05252" : "#F2D28B").WithAlpha(alpha));
    }

    private int SpriteFrame => _reducedMotion ? 0 : (int) (_elapsed / 0.2f) % 2;

    private void DrawShuttle(DrawingHandleScreen handle, float alpha)
    {
        if (_reducedMotion)
            return;

        var t = Phase(3.5f, 5.5f);
        if (t <= 0f || t >= 1f)
            return;

        // The shuttle settles over the dome; it fades out there instead of vanishing.
        DrawSprite(handle, Art.Shuttle, Vector2.Lerp(ShuttleFrom, ShuttleTo, EaseInOut(t)), SpriteFrame, alpha * (1f - Phase(8.6f, 0.4f)));
    }

    private void DrawDrone(DrawingHandleScreen handle, float alpha)
    {
        // Reduced motion: the drone is simply there, so its label still points at something.
        if (_reducedMotion)
        {
            if (_elapsed >= 43.6f)
                DrawSprite(handle, Art.Drone, DroneHover, 0, alpha);
            return;
        }

        var t = Phase(43.6f, 1.6f);
        if (t <= 0f)
            return;

        var position = Vector2.Lerp(DroneFrom, DroneHover, EaseInOut(t));
        if (t >= 1f)
            position.Y += MathF.Round(MathF.Sin(_elapsed * 2.6f));
        DrawSprite(handle, Art.Drone, position, SpriteFrame, alpha);
    }

    private void DrawWipe(DrawingHandleScreen handle, float start)
    {
        if (_reducedMotion)
            return;

        var t = Phase(start, WipeLength);
        if (t <= 0f || t >= 1f)
            return;

        // The pillar runs right to left in view space, faster than any camera move.
        var pillar = ArtSize(Art.Pillar);
        var x = _viewWidth - (_viewWidth + pillar.X) * EaseInOut(t);
        // Cropped, not squashed, where a low window cuts the canvas above the control bar.
        var bottom = MathF.Min(_canvas.Top + MathF.Min(pillar.Y, CanvasHeight) * _pixel, _canvas.Bottom);
        var dest = new UIBox2(_canvas.Left + MathF.Round(x) * _pixel, _canvas.Top,
            _canvas.Left + (MathF.Round(x) + pillar.X) * _pixel, bottom);
        if (_art.TryGetValue(Art.Pillar, out var texture))
        {
            var region = new UIBox2(0, 0, pillar.X, (bottom - _canvas.Top) / _pixel);
            handle.DrawTextureRectRegion(texture, dest, region, Color.White);
            return;
        }

        FillClipped(handle, dest, Color.FromHex("#1A2230"));
        var band = new UIBox2(dest.Left + 18 * _pixel, dest.Top, dest.Left + 22 * _pixel, dest.Bottom);
        FillClipped(handle, band, Ink.WithAlpha(0.6f));
    }

    // ---- labels ---------------------------------------------------------

    private void DrawPins(DrawingHandleScreen handle, Shot shot, float shotAlpha, float ui)
    {
        foreach (var pin in shot.Pins)
        {
            var alpha = shotAlpha * FadeWindow(pin.Start, pin.End, 0.5f, 0.4f);
            if (alpha <= 0f)
                continue;

            var anchor = ToScreen(new Vector2(pin.X, pin.Y));
            if (!_canvas.Contains(anchor) || anchor.X < _clipLeft || anchor.X > _clipRight)
                continue;

            var padding = 6f * ui;
            var lineHeight = _labelFont!.GetLineHeight(ui);
            var maxWidth = MathF.Max(1f, _canvas.Width - 2 * SideMargin * ui);
            var label = Ellipsize(handle, _labelFont!, ui, pin.Label, maxWidth - padding * 2);
            var width = TextWidth(handle, _labelFont!, ui, label) + padding * 2;
            var height = lineHeight + padding;
            var left = Math.Clamp(anchor.X - width / 2, SideMargin * ui, MathF.Max(SideMargin * ui, _canvas.Right - SideMargin * ui - width));
            var topEdge = MathF.Max(_canvas.Top + 4f * ui, anchor.Y - 18f * ui - height);
            var plate = UIBox2.FromDimensions(new Vector2(left, topEdge), new Vector2(width, height));
            handle.DrawLine(new Vector2(anchor.X, plate.Bottom), anchor, Ink.WithAlpha(alpha * 0.8f));
            handle.DrawRect(UIBox2.FromDimensions(anchor - new Vector2(_pixel), new Vector2(2 * _pixel)), Ink.WithAlpha(alpha));
            handle.DrawRect(plate, Backdrop.WithAlpha(alpha * 0.85f));
            handle.DrawRect(new UIBox2(plate.Left, plate.Bottom - 2f * ui, plate.Right, plate.Bottom), Ink.WithAlpha(alpha));
            handle.DrawString(_labelFont!, new Vector2(plate.Left + padding, plate.Top + padding / 2), label, ui, Bright.WithAlpha(alpha));
        }
    }

    /// <summary>Static caption plate in the lower left of the canvas, above the control bar.</summary>
    private void DrawCaption(DrawingHandleScreen handle, Vector2 size, float ui, Caption caption)
    {
        var alpha = FadeWindow(caption.Start, caption.End, 0.6f, 0.6f);
        if (alpha <= 0f)
            return;

        var margin = SideMargin * ui;
        var padding = 14f * ui;
        var width = MathF.Max(1f, MathF.Min(540f * ui, size.X - margin * 2));
        var inner = MathF.Max(1f, width - padding * 2 - 3f * ui);
        var labelLine = _labelFont!.GetLineHeight(ui);
        var textLine = _textFont!.GetLineHeight(ui);
        var smallLine = _smallFont!.GetLineHeight(ui);
        var bottom = MathF.Min(_canvas.Bottom, size.Y - BarReserve * ui) - 12f * ui;
        var room = bottom - 4f * ui;
        var note = caption.Note;
        var lines = Fit(handle, _textFont!, ui, caption.Text, inner, 2);
        float Height() => padding * 2 + labelLine + 4f * ui + lines.Count * textLine + (note != null ? 4f * ui + smallLine : 0f);
        // On low windows drop the note first, then the second line, instead of running under the bar.
        if (Height() > room)
            note = null;
        if (Height() > room)
            lines = Fit(handle, _textFont!, ui, caption.Text, inner, 1);
        var height = Height();
        var plate = UIBox2.FromDimensions(new Vector2(margin, MathF.Max(4f * ui, bottom - height)), new Vector2(width, height));

        handle.DrawRect(plate, Backdrop.WithAlpha(alpha * 0.88f));
        handle.DrawRect(new UIBox2(plate.Left, plate.Top, plate.Left + 3f * ui, plate.Bottom), Ink.WithAlpha(alpha));
        var x = plate.Left + 3f * ui + padding;
        var y = plate.Top + padding;
        DrawLeft(handle, _labelFont!, ui, caption.Label, x, y, inner, Ink.WithAlpha(alpha));
        y += labelLine + 4f * ui;
        foreach (var line in lines)
        {
            handle.DrawString(_textFont!, new Vector2(x, y), line, ui, Bright.WithAlpha(alpha));
            y += textLine;
        }

        if (note != null)
            DrawLeft(handle, _smallFont!, ui, note, x, y + 4f * ui, inner, Dim.WithAlpha(alpha));
    }

    /// <summary>The role appears next to the terminal screen on the desk.</summary>
    private void DrawRole(DrawingHandleScreen handle, Vector2 size, float ui)
    {
        var alpha = MathF.Min(Phase(RoleStart, _reducedMotion ? 0.4f : 0.8f), 1f - Phase(Duration - OutroLength, OutroLength));
        if (alpha <= 0f)
            return;

        BeginShot(Shots[^1]);
        var screen = ToScreen(DeskScreenRect);
        var margin = SideMargin * ui;
        var padding = 16f * ui;
        var width = MathF.Max(1f, MathF.Min(420f * ui, size.X - margin * 2));
        var inner = MathF.Max(1f, width - padding * 2 - 3f * ui);
        var title = _role?.Title ?? "Должность не выбрана";
        var step = _role?.FirstStep ?? "Выберите должность в настройках персонажа.";
        var labelLine = _labelFont!.GetLineHeight(ui);
        var roleLine = _roleFont!.GetLineHeight(ui);
        var textLine = _smallFont!.GetLineHeight(ui);
        var safeBottom = size.Y - BarReserve * ui - 12f * ui;
        var room = safeBottom - 4f * ui;
        var maxLines = 3;
        var lines = Fit(handle, _smallFont!, ui, step, inner, maxLines);
        while (maxLines > 1 && padding * 2 + labelLine + 6f * ui + roleLine + 6f * ui + lines.Count * textLine > room)
            lines = Fit(handle, _smallFont!, ui, step, inner, --maxLines);
        var height = padding * 2 + labelLine + 6f * ui + roleLine + 6f * ui + lines.Count * textLine;
        var left = Math.Clamp(screen.Right + 16f * ui, margin, MathF.Max(margin, size.X - margin - width));
        var topEdge = Math.Clamp(screen.Top - height / 2, 4f * ui, MathF.Max(4f * ui, safeBottom - height));
        var plate = UIBox2.FromDimensions(new Vector2(left, topEdge), new Vector2(width, height));

        if (plate.Left > screen.Right)
            handle.DrawLine(new Vector2(screen.Right, screen.Center.Y), new Vector2(plate.Left, screen.Center.Y), Accent.WithAlpha(alpha * 0.7f));
        handle.DrawRect(plate, Backdrop.WithAlpha(alpha * 0.92f));
        handle.DrawRect(plate, Accent.WithAlpha(alpha * 0.6f), false);
        handle.DrawRect(new UIBox2(plate.Left, plate.Top, plate.Left + 3f * ui, plate.Bottom), Ink.WithAlpha(alpha));
        var x = plate.Left + 3f * ui + padding;
        var y = plate.Top + padding;
        DrawLeft(handle, _labelFont!, ui, "ВАША РОЛЬ", x, y, inner, Ink.WithAlpha(alpha));
        y += labelLine + 6f * ui;
        DrawLeftFitted(handle, _roleFont!, ui, title, x, y, inner, Bright.WithAlpha(alpha));
        y += roleLine + 6f * ui;
        foreach (var line in lines)
        {
            handle.DrawString(_smallFont!, new Vector2(x, y), line, ui, Dim.WithAlpha(alpha));
            y += textLine;
        }
    }

    private void DrawPlaceholderTag(DrawingHandleScreen handle, float ui)
    {
        var padding = 6f * ui;
        var text = Ellipsize(handle, _labelFont!, ui, $"ЗАГЛУШКА: нет файлов арта ({_missingArt} из {ArtFiles.Length})",
            MathF.Max(1f, _canvas.Width - 16f * ui - padding * 2));
        var width = TextWidth(handle, _labelFont!, ui, text) + padding * 2;
        var plate = UIBox2.FromDimensions(new Vector2(MathF.Max(0f, _canvas.Right - width - 8f * ui), _canvas.Top + 8f * ui),
            new Vector2(width, _labelFont!.GetLineHeight(ui) + padding));
        handle.DrawRect(plate, Color.FromHex("#3D2C30"));
        handle.DrawString(_labelFont!, plate.TopLeft + new Vector2(padding, padding / 2), text, ui, SWConsoleStyle.Bad);
    }

    // ---- development placeholders ---------------------------------------

    private void FillClipped(DrawingHandleScreen handle, UIBox2 box, Color color)
    {
        var rect = new UIBox2(MathF.Max(box.Left, MathF.Max(_canvas.Left, _clipLeft)), MathF.Max(box.Top, _canvas.Top),
            MathF.Min(box.Right, MathF.Min(_canvas.Right, _clipRight)), MathF.Min(box.Bottom, _canvas.Bottom));
        if (rect.Width > 0 && rect.Height > 0)
            handle.DrawRect(rect, color);
    }

    private void Block(DrawingHandleScreen handle, float x0, float y0, float x1, float y1, string hex, Color colour, UIBox2 region)
    {
        var box = new UIBox2(MathF.Max(x0, region.Left), MathF.Max(y0, region.Top), MathF.Min(x1, region.Right), MathF.Min(y1, region.Bottom));
        if (box.Width <= 0 || box.Height <= 0)
            return;

        var c = Color.FromHex(hex);
        FillClipped(handle, ToScreen(box), new Color(c.R * colour.R, c.G * colour.G, c.B * colour.B, colour.A));
    }

    /// <summary>Flat blockouts in art coordinates: they only show framing and timing, they are not art.</summary>
    private void DrawPlaceholder(DrawingHandleScreen handle, Art art, UIBox2 region, Color colour)
    {
        switch (art)
        {
            case Art.Sky:
                string[] bands = { "#0B1020", "#141B33", "#25294A", "#4A3B5C", "#8A5A5E", "#C98A62" };
                for (var i = 0; i < bands.Length; i++)
                    Block(handle, 0, i * 60, 720, i * 60 + 60, bands[i], colour, region);
                Block(handle, 0, 360, 720, 540, "#151A26", colour, region);
                for (var i = 0; i < 12; i++)
                    Block(handle, i * 62, 300 - (i * 37 % 90), i * 62 + 40, 540, "#1C2333", colour, region);
                Block(handle, 300, 380, 420, 540, "#2A3346", colour, region);
                Block(handle, 330, 360, 390, 380, "#2A3346", colour, region);
                break;
            case Art.Rotunda:
                Block(handle, 0, 0, 1100, 460, "#121926", colour, region);
                Block(handle, 160, 60, 440, 170, "#1D2636", colour, region);
                for (var i = 0; i < 4; i++)
                    Block(handle, 180 + i * 66, 80, 226 + i * 66, 160, "#2B3548", colour, region);
                Block(handle, 690, 90, 830, 150, "#2E3A50", colour, region);
                for (var row = 0; row < 3; row++)
                    for (var i = 0; i < 14; i++)
                        Block(handle, 40 + i * 76 + row * 20, 250 + row * 50, 92 + i * 76 + row * 20, 280 + row * 50, "#273042", colour, region);
                Block(handle, 720, 330, 800, 400, "#3A4660", colour, region);
                Block(handle, 620, 352, 660, 420, "#303A4E", colour, region);
                break;
            case Art.RotundaLights:
                for (var row = 0; row < 3; row++)
                    for (var i = 0; i < 14; i++)
                        Block(handle, 52 + i * 76 + row * 20, 254 + row * 50, 80 + i * 76 + row * 20, 260 + row * 50, "#D4B57D", colour, region);
                break;
            case Art.Court:
                Block(handle, 0, 0, 720, 270, "#141A24", colour, region);
                Block(handle, 220, 70, 500, 120, "#2E3A50", colour, region);
                Block(handle, 170, 170, 230, 230, "#2B3548", colour, region);
                Block(handle, 490, 170, 550, 230, "#2B3548", colour, region);
                Block(handle, 0, 230, 720, 270, "#1B2230", colour, region);
                break;
            case Art.DeskBackground:
                Block(handle, 0, 0, 720, 270, "#131A26", colour, region);
                Block(handle, 300, 140, 720, 220, "#1F2838", colour, region);
                break;
            case Art.DeskWindows:
                for (var i = 0; i < 4; i++)
                    Block(handle, 420 + i * 70, 20, 470 + i * 70, 130, "#FFFFFF", colour, region);
                break;
            case Art.DeskForeground:
                Block(handle, 0, 200, 300, 270, "#2A2F3A", colour, region);
                Block(handle, 120, 160, 150, 200, "#3A3F4A", colour, region);
                Block(handle, 170, 146, 238, 196, "#20262F", colour, region);
                break;
            case Art.DeskLampGlow:
                Block(handle, 96, 150, 176, 210, "#F2C27A", colour.WithAlpha(colour.A * 0.35f), region);
                break;
            case Art.DeskScreen:
                Block(handle, 176, 150, 232, 184, "#93D9F3", colour.WithAlpha(colour.A * 0.6f), region);
                break;
        }
    }
}
