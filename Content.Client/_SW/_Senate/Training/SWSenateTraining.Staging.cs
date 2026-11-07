using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Staging: the 720×270 frame scaled so that its main part always fits, code-drawn conditional fragments and the text
/// plate. A scene whose background has not been delivered stays empty: nothing stands in for missing art.
/// </summary>
public sealed partial class SWSenateTraining
{
    private const float PlatePadding = 10f;
    private const float PlateGap = 4f;
    private const float PlateMaxWidth = 540f;
    private const float StackedMaxWidth = 720f;
    private const float StackGap = 8f;
    private const float WideMargin = 24f;
    private const float NarrowMargin = 8f;
    private const float NarrowWindow = 400f;
    private const float ZoneBottomGap = 12f;
    private const float MinPlateWidth = 240f;
    private const float MinCanvasHeight = 48f;
    private const float MinScrollRoom = 96f;
    private const float MinScrollPicture = 72f;
    private const float MaxScrollPictureShare = 0.4f;
    private const float AdvanceTime = 2f;
    private const float EdgeFade = 0.5f;
    private const float CrossFade = 0.8f;
    private const float MinPanelHeight = 200f;
    private const float PopupSeconds = 4f;
    private const float MinAnnotationWidth = 180f;
    private const float PointerTravel = 1.5f;
    private const float PointerUiSize = 24f;
    private const float PopupMaxWidth = 320f;

    private static readonly Color Backdrop = Color.FromHex("#0A0F16");
    private static readonly Color PlateFill = Backdrop.WithAlpha(0.92f);
    private static readonly Color WindowTint = Color.FromHex("#CFE3F2");

    private static Font? _titleFont;
    private static Font? _textFont;
    private static Font? _labelFont;

    private readonly Dictionary<SWTrainingArt, Texture> _art = new();
    private readonly Dictionary<(string Text, Font Font, float Scale, int Width), List<string>> _wrapCache = new();

    /// <summary>In the scroll modes PlateLeft and PlateAnchor give the left and top edges of the scroll area, in pixels.</summary>
    private readonly record struct PageLayout(TextMode Mode, float Pixel, UIBox2 Canvas, bool Stacked, float PlateLeft, float PlateWidth, float PlateAnchor);

    private (string Chapter, Vector2 Size, float Ui, float Reserve)? _layoutKey;
    private (string Chapter, Vector2 Size, float Ui)? _overflowFor;
    private PageLayout _layout;

