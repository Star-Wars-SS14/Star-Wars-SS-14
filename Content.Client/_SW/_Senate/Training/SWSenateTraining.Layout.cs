using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Training;

/// <summary>Where the shot's text is. In the last two modes the window cannot show it whole, so it scrolls.</summary>
public enum SWTrainingTextMode
{
    /// <summary>Drawn on a plate; the shots change by themselves.</summary>
    Drawn,
    /// <summary>In a scroll area above the button panel; the shots change by hand.</summary>
    Scroll,
    /// <summary>Too low even for that: text and buttons share one scroll area; the shots change by hand.</summary>
    Compact,
}

/// <summary>
/// The page the window lays out for a chapter. In the scroll modes PlateLeft and PlateAnchor give the left and top edges
/// of the scroll area, in pixels.
/// </summary>
public readonly record struct SWTrainingPage(
    SWTrainingTextMode Mode,
    float Pixel,
    UIBox2 Canvas,
    bool Stacked,
    float PlateLeft,
    float PlateWidth,
    float PlateAnchor)
{
    public bool PictureShown => Pixel > 0f;

    /// <summary>The stage's remarks, its game time, its window and its popup are drawn on the picture.</summary>
    public bool StageOnPicture => Mode == SWTrainingTextMode.Drawn && PictureShown;

    /// <summary>The stage's words join the scroll area, with the shot's text.</summary>
    public bool StageInScroll => Mode != SWTrainingTextMode.Drawn;

    /// <summary>The shots change only by the buttons; pause has nothing to stop.</summary>
    public bool ByHand => Mode != SWTrainingTextMode.Drawn;
}

/// <summary>
/// Sizes of the text drawn on the picture, in screen pixels: the full box of a remark at a given box width (padding,
/// heading and wrapped text included), and the full box of a game time. The window measures with its real fonts.
/// </summary>
public sealed record SWTrainingMeasure(Func<SWTrainingAnnotation, float, float> NoteHeight, Func<string, Vector2> ClockSize);

/// <summary>How a line of the scroll area is coloured.</summary>
public enum SWTrainingScrollTone
{
    Plain,
    Teaching,
    Popup,
    Action,
}

/// <summary>
/// The page layout, apart from the drawing so that the contract can check it. A chapter keeps one layout for all its
/// shots, sized by its longest one, so the picture does not jump between shots.
/// </summary>
public static class SWTrainingPageLayout
{
    public const float PlateMaxWidth = 540f;
    public const float StackedMaxWidth = 720f;
    public const float StackGap = 8f;
    public const float WideMargin = 24f;
    public const float NarrowMargin = 8f;
    public const float NarrowWindow = 400f;
    public const float ZoneBottomGap = 12f;
    public const float MinPlateWidth = 240f;
    public const float MinCanvasHeight = 48f;
    public const float MinScrollRoom = 96f;
    public const float MinScrollPicture = 72f;
    public const float MaxScrollPictureShare = 0.4f;
    public const float MinPanelHeight = 200f;
    public const float CompactEdge = 4f;

    /// <summary>A remark is never narrower than this, in UI units, so its words stay readable on a small picture.</summary>
    public const float MinNoteWidth = 180f;
    public const float NotePadding = 6f;
    public const float ClockPadding = 4f;
    /// <summary>Space kept between two boxes on the picture when one has to move below the other.</summary>
    public const float NoteGap = 4f;

    /// <summary>Where the game time stands in the frame: under the clock mark.</summary>
    public const float ClockX = 546f;
    public const float ClockY = 62f;

    /// <summary>True when a chapter has stages: their words and popups live on the picture or in the scroll area.</summary>
    public static bool HasStages(SWTrainingChapter chapter)
    {
        foreach (var shot in chapter.Shots)
        {
            if (shot.Stages is { Length: > 0 })
                return true;
        }

        return false;
    }

    /// <summary>Whole-number scale when possible; below 1 only to keep the frame's main part and full height visible.</summary>
    public static float FitPixel(float width, float room)
    {
        var fit = MathF.Min(room / SWSenateTrainingArt.FrameHeight, width / SWSenateTrainingArt.MinViewWidth);
        if (fit <= 0f)
            return 0f;

        return fit >= 1f ? MathF.Floor(fit) : fit;
    }

