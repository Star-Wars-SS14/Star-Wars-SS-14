using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate;

/// <summary>
/// Staging of the outro: the pixel panorama above the fact panel. Art is 960×360 art pixels; the view is at most
/// 720×270, scaled by a whole number and never stretched. On cramped windows the view is cropped, not squeezed.
/// </summary>
public sealed partial class SWSenateFinale
{
    private const string ArtRoot = "/Textures/_SW/_Senate/Outro/";
    private const int ArtWidth = 960;
    private const int ArtHeight = 360;
    private const int ViewWidth = 720;
    private const int ViewHeight = 270;
    private static readonly Vector2 FixedCentre = new(480, 180);

    // Layout in UI units.
    private const float BarReserve = 64f;
    private const float TopMargin = 16f;
    private const float SideMargin = 16f;
    private const float Gap = 10f;
    private const float PanelMin = 128f;
    private const float PanelMax = 210f;
    private const float PanelWidth = 560f;

    private const float CrossFade = 0.8f;
    private const float ReducedCrossFade = 0.5f;
    private const float EndFade = 1.2f;

    private enum Art
    {
        Dusk,
        Chamber,
        ChamberLights,
        Archive,
        ArchiveLamp,
        Terminal,
    }

    private static readonly (Art Art, string File)[] ArtFiles =
    {
        (Art.Dusk, "coruscant_dusk.png"),
        (Art.Chamber, "chamber_after_session.png"),
        (Art.ChamberLights, "chamber_lights.png"),
        (Art.Archive, "archive_desk.png"),
        (Art.ArchiveLamp, "archive_lamp.png"),
        (Art.Terminal, "sector_terminal.png"),
    };

    private readonly Dictionary<Art, Texture> _art = new();
    private int _missingArt;

    private Vector2 _layoutSize = new(-1, -1);
    private float _layoutScale = -1f;
    private int _pixel = 1;
    private Vector2i _view;
    private UIBox2 _canvas;

