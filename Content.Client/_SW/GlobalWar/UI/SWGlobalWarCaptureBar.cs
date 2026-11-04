using System.Numerics;
using Content.Client._SW.UserInterface;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// Thirty cells of ten seconds: the attacker's taken, the owner's free, and a white front between them. Stripes run
/// on the taken part while the capture goes on. A contested zone gets hazard strips above and below.
/// </summary>
public sealed class SWGlobalWarCaptureBar : Control
{
    private const int CellCount = 30;
    private const float CellGap = 2;
    private const float CellHeight = 38;
    private const float FrontHeight = 48;
    private const float StripeSpeed = 24;
    private const float StripeWidth = 5;
    private const float StripePeriod = 14;
    private const float StripeLight = .55f;
    private const float HazardHeight = 5;

    private static readonly Color HazardBack = Color.FromHex("#17110A");

    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();

    public float Progress;
    public float Duration = 1;
    public SWGlobalWarFaction Attacker;
    public SWGlobalWarFaction Owner;
    public bool Running;
    public bool Contested;
    public bool Off;
    public bool Finished;

    private bool _wasFinished;
    private float _frontX;
    private TimeSpan? _burstStart;
    private TimeSpan _nextSpark;
    private const float SparkSeconds = 3;

    public SWGlobalWarCaptureBar()
    {
        MinHeight = FrontHeight;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    private static (Color Fill, Color Border) SideColours(SWGlobalWarFaction side)
    {
        var accent = side switch
        {
            SWGlobalWarFaction.Republic => "Republic",
            SWGlobalWarFaction.Empire => "Empire",
            _ => SWTerminalStyle.NeutralAccent,
        };
        var (fill, _, border) = SWTerminalStyle.AccentFills(accent);
        return (fill, border);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var moving = !_configuration.GetCVar(CCVars.ReducedMotion);
        var time = (float) _timing.RealTime.TotalSeconds;
        var ratio = Duration > 0 ? Math.Clamp(Progress / Duration, 0, 1) : 0;
        var taken = Finished ? CellCount : (int) MathF.Floor(ratio * CellCount);
        var gap = CellGap * UIScale;
        var cell = (PixelWidth - gap * (CellCount - 1)) / CellCount;
        var top = (PixelHeight - CellHeight * UIScale) / 2;
        var bottom = top + CellHeight * UIScale;
        var (attackerFill, attackerBorder) = SideColours(Attacker);
        var (ownerFill, ownerBorder) = SideColours(Owner);
        var offFill = Color.FromHex("#1F2127");
        var offHeld = Color.FromHex("#4A4F5A");
        var offHeldBorder = Color.FromHex("#7A808E");

        for (var i = 0; i < CellCount; i++)
        {
            var left = i * (cell + gap);
            var box = new UIBox2(left, top, left + cell, bottom);
            var mine = i < taken;
            var fill = Off ? mine ? offHeld : offFill : mine ? attackerFill : Dim(ownerFill);
            handle.DrawRect(box, fill);
            handle.DrawRect(box, Off ? mine ? offHeldBorder : SWTerminalStyle.CardBorder : mine ? attackerBorder : ownerBorder, false);
            if (mine && !Off)
                DrawStripes(handle, box, attackerBorder, moving && Running ? time * StripeSpeed * UIScale : 0);
        }

        if (Contested)
        {
            DrawHazard(handle, 0, moving ? time * StripeSpeed * UIScale : 0);
            DrawHazard(handle, PixelHeight - HazardHeight * UIScale, moving ? -time * StripeSpeed * UIScale : 0);
        }

        if (moving)
            DrawEffects(handle);

        if (taken is <= 0 or >= CellCount || Off)
            return;

        // The front breathes between 70 and 100 % while the capture goes on.
        var light = moving && Running ? .85f + .15f * MathF.Sin(time * MathF.Tau) : 1;
        var x = taken * (cell + gap) - gap / 2;
        _frontX = x;
        handle.DrawRect(new UIBox2(x - 2 * UIScale, 0, x + 2 * UIScale, PixelHeight), Color.White.WithAlpha(light));
    }

    /// <summary>A spark at the front of a contested zone every few seconds, and a burst where the capture ended.</summary>
    private void DrawEffects(DrawingHandleScreen handle)
    {
        var now = _timing.RealTime;
        var at = new Vector2(_frontX, PixelHeight / 2f);
        if (Finished && !_wasFinished && _frontX > 0)
            _burstStart = now;
        _wasFinished = Finished;

        if (_burstStart is { } start)
        {
            var effect = Attacker == SWGlobalWarFaction.Empire ? SWGlobalWarFx.BurstEmpire : SWGlobalWarFx.BurstRepublic;
            var time = (float) (now - start).TotalSeconds;
            effect.Draw(handle, at, UIScale, time);
            if (time > effect.Duration)
                _burstStart = null;
        }

        if (!Contested || _frontX <= 0)
            return;
        if (now >= _nextSpark)
            _nextSpark = now + TimeSpan.FromSeconds(SparkSeconds);
        var spark = SparkSeconds - (float) (_nextSpark - now).TotalSeconds;
        SWGlobalWarFx.ZapContested.Draw(handle, at, UIScale, spark);
    }

    private static Color Dim(Color color) => new(color.R * .72f, color.G * .72f, color.B * .72f);

    private void DrawStripes(DrawingHandleScreen handle, UIBox2 box, Color color, float run)
    {
        var period = StripePeriod * UIScale;
        var width = StripeWidth * UIScale;
        color = color.WithAlpha(StripeLight);

        // A slanted band is built row by row, so that it keeps its width and stays inside the cell.
        for (var y = box.Top; y < box.Bottom; y++)
        {
            var slant = box.Bottom - y;
            var start = ((run + slant) % period + period) % period - period;
            for (var x = box.Left + start; x < box.Right; x += period)
            {
                var from = MathF.Max(x, box.Left);
                var to = MathF.Min(x + width, box.Right);
                if (to > from)
                    handle.DrawRect(new UIBox2(from, y, to, MathF.Min(y + 1, box.Bottom)), color);
            }
        }
    }

    private void DrawHazard(DrawingHandleScreen handle, float y, float run)
    {
        var period = 10 * UIScale;
        var height = HazardHeight * UIScale;
        handle.DrawRect(new UIBox2(0, y, PixelWidth, y + height), HazardBack);
        for (var row = 0f; row < height; row++)
        {
            var start = ((run + height - row) % period + period) % period - period;
            for (var x = start; x < PixelWidth; x += period)
            {
                var from = MathF.Max(x, 0);
                var to = MathF.Min(x + period * .6f, PixelWidth);
                if (to > from)
                    handle.DrawRect(new UIBox2(from, y + row, to, MathF.Min(y + row + 1, y + height)), SWGlobalWarConsoleStyle.Amber);
            }
        }
    }
}