    /// <summary>
    /// The plate covers the free corner of the frame when it fits there; otherwise it moves under the picture, which
    /// shrinks as a whole. The font never shrinks: a window that cannot hold the whole text gets a scroll area above the
    /// buttons and, when even that has no room, one scroll area with the text and the buttons together.
    /// A plate alone, without a picture, is only for chapters without stages: a stage's remarks, game time and popup are
    /// drawn on the picture, so with no room for it they go to the scroll area with the text.
    /// </summary>
    /// <param name="worstPlate">The tallest plate of the chapter at a given width, in pixels.</param>
    /// <param name="measure">Sizes of the remarks and game times; a drawn page is chosen only if all of them fit.</param>
    public static SWTrainingPage Compute(SWTrainingChapter chapter, Vector2 size, float ui, float barReserve, bool overflowed,
        Func<float, float> worstPlate, SWTrainingMeasure measure)
    {
        var room = MathF.Max(1f, size.Y - barReserve * ui);
        var margin = (size.X / ui < NarrowWindow ? NarrowMargin : WideMargin) * ui;
        var needsPanel = SWSenateTrainingScript.HasPanels(chapter);
        var needsPicture = HasStages(chapter);

        var pixel = FitPixel(size.X, room);
        if (pixel >= 1f && (!needsPanel || (!overflowed && PanelsFit(chapter, size.X, pixel, ui))))
        {
            var view = size.X / pixel;
            var zoneRight = (SWSenateTrainingArt.ZoneRight - (SWSenateTrainingArt.FrameWidth - view) / 2f) * pixel;
            var zoneHeight = (SWSenateTrainingArt.FrameHeight - SWSenateTrainingArt.ZoneTop) * pixel;
            var width = MathF.Min(PlateMaxWidth * ui, zoneRight - margin);
            if (width >= MinPlateWidth * ui && worstPlate(width) + ZoneBottomGap * ui <= zoneHeight)
            {
                var top = MathF.Floor((room - SWSenateTrainingArt.FrameHeight * pixel) / 2f);
                var canvas = new UIBox2(0f, top, size.X, top + SWSenateTrainingArt.FrameHeight * pixel);
                var anchor = canvas.Bottom - ZoneBottomGap * ui;
                var plateHeight = worstPlate(width);
                // The plate covers the corner of the picture here, so the remarks must keep clear of it too.
                var plateBox = new UIBox2(margin, anchor - plateHeight, margin + width, anchor);
                if (NotesFit(chapter, canvas, pixel, ui, measure, plateBox))
                    return new SWTrainingPage(SWTrainingTextMode.Drawn, pixel, canvas, false, margin, width, anchor);
            }
        }

        var stackedWidth = MathF.Max(1f, MathF.Min(StackedMaxWidth * ui, size.X - margin * 2f));
        var plate = worstPlate(stackedWidth);
        var gap = StackGap * ui;
        var left = MathF.Floor((size.X - stackedWidth) / 2f);
        if (plate <= room)
        {
            var stackedPixel = FitPixel(size.X, room - plate - gap);
            var pictureHeight = SWSenateTrainingArt.FrameHeight * stackedPixel;
            if (pictureHeight < MinCanvasHeight * ui && !needsPanel && !needsPicture)
                return new SWTrainingPage(SWTrainingTextMode.Drawn, 0f, default, true, left, stackedWidth, MathF.Floor((room - plate) / 2f));

            // A chapter with illustrations stays here only if they fit the picture; otherwise they join the scroll area.
            if (pictureHeight >= MinCanvasHeight * ui && (!needsPanel || (!overflowed && PanelsFit(chapter, size.X, stackedPixel, ui))))
            {
                var stackedTop = MathF.Floor((room - pictureHeight - gap - plate) / 2f);
                var stackedCanvas = new UIBox2(0f, stackedTop, size.X, stackedTop + pictureHeight);
                // The plate is under the picture here; the remarks have to fit inside the small picture itself.
                if (NotesFit(chapter, stackedCanvas, stackedPixel, ui, measure, null))
                    return new SWTrainingPage(SWTrainingTextMode.Drawn, stackedPixel, stackedCanvas, true, left, stackedWidth, stackedCanvas.Bottom + gap);
            }
        }

        // The panel keeps its place while the text can still get a usable scroll area above it.
        var edge = CompactEdge * ui;
        if (room < MinScrollRoom * ui)
            return new SWTrainingPage(SWTrainingTextMode.Compact, 0f, default, true, edge, size.X - edge * 2f, edge);

        // The text comes first here; the picture only takes what the text can spare.
        var scrollPixel = FitPixel(size.X, MathF.Min(room * MaxScrollPictureShare, room - MinScrollRoom * ui - gap));
        var scrollPicture = SWSenateTrainingArt.FrameHeight * scrollPixel;
        if (scrollPicture < MinScrollPicture * ui)
            return new SWTrainingPage(SWTrainingTextMode.Scroll, 0f, default, true, margin, size.X - margin * 2f, edge);

        var scrollCanvas = new UIBox2(0f, 0f, size.X, scrollPicture);
        return new SWTrainingPage(SWTrainingTextMode.Scroll, scrollPixel, scrollCanvas, true, margin, size.X - margin * 2f, scrollCanvas.Bottom + gap);
    }

