using Content.Client._SW.Tutorial;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// Shows the campaign's next-thing line in the style of the tutorial. A line that stayed up for twenty minutes would cover
/// the world and press a player who is waiting on purpose, so it comes and goes.
/// </summary>
public sealed class SWGlobalWarCoachUIController : UIController
{
    /// <summary>
    /// Over the left edge of the hands, which stay centred. An open inventory takes the corner of the screen.
    /// </summary>
    internal const float LeftOfCenter = 190;

    /// <summary>
    /// The room the longest line takes, in UI units.
    /// </summary>
    private const float PlateWidth = 480;
    private const float PlateHeight = 24;

    private static readonly SoundSpecifier RewardSound = new SoundPathSpecifier("/Audio/Effects/double_beep.ogg");

    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;

    private SWTutorialPrompt? _prompt;
    private SWTutorialFocusOverlay? _overlay;
    private SWTutorialHighlightBox? _box;
    private Control? _boxTarget;

    private SWGlobalWarCoachHint _hint;
    private NetCoordinates? _focus;
    private TimeSpan? _warAt;
    private TimeSpan _hintStarted;
    private NetCoordinates? _focusShown;

    public override void Initialize()
    {
        base.Initialize();

        var gameplayLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayLoad.OnScreenLoad += Refresh;
        gameplayLoad.OnScreenUnload += RemoveFromScreen;
    }

    public void ResetConnection()
    {
        _hint = SWGlobalWarCoachHint.None;
        _focus = null;
        _warAt = null;
        RemoveFromScreen();
    }

    internal void OnCoach(SWGlobalWarCoachEvent ev, EntitySessionEventArgs args)
    {
        // The kit and the way out lead on to the next line. A line that just ends is a success only where the player
        // ended it: the war or a route. A war that begins ends the other lines too, and that is not theirs to celebrate.
        var progress = _hint != ev.Hint && (ev.Hint != SWGlobalWarCoachHint.None
            ? _hint is SWGlobalWarCoachHint.Kit or SWGlobalWarCoachHint.Teleporter
            : _hint is SWGlobalWarCoachHint.HeadDeclare or SWGlobalWarCoachHint.Route);

        if (_hint != ev.Hint)
            _hintStarted = _timing.RealTime;

        _hint = ev.Hint;
        _focus = ev.Focus;
        _warAt = ev.WarIn is { } warIn ? _timing.RealTime + warIn : null;

        Refresh();
        ShowFocus(_focus);

        if (progress)
        {
            _overlay?.Reward();
            _prompt?.Done();
            EntityManager.System<AudioSystem>().PlayGlobal(RewardSound, Filter.Local(), false);
        }
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_prompt == null)
            return;

        var reduced = _config.GetCVar(CCVars.ReducedMotion);
        if (_overlay != null)
            _overlay.ReducedMotion = reduced;

        var inWindow = UpdateHighlight(reduced);
        var shown = !inWindow && IsShown();
        ShowFocus(shown ? _focus : null);

        // A line over a window would lie on its rows. The pointer in the world stays.
        var covered = WindowOver(UIManager, -LeftOfCenter, SWTutorialPrompt.AboveBottom, PlateWidth, PlateHeight);
        var line = shown && !covered ? Line() : null;
        if (_prompt.PromptText != (line ?? string.Empty))
            _prompt.SetText(line, null);
    }

    /// <summary>
    /// Whether an open window covers this part of the screen, given in UI units from the bottom centre.
    /// </summary>
    internal static bool WindowOver(IUserInterfaceManager ui, float left, float bottom, float width, float height)
    {
        var root = ui.RootControl;
        var x = root.PixelSize.X / 2f + left * root.UIScale;
        var y = root.PixelSize.Y - bottom * root.UIScale;
        var area = new UIBox2(x, y - height * root.UIScale, x + width * root.UIScale, y);

        foreach (var child in ui.WindowRoot.Children)
        {
            if (child is not BaseWindow { Visible: true } window)
                continue;

            var rect = UIBox2.FromDimensions(window.GlobalPixelPosition, window.PixelSize);
            if (rect.Left < area.Right && area.Left < rect.Right && rect.Top < area.Bottom && area.Top < rect.Bottom)
                return true;
        }

        return false;
    }

    private void Refresh()
    {
        if (_hint != SWGlobalWarCoachHint.None && UIManager.ActiveScreen is InGameScreen && _prompt == null)
        {
            _prompt = new SWTutorialPrompt();
            UIManager.PopupRoot.AddChild(_prompt);
            LayoutContainer.SetAnchorPreset(_prompt, LayoutContainer.LayoutPreset.CenterBottom);
            LayoutContainer.SetGrowHorizontal(_prompt, LayoutContainer.GrowDirection.End);
            LayoutContainer.SetGrowVertical(_prompt, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetMarginLeft(_prompt, -LeftOfCenter);
            LayoutContainer.SetMarginRight(_prompt, -LeftOfCenter);
            LayoutContainer.SetMarginBottom(_prompt, -SWTutorialPrompt.AboveBottom);

            if (!_overlays.HasOverlay<SWTutorialFocusOverlay>())
            {
                _overlay = new SWTutorialFocusOverlay();
                _overlays.AddOverlay(_overlay);
            }
        }

    }

    private void ShowFocus(NetCoordinates? focus)
    {
        if (Nullable.Equals(_focusShown, focus))
            return;

        _focusShown = focus;
        _overlay?.SetFocus(null, focus);
    }

    private bool IsShown()
    {
        var remaining = _warAt is { } warAt ? (warAt - _timing.RealTime).TotalSeconds : 0;
        return SWGlobalWarCoachRules.IsShown(_hint, (_timing.RealTime - _hintStarted).TotalSeconds, remaining);
    }

    private string? Line()
    {
        if (SWGlobalWarCoachRules.LocKey(_hint) is not { } key)
            return null;

        if (_warAt is not { } warAt)
            return Loc.GetString(key);

        var remaining = warAt - _timing.RealTime;
        return remaining <= TimeSpan.Zero
            ? null
            : Loc.GetString(key, ("time", SWGlobalWarCharacterPanel.Clock(remaining)));
    }

    /// <returns>Whether the control the line talks about is on screen, where the frame says it all.</returns>
    private bool UpdateHighlight(bool reduced)
    {
        Control? target = null;
        if (_hint == SWGlobalWarCoachHint.HeadDeclare)
        {
            foreach (var child in UIManager.WindowRoot.Children)
            {
                if (child is SWGlobalWarDiplomacyMenu menu)
                    target = menu.DeclareWarControl;
            }
        }

        if (target is not { VisibleInTree: true })
        {
            RemoveHighlight();
            return false;
        }

        if (target != _boxTarget)
        {
            RemoveHighlight();
            _boxTarget = target;
            _box = new SWTutorialHighlightBox();
            UIManager.PopupRoot.AddChild(_box);
        }

        _box!.ReducedMotion = reduced;
        _box.Follow(target);
        return true;
    }

    private void RemoveHighlight()
    {
        _box?.Orphan();
        _box = null;
        _boxTarget = null;
    }

    private void RemoveFromScreen()
    {
        RemoveHighlight();
        _prompt?.Orphan();
        _prompt = null;
        _focusShown = null;

        if (_overlay != null)
        {
            _overlays.RemoveOverlay(_overlay);
            _overlay = null;
        }
    }
}