    private void LoadArt()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        foreach (var (art, file) in ArtFiles)
        {
            // A file of the wrong size is treated as missing rather than drawn misaligned.
            if (cache.TryGetResource<TextureResource>(new ResPath(ArtRoot + file), out var resource) &&
                resource.Texture.Width == ArtWidth && resource.Texture.Height == ArtHeight)
                _art[art] = resource.Texture;
            else
                _missingArt++;
        }
    }

    /// <summary>Places the panorama and the fact panel. Runs from FrameUpdate when the size or UI scale changes.</summary>
    private void UpdateLayout()
    {
        var size = Size;
        var ui = UIScale;
        if (size == _layoutSize && MathHelper.CloseTo(ui, _layoutScale))
            return;

        _layoutSize = size;
        _layoutScale = ui;
        if (size.X < 1f || size.Y < 1f || ui <= 0f)
            return;

        var avail = MathF.Max(0f, size.Y - BarReserve - TopMargin);
        var desiredPanel = Math.Clamp(avail * 0.42f, PanelMin, PanelMax);
        var artRoomPx = MathF.Max(0f, (avail - desiredPanel - Gap) * ui);
        var widthPx = MathF.Max(0f, (size.X - SideMargin * 2) * ui);
        _pixel = Math.Max(1, Math.Min((int) (artRoomPx / ViewHeight), (int) (widthPx / ViewWidth)));
        var viewW = (int) MathF.Min(ViewWidth, widthPx / _pixel);
        var viewH = (int) MathF.Min(ViewHeight, artRoomPx / _pixel);
        // Too cramped for a picture: keep the facts and the controls.
        if (viewW < 96 || viewH < 48)
            viewW = viewH = 0;
        _view = new Vector2i(viewW, viewH);

        var canvasW = viewW * _pixel / ui;
        var canvasH = viewH * _pixel / ui;
        var gap = viewH > 0 ? Gap : 0f;
        var panelH = Math.Clamp(avail - canvasH - gap, 48f, PanelMax);
        var stack = canvasH + gap + panelH;
        var top = TopMargin + MathF.Max(0f, (avail - stack) / 2f);

        var leftPx = MathF.Floor((size.X * ui - viewW * _pixel) / 2f);
        var topPx = MathF.Round(top * ui);
        _canvas = UIBox2.FromDimensions(new Vector2(leftPx, topPx), new Vector2(viewW * _pixel, viewH * _pixel));

        var panelW = MathF.Max(1f, MathF.Min(size.X - SideMargin * 2, MathF.Max(canvasW, PanelWidth)));
        var panelX = MathF.Floor((size.X - panelW) / 2f);
        var panelY = top + canvasH + gap;
        SetMarginLeft(_panel, panelX);
        SetMarginTop(_panel, panelY);
        SetMarginRight(_panel, panelX + panelW);
        SetMarginBottom(_panel, panelY + panelH);
        ShowAllRows();
    }

    // ---- frame ----------------------------------------------------------

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        if (size.X < 1f || size.Y < 1f)
            return;

        handle.DrawRect(new UIBox2(Vector2.Zero, size), Backdrop.WithAlpha(Clamp01(_elapsed / 0.6f)));
        if (_view.X <= 0 || _view.Y <= 0 || _scenes.Length == 0)
            return;

        var index = SceneAt(_elapsed);
        var scene = _scenes[index];
        var fade = _reducedMotion ? ReducedCrossFade : CrossFade;
        var alpha = Phase(scene.Start, fade);
        // The incoming shot dissolves over the outgoing one, which stays opaque, so a cut never dips to black.
        if (index > 0 && alpha < 1f)
            DrawShot(handle, _scenes[index - 1], 1f, 1f);
        DrawShot(handle, scene, alpha, Clamp01((_elapsed - scene.Start) / scene.Length));

        var end = Phase(_total - EndFade, EndFade);
        if (end > 0f)
            handle.DrawRect(_canvas, Backdrop.WithAlpha(end));
        handle.DrawRect(_canvas, SWConsoleStyle.Edge, false);

        if (_missingArt > 0)
            DrawPlaceholderTag(handle);
    }

    private void DrawShot(DrawingHandleScreen handle, Scene scene, float alpha, float t)
    {
        if (alpha <= 0f)
            return;

        var region = ViewRegion(scene, t);
        switch (scene.Shot)
        {
            case Shot.Dusk:
                DrawLayer(handle, Art.Dusk, region, alpha);
                break;
            case Shot.Chamber:
                DrawLayer(handle, Art.Chamber, region, alpha);
                // The hall lights go down slowly after the last session; a fade, never a flicker.
                DrawLayer(handle, Art.ChamberLights, region, alpha * (1f - 0.4f * EaseInOut(t)));
                break;
            case Shot.Archive:
                DrawLayer(handle, Art.Archive, region, alpha);
                DrawLayer(handle, Art.ArchiveLamp, region, alpha);
                break;
            case Shot.Terminal:
                DrawLayer(handle, Art.Terminal, region, alpha);
                break;
            case Shot.Closing:
                // The same desk; the lamp goes out and the room darkens before the report opens.
                var local = t * scene.Length;
                var dark = EaseInOut(Clamp01((local - 1.5f) / MathF.Max(0.1f, scene.Length - 3.5f)));
                DrawLayer(handle, Art.Archive, region, alpha);
                DrawLayer(handle, Art.ArchiveLamp, region, alpha * (1f - dark));
                handle.DrawRect(_canvas, Backdrop.WithAlpha(alpha * 0.55f * dark));
                break;
        }
    }

    /// <summary>Visible art region for a shot; reduced motion holds the fixed centre without any pan.</summary>
    private UIBox2 ViewRegion(Scene scene, float t)
    {
        var centre = _reducedMotion ? FixedCentre : Vector2.Lerp(scene.From, scene.To, EaseInOut(t));
        // Snap to whole screen pixels, so scaled art pixels never shimmer during a pan.
        var left = MathF.Round((centre.X - _view.X / 2f) * _pixel) / _pixel;
        var top = MathF.Round((centre.Y - _view.Y / 2f) * _pixel) / _pixel;
        left = Math.Clamp(left, 0f, ArtWidth - _view.X);
        top = Math.Clamp(top, 0f, ArtHeight - _view.Y);
        return new UIBox2(left, top, left + _view.X, top + _view.Y);
    }

    private void DrawLayer(DrawingHandleScreen handle, Art art, UIBox2 region, float alpha)
    {
        if (alpha <= 0f)
            return;

        if (_art.TryGetValue(art, out var texture))
        {
            handle.DrawTextureRectRegion(texture, _canvas, region, Color.White.WithAlpha(alpha));
            return;
        }

        // Development fallback for a missing file: flat tone only, never presented as art.
        var tone = art switch
        {
            Art.Dusk => "#2A2440",
            Art.Chamber => "#151C2A",
            Art.Archive => "#1E1A16",
            Art.Terminal => "#0F1C24",
            _ => null,
        };
        if (tone != null)
            handle.DrawRect(_canvas, Color.FromHex(tone).WithAlpha(alpha));
    }

    private void DrawPlaceholderTag(DrawingHandleScreen handle)
    {
        var ui = UIScale;
        var font = _noteFont!;
        var text = $"ЗАГЛУШКА: нет файлов арта ({_missingArt} из {ArtFiles.Length})";
        var padding = 6f * ui;
        var width = MathF.Min(handle.GetDimensions(font, text, ui).X + padding * 2, _canvas.Width);
        var plate = UIBox2.FromDimensions(new Vector2(_canvas.Right - width, _canvas.Top), new Vector2(width, font.GetLineHeight(ui) + padding));
        handle.DrawRect(plate, Color.FromHex("#3D2C30"));
        if (handle.GetDimensions(font, text, ui).X + padding * 2 <= _canvas.Width)
            handle.DrawString(font, plate.TopLeft + new Vector2(padding, padding / 2), text, ui, SWConsoleStyle.Bad);
    }

    // ---- timing helpers -------------------------------------------------

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static float EaseInOut(float value)
    {
        value = Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private float Phase(float start, float length) =>
        length <= 0f ? (_elapsed >= start ? 1f : 0f) : Clamp01((_elapsed - start) / length);

    private float FadeWindow(float start, float end, float fadeIn, float fadeOut) =>
        MathF.Min(Phase(start, fadeIn), 1f - Phase(end - fadeOut, fadeOut));

    private int SceneAt(float time)
    {
        for (var i = 0; i < _scenes.Length - 1; i++)
        {
            if (time < _scenes[i].End)
                return i;
        }

        return _scenes.Length - 1;
    }
}
