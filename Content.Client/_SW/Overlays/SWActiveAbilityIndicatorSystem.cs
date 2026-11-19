using System.Numerics;
using Content.Client._SW.Overlays;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.Actions.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._SW;

public sealed class SWActiveAbilityIndicatorSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private const float IconOffsetX = 25f;
    private const float IconOffsetY = -25f;
    private const float IconSize = 32f;

    private SWActiveAbilityIndicatorOverlay? _currentOverlay;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var localPlayer = _player.LocalEntity;

        if (!localPlayer.HasValue || !_input.MouseScreenPosition.IsValid)
        {
            RemoveOverlay();
            return;
        }

        // The engine's source of truth: which action is currently being aimed.
        // Works for all targeted abilities (SW and vanilla).
        var selecting = _ui.GetUIController<ActionUIController>().SelectingTargetFor;

        if (selecting == null || !selecting.Value.IsValid())
        {
            RemoveOverlay();
            return;
        }

        if (!TryComp<ActionComponent>(selecting.Value, out var baseAction) || baseAction.Icon == null)
        {
            RemoveOverlay();
            return;
        }

        var actionIcon = _sprite.Frame0(baseAction.Icon);
        if (actionIcon == null)
        {
            RemoveOverlay();
            return;
        }

        var mousePos = _input.MouseScreenPosition.Position;
        var drawPos = new Vector2(mousePos.X + IconOffsetX, mousePos.Y + IconOffsetY);

        if (_currentOverlay == null || _currentOverlay.Icon != actionIcon)
        {
            RemoveOverlay();
            _currentOverlay = new SWActiveAbilityIndicatorOverlay(actionIcon, drawPos, IconSize);
            _overlayManager.AddOverlay(_currentOverlay);
        }
        else
        {
            _currentOverlay.UpdatePosition(drawPos);
        }
    }

    private void RemoveOverlay()
    {
        if (_currentOverlay != null)
        {
            _overlayManager.RemoveOverlay(_currentOverlay);
            _currentOverlay = null;
        }
    }
}