    /// <summary>The illustration's part of the frame on screen, cropped to what a narrow window shows.</summary>
    public static UIBox2 PanelBoxOnScreen(SWTrainingScene scene, UIBox2 canvas, float pixel)
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

    /// <summary>
    /// Where the window really puts a stage's illustration: its part of the frame, narrowed to the illustration's widest
    /// size and centred in it, over the part's full height. SyncPanelArea places the control exactly here.
    /// </summary>
    public static UIBox2 PanelRectOnScreen(SWTrainingScene scene, SWTrainingPanelState panel, UIBox2 canvas, float pixel, float ui)
    {
        var box = PanelBoxOnScreen(scene, canvas, pixel);
        var width = MathF.Min(SWTrainingUiPanels.MaxWidth(panel) * ui, box.Width);
        var left = box.Left + (box.Width - width) / 2f;
        return new UIBox2(left, box.Top, left + width, box.Bottom);
    }

    /// <summary>The clock mark of a stage as DrawTop draws it, over the picture: its sprite at its frame position.</summary>
    public static List<UIBox2> ClockMarks(SWTrainingStage stage, UIBox2 canvas, float pixel)
    {
        var cut = (SWSenateTrainingArt.FrameWidth - canvas.Width / pixel) / 2f;
        var marks = new List<UIBox2>();
        foreach (var overlay in stage.Visual.Overlays)
        {
            if (overlay.Art != SWTrainingArt.MarkClock)
                continue;

            var file = SWSenateTrainingArt.File(overlay.Art);
            var left = canvas.Left + (overlay.X - cut) * pixel;
            var top = canvas.Top + overlay.Y * pixel;
            marks.Add(new UIBox2(left, top, left + (float) file.Width / file.Frames * pixel, top + file.Height * pixel));
        }

        return marks;
    }

    public static bool PanelsFit(SWTrainingChapter chapter, float width, float pixel, float ui)
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

    /// <summary>Screen pixels kept free around a game window, against rounding when the control is laid out.</summary>
    public const float PanelClearance = 1f;

    public static UIBox2 Inflate(UIBox2 box, float by) => new(box.Left - by, box.Top - by, box.Right + by, box.Bottom + by);