    // Per-scene drawing state, in art pixels of the current scene.
    private float _pixel = 1f;
    private UIBox2 _canvas;
    private float _left;
    private float _top;
    private Vector2 _origin;
    private bool _canvasDrawn;

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>($"/Fonts/NotoSans/NotoSans-{variant}.ttf"), size);

    private void LoadStaging()
    {
        _titleFont ??= LoadFont("Bold", 16);
        _textFont ??= LoadFont("Regular", 13);
        _labelFont ??= LoadFont("Bold", 12);

        var cache = IoCManager.Resolve<IResourceCache>();
        var missing = 0;
        foreach (var file in SWSenateTrainingArt.Files)
        {
            // A file of the wrong size counts as missing rather than being drawn misaligned.
            if (cache.TryGetResource<TextureResource>(new ResPath(file.Path), out var resource) &&
                resource.Texture.Width == file.Width && resource.Texture.Height == file.Height)
                _art[file.Art] = resource.Texture;
            else
                missing++;
        }

        if (missing > 0)
        {
            IoCManager.Resolve<ILogManager>().GetSawmill("senate.training")
                .Warning($"Senate training: {missing} of {SWSenateTrainingArt.Files.Length} pictures are not available; their scenes stay empty.");
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        if (size.X < 1f || size.Y < 1f)
            return;

        handle.DrawRect(new UIBox2(Vector2.Zero, size), Backdrop);
        if (_chapter == null)
            return;

        var layout = GetLayout(handle, _chapter, size, UIScale);
        // The controller moves the text to the mode chosen here, outside the UI tree walk.
        _wantedMode = layout.Mode;
        var shot = _chapter.Shots[_shotIndex];
        _canvasDrawn = layout.Pixel > 0f;
        if (_canvasDrawn)
            DrawCanvas(handle, layout, shot, UIScale);

        if (layout.Mode == TextMode.Drawn && _textMode == TextMode.Drawn)
            DrawPlate(handle, layout, shot);
    }

    // ---- layout -----------------------------------------------------------

    /// <summary>Whole-number scale when possible; below 1 only to keep the frame's main part and full height visible.</summary>
    private static float FitPixel(float width, float room)
    {
        var fit = MathF.Min(room / SWSenateTrainingArt.FrameHeight, width / SWSenateTrainingArt.MinViewWidth);
        if (fit <= 0f)
            return 0f;

        return fit >= 1f ? MathF.Floor(fit) : fit;
    }

    private PageLayout GetLayout(DrawingHandleScreen handle, SWTrainingChapter chapter, Vector2 size, float ui)
    {
        var key = (chapter.Id, size, ui, _barReserve);
        if (_layoutKey == key)
            return _layout;

        _layoutKey = key;
        _layout = ComputeLayout(handle, chapter, size, ui);
        return _layout;
    }

    /// <summary>
    /// The layout holds for the whole chapter, sized by its longest shot, so the picture does not jump between shots.
    /// The plate covers the free corner of the frame when it fits there; otherwise it moves under the picture, which
    /// shrinks as a whole. The font never shrinks: a window that cannot hold the whole text gets a scroll area above
    /// the buttons and, when even that has no room, one scroll area with the text and the buttons together.
    /// </summary>
    private PageLayout ComputeLayout(DrawingHandleScreen handle, SWTrainingChapter chapter, Vector2 size, float ui)
    {
        var room = MathF.Max(1f, size.Y - _barReserve * ui);
        var margin = (size.X / ui < NarrowWindow ? NarrowMargin : WideMargin) * ui;
        var needsPanel = SWSenateTrainingScript.HasPanels(chapter);
        var overflowed = _overflowFor == (chapter.Id, size, ui);

        var pixel = FitPixel(size.X, room);
        if (pixel >= 1f && (!needsPanel || (!overflowed && PanelsFit(chapter, size.X, pixel, ui))))
        {
            var view = size.X / pixel;
            var zoneRight = (SWSenateTrainingArt.ZoneRight - (SWSenateTrainingArt.FrameWidth - view) / 2f) * pixel;
            var zoneHeight = (SWSenateTrainingArt.FrameHeight - SWSenateTrainingArt.ZoneTop) * pixel;
            var width = MathF.Min(PlateMaxWidth * ui, zoneRight - margin);
            if (width >= MinPlateWidth * ui && WorstPlateHeight(handle, chapter, ui, width) + ZoneBottomGap * ui <= zoneHeight)
            {
                var top = MathF.Floor((room - SWSenateTrainingArt.FrameHeight * pixel) / 2f);
                var canvas = new UIBox2(0f, top, size.X, top + SWSenateTrainingArt.FrameHeight * pixel);
                return new PageLayout(TextMode.Drawn, pixel, canvas, false, margin, width, canvas.Bottom - ZoneBottomGap * ui);
            }
        }

        var stackedWidth = MathF.Max(1f, MathF.Min(StackedMaxWidth * ui, size.X - margin * 2f));
        var plate = WorstPlateHeight(handle, chapter, ui, stackedWidth);
        var gap = StackGap * ui;
        var left = MathF.Floor((size.X - stackedWidth) / 2f);
        if (plate <= room)
        {
            var stackedPixel = FitPixel(size.X, room - plate - gap);
            var pictureHeight = SWSenateTrainingArt.FrameHeight * stackedPixel;
            if (pictureHeight < MinCanvasHeight * ui && !needsPanel)
                return new PageLayout(TextMode.Drawn, 0f, default, true, left, stackedWidth, MathF.Floor((room - plate) / 2f));

            // A chapter with illustrations stays here only if they fit the picture; otherwise they join the scroll area.
            if (pictureHeight >= MinCanvasHeight * ui && (!needsPanel || (!overflowed && PanelsFit(chapter, size.X, stackedPixel, ui))))
            {
                var stackedTop = MathF.Floor((room - pictureHeight - gap - plate) / 2f);
                var stackedCanvas = new UIBox2(0f, stackedTop, size.X, stackedTop + pictureHeight);
                return new PageLayout(TextMode.Drawn, stackedPixel, stackedCanvas, true, left, stackedWidth, stackedCanvas.Bottom + gap);
            }
        }

        // The panel keeps its place while the text can still get a usable scroll area above it.
        var edge = CompactEdge * ui;
        if (room < MinScrollRoom * ui)
            return new PageLayout(TextMode.Compact, 0f, default, true, edge, size.X - edge * 2f, edge);

        // The text comes first here; the picture only takes what the text can spare.
        var scrollPixel = FitPixel(size.X, MathF.Min(room * MaxScrollPictureShare, room - MinScrollRoom * ui - gap));
        var scrollPicture = SWSenateTrainingArt.FrameHeight * scrollPixel;
        if (scrollPicture < MinScrollPicture * ui)
            return new PageLayout(TextMode.Scroll, 0f, default, true, margin, size.X - margin * 2f, edge);

        var scrollCanvas = new UIBox2(0f, 0f, size.X, scrollPicture);
        return new PageLayout(TextMode.Scroll, scrollPixel, scrollCanvas, true, margin, size.X - margin * 2f, scrollCanvas.Bottom + gap);
    }

    /// <summary>The illustration's part of the frame on screen, cropped to what a narrow window shows.</summary>
    private static UIBox2 PanelBoxOnScreen(SWTrainingScene scene, UIBox2 canvas, float pixel)
    {
        var box = SWSenateTrainingArt.PanelBox(scene);
        var view = canvas.Width / pixel;
        var frameLeft = canvas.Left - (SWSenateTrainingArt.FrameWidth - view) / 2f * pixel;
        return new UIBox2(
            MathF.Max(frameLeft + box.Left * pixel, canvas.Left),
            canvas.Top + box.Top * pixel,
            MathF.Min(frameLeft + box.Right * pixel, canvas.Right),
            canvas.Top + box.Bottom * pixel);
    }

    private static bool PanelsFit(SWTrainingChapter chapter, float width, float pixel, float ui)
    {
        var canvas = new UIBox2(0f, 0f, width, SWSenateTrainingArt.FrameHeight * pixel);
        foreach (var shot in chapter.Shots)
        {
            if (shot.Stages == null)
                continue;

            foreach (var stage in shot.Stages)
            {
                if (stage.Panel == null)
                    continue;

                var box = PanelBoxOnScreen(stage.Visual.Scene, canvas, pixel);
                if (box.Width < SWTrainingUiPanels.MinWidth(stage.Panel) * ui || box.Height < MinPanelHeight * ui)
                    return false;
            }
        }

        return true;
    }

    // ---- text -------------------------------------------------------------

    private float WorstPlateHeight(DrawingHandleScreen handle, SWTrainingChapter chapter, float scale, float width)
    {
        var worst = 0f;
        foreach (var shot in chapter.Shots)
            worst = MathF.Max(worst, PlateHeight(handle, shot, scale, width));
        return worst;
    }

    private float PlateHeight(DrawingHandleScreen handle, SWTrainingShot shot, float scale, float width)
    {
        var inner = MathF.Max(1f, width - PlatePadding * 2f * scale);
        var title = Wrap(handle, _titleFont!, scale, shot.Title, inner).Count * _titleFont!.GetLineHeight(scale);
        var text = Wrap(handle, _textFont!, scale, shot.Text, inner).Count * _textFont!.GetLineHeight(scale);
        // The optional line takes no room when a shot has nothing to add.
        var note = shot.Note.Length == 0
            ? 0f
            : PlateGap * scale + Wrap(handle, _textFont!, scale, shot.Note, inner).Count * _textFont!.GetLineHeight(scale);
        return PlatePadding * 2f * scale + title + PlateGap * scale + text + note;
    }

    /// <summary>Plain plate: all lines in full, no ellipsis, no accent bar and no markers.</summary>
    private void DrawPlate(DrawingHandleScreen handle, PageLayout layout, SWTrainingShot shot)
    {
        var scale = UIScale;
        var height = PlateHeight(handle, shot, scale, layout.PlateWidth);
        var top = layout.Stacked ? layout.PlateAnchor : layout.PlateAnchor - height;
        var plate = UIBox2.FromDimensions(new Vector2(layout.PlateLeft, top), new Vector2(layout.PlateWidth, height));
        handle.DrawRect(plate, PlateFill);

        var padding = PlatePadding * scale;
        var inner = MathF.Max(1f, layout.PlateWidth - padding * 2f);
        var x = plate.Left + padding;
        var y = plate.Top + padding;
        y = DrawLines(handle, _titleFont!, scale, shot.Title, x, y, inner, SWConsoleStyle.TextBright) + PlateGap * scale;
        y = DrawLines(handle, _textFont!, scale, shot.Text, x, y, inner, SWConsoleStyle.TextBright) + PlateGap * scale;
        DrawLines(handle, _textFont!, scale, shot.Note, x, y, inner, SWConsoleStyle.TextDim);
    }

    private float DrawLines(DrawingHandleScreen handle, Font font, float scale, string text, float x, float y, float width, Color color)
    {
        var lineHeight = font.GetLineHeight(scale);
        foreach (var line in Wrap(handle, font, scale, text, width))
        {
            handle.DrawString(font, new Vector2(x, y), line, scale, color);
            y += lineHeight;
        }

        return y;
    }

    private List<string> Wrap(DrawingHandleScreen handle, Font font, float scale, string text, float width)
    {
        var key = (text, font, scale, (int) width);
        if (_wrapCache.TryGetValue(key, out var cached))
            return cached;

        if (_wrapCache.Count > 1024)
            _wrapCache.Clear();

        var lines = new List<string>();
        var current = string.Empty;
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw;
            // A word wider than the line is split, so it can never run past the plate.
            while (word.Length > 1 && handle.GetDimensions(font, word, scale).X > width)
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                var take = word.Length - 1;
                while (take > 1 && handle.GetDimensions(font, word[..take] + "-", scale).X > width)
                    take--;
                lines.Add(word[..take] + "-");
                word = word[take..];
            }

            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && handle.GetDimensions(font, candidate, scale).X > width)
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

        _wrapCache[key] = lines;
        return lines;
    }

    // ---- canvas -----------------------------------------------------------

    private void DrawCanvas(DrawingHandleScreen handle, PageLayout layout, SWTrainingShot shot, float ui)
    {
        _pixel = layout.Pixel;
        _canvas = layout.Canvas;
        var stage = CurrentStage;
        var visual = stage?.Visual ?? shot.Visual;
        var shotTime = _shotTime;
        var duration = SWSenateTrainingScript.ShotDuration(_chapter!, shot, _reducedMotion);

        var delivered = BeginScene(visual.Scene);
        if (visual.Then is not { } then)
        {
            if (delivered)
            {
                // A stage starts its pictures' own time at its beginning.
                var localTime = stage != null ? shotTime - StageStart(stage) : shotTime;
                DrawDetails(handle, visual, localTime, ui, stage != null);
            }
        }
        else
        {
            // One set-up is replaced by the next halfway through the shot: a soft fade, or a cut under reduced motion.
            var halfway = duration / 2f;
            var alpha = _reducedMotion
                ? (shotTime >= halfway ? 1f : 0f)
                : Math.Clamp((shotTime - (halfway - CrossFade / 2f)) / CrossFade, 0f, 1f);
            if (alpha < 1f && delivered)
                DrawLayers(handle, visual.Scene, 1f);
            if (alpha > 0f && BeginScene(then))
                DrawLayers(handle, then, alpha);
        }

        if (_reducedMotion)
            return;

        var fadeIn = _shotIndex == 0 ? 1f - Math.Clamp(shotTime / EdgeFade, 0f, 1f) : 0f;
        var fadeOut = _shotIndex == _chapter!.Shots.Length - 1 ? Math.Clamp((shotTime - (duration - EdgeFade)) / EdgeFade, 0f, 1f) : 0f;
        var fade = MathF.Max(fadeIn, fadeOut);
        if (fade > 0f)
            handle.DrawRect(_canvas, Backdrop.WithAlpha(fade));
    }

    /// <summary>Sets the camera for the scene; false when its background has not been delivered.</summary>
    private bool BeginScene(SWTrainingScene scene)
    {
        var def = SWSenateTrainingArt.Scene(scene);
        var file = SWSenateTrainingArt.File(def.Layers[0]);
        var view = _canvas.Width / _pixel;
        var halfW = view / 2f;
        var halfH = SWSenateTrainingArt.FrameHeight / 2f;
        var x = file.Width <= view ? file.Width / 2f : Math.Clamp(def.CameraX, halfW, file.Width - halfW);
        var y = file.Height <= SWSenateTrainingArt.FrameHeight ? file.Height / 2f : Math.Clamp(def.CameraY, halfH, file.Height - halfH);
        _left = MathF.Round((x - halfW) * _pixel) / _pixel;
        _top = MathF.Round((y - halfH) * _pixel) / _pixel;
        _origin = new Vector2(x - SWSenateTrainingArt.FrameWidth / 2f, y - halfH);
        return _art.ContainsKey(def.Layers[0]);
    }

    private void DrawLayers(DrawingHandleScreen handle, SWTrainingScene scene, float alpha)
    {
        foreach (var art in SWSenateTrainingArt.Scene(scene).Layers)
        {
            if (!_art.TryGetValue(art, out var texture))
                continue;

            var file = SWSenateTrainingArt.File(art);
            var box = new UIBox2(0f, 0f, file.Width, file.Height);
            var tint = art == SWTrainingArt.DeskWindows ? WindowTint : Color.White;
            Blit(handle, texture, box, box, tint.WithAlpha(alpha));
        }
    }

    private void DrawDetails(DrawingHandleScreen handle, SWTrainingVisual visual, float shotTime, float ui, bool staged)
    {
        DrawLayers(handle, visual.Scene, 1f);

        // The fragment fills the screen, so pictures that sit on the screen go over it.
        if (visual.Schema != SWTrainingSchema.None)
            DrawSchema(handle, visual, ui);

        foreach (var overlay in visual.Overlays)
        {
            // In a staged shot the clock goes above the illustration, in DrawTop.
            if (!staged || overlay.Art != SWTrainingArt.MarkClock)
                DrawOverlay(handle, overlay, shotTime);
        }

        DrawFocus(handle, visual.Focus);
        if (visual.ClockText != null && !staged)
            DrawClockText(handle, visual.ClockText, ui);
    }

    // ---- stage layer ------------------------------------------------------

    /// <summary>Called by the top layer after the illustration; it reuses the camera the canvas set this frame.</summary>
    private void DrawTop(DrawingHandleScreen handle)
    {
        if (_chapter == null || CurrentStage is not { } stage)
            return;

        var ui = UIScale;
        var visual = stage.Visual;
        // In the scroll modes the clock and the notes are part of the text instead of covering it.
        if (_textMode == TextMode.Drawn && _canvasDrawn && _art.ContainsKey(SWSenateTrainingArt.Scene(visual.Scene).Layers[0]))
        {
            foreach (var overlay in visual.Overlays)
            {
                if (overlay.Art == SWTrainingArt.MarkClock)
                    DrawOverlay(handle, overlay, 0f);
            }

            if (visual.ClockText != null)
                DrawClockText(handle, visual.ClockText, ui);

            if (stage.Annotations != null)
            {
                foreach (var annotation in stage.Annotations)
                    DrawAnnotation(handle, annotation, ui);
            }
        }

        if (!_panelArea.Visible)
            return;

        // A state without a target, such as an approved budget, still shows its popup at the illustration.
        Control anchor = _panelTarget ?? _panelArea;
        // Controls built this frame have no place yet; the pointer and the popup appear with the next one.
        if (anchor.PixelSize.X <= 0)
            return;

        var position = (Vector2) (anchor.GlobalPixelPosition - GlobalPixelPosition);
        var target = UIBox2.FromDimensions(position, (Vector2) anchor.PixelSize);
        if (_panelTarget != null)
        {
            // Without movement the outline tells the aim from the press: dim, then bright.
            if (stage.Pointer == SWTrainingPointer.Press)
                handle.DrawRect(target, SWConsoleStyle.Accent, false);
            else if (stage.Pointer == SWTrainingPointer.Travel && _reducedMotion)
                handle.DrawRect(target, SWConsoleStyle.TextDim, false);

            DrawPointer(handle, stage, target, ui);
        }

        // A popup is brief in the game too; its stage stays on screen and the scroll area keeps its words.
        if (stage.Popup != null && _shotTime - StageStart(stage) < PopupSeconds)
            DrawPopup(handle, stage.Popup, target, ui);
    }

    private void DrawPointer(DrawingHandleScreen handle, SWTrainingStage stage, UIBox2 target, float ui)
    {
        if (stage.Pointer == SWTrainingPointer.None || !_art.TryGetValue(SWTrainingArt.UiPointer, out var texture))
            return;

        var size = PointerUiSize * ui;
        var position = target.Center;
        // Under reduced motion the pointer stands still on the control.
        if (stage.Pointer == SWTrainingPointer.Travel && !_reducedMotion)
        {
            var t = Math.Clamp((_shotTime - StageStart(stage)) / PointerTravel, 0f, 1f);
            t = t * t * (3f - 2f * t);
            position += new Vector2(90f, 60f) * ui * (1f - t);
        }

        var frame = stage.Pointer == SWTrainingPointer.Press ? 1 : 0;
        var region = new UIBox2(PointerUiSize * frame, 0f, PointerUiSize * (frame + 1), PointerUiSize);
        handle.DrawTextureRectRegion(texture, UIBox2.FromDimensions(position, new Vector2(size, size)), region, Color.White);
    }

    /// <summary>A popup, as the game shows it at the cursor: brief, apart from the window, and over it.</summary>
    private void DrawPopup(DrawingHandleScreen handle, string text, UIBox2 target, float ui)
    {
        var padding = 6f * ui;
        var width = PopupMaxWidth * ui;
        var lines = Wrap(handle, _labelFont!, ui, text, width - padding * 2f);
        var height = lines.Count * _labelFont!.GetLineHeight(ui) + padding * 2f;
        var size = (Vector2) PixelSize;
        var left = Math.Clamp(target.Center.X - width / 2f, 0f, MathF.Max(0f, size.X - width));
        var top = MathF.Max(0f, target.Top - height - 4f * ui);
        var box = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(width, height));
        handle.DrawRect(box, PlateFill);
        handle.DrawRect(box, SWConsoleStyle.Brass, false);
        DrawLines(handle, _labelFont!, ui, text, box.Left + padding, box.Top + padding, width - padding * 2f, SWConsoleStyle.TextBright);
    }

    private void DrawAnnotation(DrawingHandleScreen handle, SWTrainingAnnotation annotation, float ui)
    {
        var padding = 6f * ui;
        var width = MathF.Max(annotation.Width * _pixel, MinAnnotationWidth * ui);
        var inner = MathF.Max(1f, width - padding * 2f);
        var color = annotation.Tone switch
        {
            SWTrainingTone.Teaching => SWConsoleStyle.Brass,
            SWTrainingTone.GameWarning => SWConsoleStyle.Warn,
            _ => SWConsoleStyle.TextBright,
        };

        var heading = annotation.Heading == null ? 0f : Wrap(handle, _labelFont!, ui, annotation.Heading, inner).Count * _labelFont!.GetLineHeight(ui);
        var height = heading + Wrap(handle, _textFont!, ui, annotation.Text, inner).Count * _textFont!.GetLineHeight(ui) + padding * 2f;
        var anchor = ToScreen(_origin + new Vector2(annotation.X, annotation.Y));
        // The requested right edge is kept where a wider minimum applies, so a note never grows over a face.
        var right = anchor.X + annotation.Width * _pixel;
        var left = MathF.Max(_canvas.Left, right - width);
        var box = UIBox2.FromDimensions(new Vector2(left, anchor.Y), new Vector2(width, height));
        handle.DrawRect(box, PlateFill);
        var y = box.Top + padding;
        if (annotation.Heading != null)
            y = DrawLines(handle, _labelFont!, ui, annotation.Heading, box.Left + padding, y, inner, SWConsoleStyle.TextBright);
        DrawLines(handle, _textFont!, ui, annotation.Text, box.Left + padding, y, inner, color);
    }

    private void DrawOverlay(DrawingHandleScreen handle, SWTrainingOverlay overlay, float shotTime)
    {
        if (!_art.TryGetValue(overlay.Art, out var texture))
            return;

        var file = SWSenateTrainingArt.File(overlay.Art);
        var frame = overlay.Frame;
        // Under reduced motion only the finished state of an action is shown.
        if (overlay.Advance)
            frame = _reducedMotion || shotTime >= AdvanceTime ? 1 : 0;
        else if (overlay.Sequence > 0f && !_reducedMotion)
        {
            var step = (int) (shotTime / overlay.Sequence * file.Frames);
            frame = overlay.Reverse ? file.Frames - 1 - step : step;
        }

        frame = Math.Clamp(frame, 0, file.Frames - 1);

        var frameWidth = (float) file.Width / file.Frames;
        var region = new UIBox2(frameWidth * frame, 0f, frameWidth * (frame + 1), file.Height);
        var dest = UIBox2.FromDimensions(_origin + new Vector2(overlay.X, overlay.Y), new Vector2(frameWidth, file.Height));
        Blit(handle, texture, dest, region, Color.White);
    }

    private Vector2 ToScreen(Vector2 art) =>
        new(_canvas.Left + (art.X - _left) * _pixel, _canvas.Top + (art.Y - _top) * _pixel);

    private UIBox2 ToScreen(UIBox2 art) => new(ToScreen(art.TopLeft), ToScreen(art.BottomRight));

    /// <summary>Draws a texture region to art coordinates, cropped to the canvas.</summary>
    private void Blit(DrawingHandleScreen handle, Texture texture, UIBox2 artDest, UIBox2 region, Color color)
    {
        var dest = ToScreen(artDest);
        var visible = new UIBox2(MathF.Max(dest.Left, _canvas.Left), MathF.Max(dest.Top, _canvas.Top),
            MathF.Min(dest.Right, _canvas.Right), MathF.Min(dest.Bottom, _canvas.Bottom));
        if (visible.Width <= 0f || visible.Height <= 0f)
            return;

        var cropped = new UIBox2(
            region.Left + (visible.Left - dest.Left) / _pixel, region.Top + (visible.Top - dest.Top) / _pixel,
            region.Right - (dest.Right - visible.Right) / _pixel, region.Bottom - (dest.Bottom - visible.Bottom) / _pixel);
        handle.DrawTextureRectRegion(texture, visible, cropped, color);
    }

    // ---- code-drawn fragments ---------------------------------------------

    private UIBox2 FrameBox(float x0, float y0, float x1, float y1) =>
        ToScreen(new UIBox2(_origin.X + x0, _origin.Y + y0, _origin.X + x1, _origin.Y + y1));

    private void Fill(DrawingHandleScreen handle, UIBox2 box, Color color)
    {
        var rect = new UIBox2(MathF.Max(box.Left, _canvas.Left), MathF.Max(box.Top, _canvas.Top),
            MathF.Min(box.Right, _canvas.Right), MathF.Min(box.Bottom, _canvas.Bottom));
        if (rect.Width > 0f && rect.Height > 0f)
            handle.DrawRect(rect, color);
    }

    private void Outline(DrawingHandleScreen handle, UIBox2 box, Color color)
    {
        var edge = MathF.Max(1f, MathF.Floor(_pixel));
        Fill(handle, new UIBox2(box.Left, box.Top, box.Right, box.Top + edge), color);
        Fill(handle, new UIBox2(box.Left, box.Bottom - edge, box.Right, box.Bottom), color);
        Fill(handle, new UIBox2(box.Left, box.Top, box.Left + edge, box.Bottom), color);
        Fill(handle, new UIBox2(box.Right - edge, box.Top, box.Right, box.Bottom), color);
    }

    private void DrawSchema(DrawingHandleScreen handle, SWTrainingVisual visual, float ui)
    {
        // On the console the fragment fills its screen; on the desk it keeps clear of the desk itself.
        var onDesk = visual.Scene == SWTrainingScene.Desk;
        var x0 = onDesk ? 340f : 162f;
        var y0 = onDesk ? 40f : 32f;
        var x1 = onDesk ? 600f : 558f;
        var y1 = onDesk ? 120f : 160f;
        Fill(handle, FrameBox(x0, y0, x1, y1), SWConsoleStyle.BgDark);

        switch (visual.Schema)
        {
            case SWTrainingSchema.List:
                DrawRows(handle, visual, x0 + 6f, y0 + 6f, x1 - 6f, 4, 26f, 5f, ui);
                break;
            case SWTrainingSchema.Log:
                DrawRows(handle, visual, x0 + 6f, y0 + 6f, x1 - 6f, 5, 18f, 6f, ui);
                break;
            case SWTrainingSchema.Columns2:
                DrawCells(handle, visual, x0 + 6f, y0 + 6f, x1 - 6f, y1 - 6f, 2, ui);
                break;
            case SWTrainingSchema.Columns3:
                DrawCells(handle, visual, x0 + 6f, y0 + 6f, x1 - 6f, y1 - 6f, 3, ui);
                break;
            case SWTrainingSchema.Steps3:
                DrawCells(handle, visual, x0 + 6f, (y0 + y1) / 2f - 22f, x1 - 6f, (y0 + y1) / 2f + 22f, 3, ui);
                break;
            case SWTrainingSchema.Steps4:
                DrawCells(handle, visual, x0 + 6f, (y0 + y1) / 2f - 22f, x1 - 6f, (y0 + y1) / 2f + 22f, 4, ui);
                break;
            case SWTrainingSchema.Meter:
                var middle = (y0 + y1) / 2f;
                Fill(handle, FrameBox(x0 + 12f, middle - 10f, x1 - 12f, middle + 10f), SWConsoleStyle.PillDim);
                Fill(handle, FrameBox(x0 + 12f, middle - 10f, x0 + 12f + (x1 - x0 - 24f) * 0.55f, middle + 10f), SWConsoleStyle.Accent);
                break;
        }
    }

    private void DrawRows(DrawingHandleScreen handle, SWTrainingVisual visual, float x0, float y0, float x1, int count, float height, float gap, float ui)
    {
        for (var i = 0; i < count; i++)
        {
            var top = y0 + i * (height + gap);
            DrawCell(handle, visual, FrameBox(x0, top, x1, top + height), i, ui);
        }
    }

    private void DrawCells(DrawingHandleScreen handle, SWTrainingVisual visual, float x0, float y0, float x1, float y1, int count, float ui)
    {
        const float gap = 6f;
        var width = (x1 - x0 - gap * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var left = x0 + i * (width + gap);
            DrawCell(handle, visual, FrameBox(left, y0, left + width, y1), i, ui);
        }
    }

    private void DrawCell(DrawingHandleScreen handle, SWTrainingVisual visual, UIBox2 box, int index, float ui)
    {
        var active = index == visual.Highlight;
        Fill(handle, box, active ? SWConsoleStyle.Selected : SWConsoleStyle.PillDim);
        if (active)
            Outline(handle, box, SWConsoleStyle.Accent);

        if (visual.Labels == null || index >= visual.Labels.Length)
            return;

        // Only names confirmed in the game's interface reach this point. They shrink to fit and are never cut.
        var label = visual.Labels[index];
        var room = MathF.Max(1f, box.Width - 8f * ui);
        var width = handle.GetDimensions(_labelFont!, label, ui).X;
        var scale = width > room ? ui * room / width : ui;
        var position = new Vector2(box.Left + 4f * ui, box.Center.Y - _labelFont!.GetLineHeight(scale) / 2f);
        handle.DrawString(_labelFont!, position, label, scale, SWConsoleStyle.TextBright);
    }

    private void DrawFocus(DrawingHandleScreen handle, SWTrainingFocus focus)
    {
        switch (focus)
        {
            case SWTrainingFocus.Judges:
                Outline(handle, FrameBox(240f, 47f, 480f, 137f), SWConsoleStyle.Brass);
                break;
            case SWTrainingFocus.Prosecutor:
                Outline(handle, FrameBox(120f, 108f, 210f, 228f), SWConsoleStyle.Brass);
                break;
            case SWTrainingFocus.Defender:
                Outline(handle, FrameBox(520f, 110f, 600f, 230f), SWConsoleStyle.Brass);
                break;
        }
    }

    /// <summary>The exact time named by the script, under the clock mark.</summary>
    private void DrawClockText(DrawingHandleScreen handle, string text, float ui)
    {
        var anchor = ToScreen(_origin + new Vector2(546f, 62f));
        var size = handle.GetDimensions(_titleFont!, text, ui);
        var padding = 4f * ui;
        var box = UIBox2.FromDimensions(anchor - new Vector2(size.X / 2f + padding, 0f), new Vector2(size.X + padding * 2f, size.Y + padding * 2f));
        Fill(handle, box, PlateFill);
        handle.DrawString(_titleFont!, box.TopLeft + new Vector2(padding, padding), text, ui, SWConsoleStyle.TextBright);
    }
}