    public static bool Overlap(UIBox2 a, UIBox2 b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>
    /// The boxes the picture shows for a stage, in screen pixels and in drawing order: the game time first, then each
    /// remark. A remark keeps its requested right edge where it can, but never leaves the picture sideways, and it moves
    /// down below any earlier box it would cover. The window draws exactly these boxes.
    /// </summary>
    public static List<UIBox2> StageBoxes(SWTrainingStage[] stages, int index, UIBox2 canvas, float pixel, float ui, SWTrainingMeasure measure)
    {
        var stage = stages[index];
        // Art pixels cut off on the left of a narrow window; the frame's centre is the view's centre.
        var cut = (SWSenateTrainingArt.FrameWidth - canvas.Width / pixel) / 2f;
        float X(float art) => canvas.Left + (art - cut) * pixel;
        float Y(float art) => canvas.Top + art * pixel;

        var boxes = new List<UIBox2>();
        if (stage.Visual.ClockText is { } clock)
        {
            var size = measure.ClockSize(clock);
            var left = Math.Clamp(X(ClockX) - size.X / 2f, canvas.Left, MathF.Max(canvas.Left, canvas.Right - size.X));
            boxes.Add(UIBox2.FromDimensions(new Vector2(left, Y(ClockY)), size));
        }

        foreach (var note in SWSenateTrainingScript.VisibleAnnotations(stages, index))
        {
            var width = MathF.Max(note.Width * pixel, MinNoteWidth * ui);
            var right = X(note.X) + note.Width * pixel;
            var left = MathF.Max(canvas.Left, MathF.Min(right, canvas.Right) - width);
            var height = measure.NoteHeight(note, width);
            var box = UIBox2.FromDimensions(new Vector2(left, Y(note.Y)), new Vector2(width, height));
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var earlier in boxes)
                {
                    if (!Overlap(box, earlier))
                        continue;
                    box = UIBox2.FromDimensions(new Vector2(box.Left, earlier.Bottom + NoteGap * ui), box.Size);
                    moved = true;
                }
            }

            boxes.Add(box);
        }

        return boxes;
    }

    /// <summary>
    /// True when every stage's game time and remarks lie wholly inside the picture, and clear of the plate where the plate
    /// covers the picture. Boxes never overlap each other by construction; whatever does not fit sends the chapter to the
    /// scroll area, where nothing is drawn over anything.
    /// </summary>
    public static bool NotesFit(SWTrainingChapter chapter, UIBox2 canvas, float pixel, float ui, SWTrainingMeasure measure, UIBox2? plate)
    {
        const float slack = 0.5f;
        foreach (var shot in chapter.Shots)
        {
            if (shot.Stages is not { } stages)
                continue;

            for (var i = 0; i < stages.Length; i++)
            {
                var boxes = StageBoxes(stages, i, canvas, pixel, ui, measure);
                foreach (var box in boxes)
                {
                    if (box.Left < canvas.Left - slack || box.Right > canvas.Right + slack ||
                        box.Top < canvas.Top - slack || box.Bottom > canvas.Bottom + slack)
                        return false;
                    if (plate is { } covered && Overlap(box, covered))
                        return false;
                }

                // A drawn page shows the stage's game window. The clock, its time and the remarks stay off it; only the
                // popup goes over it, as the game's own popup does.
                if (stages[i].Panel is { } panel)
                {
                    var window = Inflate(PanelRectOnScreen(stages[i].Visual.Scene, panel, canvas, pixel, ui), PanelClearance);
                    foreach (var box in boxes.Concat(ClockMarks(stages[i], canvas, pixel)))
                    {
                        if (Overlap(box, window))
                            return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// What the scroll area says for a stage, after the shot's own lines: its game time, its remarks, its popup and the
    /// action on its window. These are the same words the picture shows in the drawn mode, and only one of the two
    /// places shows them.
    /// </summary>
    public static List<(string Text, SWTrainingScrollTone Tone)> StageScrollLines(SWTrainingStage[] stages, int index)
    {
        var stage = stages[index];
        var lines = new List<(string Text, SWTrainingScrollTone Tone)>();
        if (stage.Visual.ClockText != null)
            lines.Add(($"Игровое время: {stage.Visual.ClockText}", SWTrainingScrollTone.Plain));

        foreach (var annotation in SWSenateTrainingScript.VisibleAnnotations(stages, index))
        {
            var text = annotation.Heading == null ? annotation.Text : $"{annotation.Heading}: {annotation.Text}";
            lines.Add((text, annotation.Tone == SWTrainingTone.Teaching ? SWTrainingScrollTone.Teaching : SWTrainingScrollTone.Plain));
        }

        if (stage.Popup != null)
            lines.Add(($"Всплывающее сообщение: {stage.Popup}", SWTrainingScrollTone.Popup));

        // No pointer can be drawn on a control in the scroll area, so the action is said in words and framed.
        if (stage.Panel != null && stage.Pointer != SWTrainingPointer.None && SWTrainingUiPanels.TargetLabel(stage.Panel) is { } label)
        {
            var line = stage.Pointer == SWTrainingPointer.Press ? $"Нажатие: {label}." : $"Следующее действие: {label}.";
            lines.Add((line, SWTrainingScrollTone.Action));
        }

        return lines;
    }
}
