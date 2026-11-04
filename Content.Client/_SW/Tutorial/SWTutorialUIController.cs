using System.Linq;
using System.Numerics;
using Content.Client.Info;
using Content.Client.Lobby;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.UserInterface.Systems.Ghost.Widgets;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Content.Client.UserInterface.Systems.Inventory.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._SW.Tutorial;
using Content.Shared.Input;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Content.Shared.CCVar;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// The client side of the tutorial: the choice and finish screens, the lobby button, the prompt, the highlight, and
/// the list of what the current step shows.
/// </summary>
public sealed class SWTutorialUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>
{
    private const float CornerAboveBottom = SWTutorialPrompt.AboveBottom;
    private const float CornerLeft = SWTutorialPrompt.FromLeft;

    /// <summary>
    /// Beside the menu button, which stands in the corner.
    /// </summary>
    private const float PromptLeft = 48;

    /// <summary>
    /// How far the character has to move for the movement keys to count as learned, in tiles.
    /// </summary>
    private const float MovedDistance = 0.4f;
    private const float MoveKeysAboveCharacter = 40f;

    /// <summary>
    /// The same beat this long means the student needs a stronger pointer, not more words.
    /// </summary>
    private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StuckRamp = TimeSpan.FromSeconds(2);
    private static readonly ResolvedPathSpecifier RewardSound = new("/Audio/Effects/double_beep.ogg");

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private SWTutorialChoicePopup? _choicePopup;
    private SWTutorialFinishPopup? _finishPopup;
    private SWTutorialPrompt? _prompt;
    private SWTutorialCorner? _corner;
    private SWTutorialFocusOverlay? _focusOverlay;
    private TimeSpan _beatStarted;
    private SWTutorialMoveKeys? _moveKeys;
    private EntityCoordinates? _moveKeysFrom;
    private bool _moveKeysDone;
    private SWTutorialHighlightBox? _highlightBox;
    private Button? _lobbyButton;

    private bool _active;
    private bool _offerPending;

    private SWTutorialFinishedEvent? _finishPending;
    private SWTutorialStateEvent? _state;

    private InGameScreen? _screen;

    private readonly List<Control> _hidden = new();
    private Control? _highlightTarget;
    private bool _showAll;
    private bool _showSlots;
    private bool _allRequested;
    private Guid _attempt;
    private readonly HashSet<SWTutorialHudElement> _unlocked = new();

    public override void Initialize()
    {
        base.Initialize();

        var gameplayLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayLoad.OnScreenLoad += OnScreenLoad;
        gameplayLoad.OnScreenUnload += RemoveFromScreen;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_lobbyButton != null && !UIManager.WindowRoot.Children.Any(c => c is RulesPopup))
        {
            if (_finishPending != null)
            {
                OpenFinish(_finishPending);
                _finishPending = null;
            }
            else if (_offerPending)
            {
                _offerPending = false;
                OpenChoice(true);
            }
        }

        if (_screen == null)
            return;

        // Other controllers switch their own widgets on, and a merge can add new ones.
        if (!_showAll)
            HideOthers(_screen);

        if (_prompt != null && _state is { } state)
            UpdatePrompt(state);
        UpdateHighlight();
        UpdateUrgency();
        UpdateMoveKeys();

        // Never leave focus in a temporarily hidden teaching control. Chat itself remains available.
        if (UIManager.KeyboardFocused is { VisibleInTree: false } focused)
            UIManager.ReleaseKeyboardFocus(focused);
    }

    public void OnStateEntered(LobbyState state)
    {
        if (state.Lobby is not { } lobby)
            return;

        _lobbyButton = new Button
        {
            Text = Loc.GetString("sw-tutorial-lobby-button"),
            StyleClasses = { "ButtonBig" },
        };
        _lobbyButton.OnPressed += _ => OpenChoice(false);

        // Beside "Observe", under the main button: a beginner finds it right where the choice is made.
        lobby.TutorialSlot.AddChild(_lobbyButton);
        _lobbyButton.SetPositionFirst();
        _lobbyButton.HorizontalExpand = true;
    }

    public void OnStateExited(LobbyState state)
    {
        // The lobby keeps its screen between visits, so a button left there would pile up.
        _lobbyButton?.Orphan();
        _lobbyButton = null;
        CloseChoice();
        CloseFinish();
    }

    internal void OnOffer(SWTutorialOfferEvent ev, EntitySessionEventArgs args)
    {
        _offerPending = true;
    }

    internal void OnSeen(SWTutorialSeenEvent ev, EntitySessionEventArgs args)
    {
        // Never close a guidebook opened by the player.
    }

    internal void OnFinished(SWTutorialFinishedEvent ev, EntitySessionEventArgs args)
    {
        _finishPending = ev;
    }

    internal void OnState(SWTutorialStateEvent ev, EntitySessionEventArgs args)
    {
        if (_attempt != ev.Attempt)
        {
            _attempt = ev.Attempt;
            _unlocked.Clear();
            _allRequested = false;
        }
        _unlocked.UnionWith(ev.Unlocked);
        _active = ev.Active;
        NoteProgress(ev);
        _state = ev;

        if (!_active)
        {
            RestoreHidden();
            RemoveFromScreen();
            return;
        }

        if (UIManager.ActiveScreen is InGameScreen)
            OnScreenLoad();
    }

    internal void ResetConnection()
    {
        RestoreHidden();
        RemoveFromScreen();
        CloseChoice();
        CloseFinish();
        _active = false;
        _state = null;
        _offerPending = false;
        _finishPending = null;
    }

    private void OpenChoice(bool isOffer)
    {
        if (_choicePopup != null)
            return;

        var newToServerReady = _proto.EnumeratePrototypes<SWTutorialScenarioPrototype>()
            .Any(s => s.Mode == SWTutorialChoice.NewToServer);

        _choicePopup = new SWTutorialChoicePopup(isOffer, newToServerReady);
        _choicePopup.OnChoice += OnChoice;
        UIManager.WindowRoot.AddChild(_choicePopup);
        LayoutContainer.SetAnchorPreset(_choicePopup, LayoutContainer.LayoutPreset.Wide);
    }

    private void CloseChoice()
    {
        _choicePopup?.Orphan();
        _choicePopup = null;
    }

    private void OnChoice(SWTutorialChoice? choice)
    {
        var restart = _choicePopup?.RestartSelected ?? false;
        CloseChoice();

        if (choice != null)
            EntityManager.EntityNetManager.SendSystemNetworkMessage(new SWTutorialChoiceEvent(choice.Value, restart));
    }

    private void OpenFinish(SWTutorialFinishedEvent ev)
    {
        if (_finishPopup != null)
            return;

        _finishPopup = new SWTutorialFinishPopup(ev);
        _finishPopup.OnSide += side =>
        {
            CloseFinish();
            EntityManager.EntityNetManager.SendSystemNetworkMessage(new SWTutorialJoinSideEvent(side));
        };
        _finishPopup.OnClose += CloseFinish;
        UIManager.WindowRoot.AddChild(_finishPopup);
        LayoutContainer.SetAnchorPreset(_finishPopup, LayoutContainer.LayoutPreset.Wide);
    }

    private void CloseFinish()
    {
        _finishPopup?.Orphan();
        _finishPopup = null;
    }

    private void OnExitPressed()
    {
        EntityManager.EntityNetManager.SendSystemNetworkMessage(new SWTutorialExitEvent());
    }

    private void OnReturnPressed()
    {
        EntityManager.EntityNetManager.SendSystemNetworkMessage(new SWTutorialReturnEvent());
    }

    /// <summary>
    /// Runs in the same frame as the screen load, which is why the hidden HUD never gets drawn.
    /// </summary>
    private void OnScreenLoad()
    {
        if (!_active || _state == null || UIManager.ActiveScreen is not InGameScreen screen)
            return;

        if (_prompt == null || _screen != screen)
        {
            RemoveFromScreen();
            _screen = screen;

            _prompt = new SWTutorialPrompt();
            _prompt.OnReturnPressed += OnReturnPressed;

            // In the popup layer, because a step can open a window the line has to stay readable over.
            UIManager.PopupRoot.AddChild(_prompt);
            LayoutContainer.SetAnchorPreset(_prompt, LayoutContainer.LayoutPreset.BottomLeft);
            LayoutContainer.SetGrowVertical(_prompt, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetMarginLeft(_prompt, PromptLeft);
            LayoutContainer.SetMarginBottom(_prompt, -SWTutorialPrompt.AboveBottom);

            _corner = new SWTutorialCorner();
            _corner.OnExitPressed += OnExitPressed;
            _corner.OnRetryPressed += () => SendHelp(true);
            _corner.OnShowAllPressed += () =>
            {
                _allRequested = true;
                ApplyStep(screen);
            };
            screen.AddWidget(_corner);
            LayoutContainer.SetAnchorPreset(_corner, LayoutContainer.LayoutPreset.BottomLeft);
            LayoutContainer.SetGrowVertical(_corner, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetMarginLeft(_corner, CornerLeft);
            LayoutContainer.SetMarginBottom(_corner, -CornerAboveBottom);

            _focusOverlay = new SWTutorialFocusOverlay();
            _overlays.AddOverlay(_focusOverlay);
        }

        ApplyStep(screen);
    }

    private void ApplyStep(InGameScreen screen)
    {
        var state = _state!;
        var step = state.Step is { } id && _proto.TryIndex(id, out var found) ? found : null;

        UpdatePrompt(state);
        _prompt!.SetReturnButton(step?.ReturnButton ?? false);
        _focusOverlay?.SetFocus(state.FocusEntity, state.FocusCoordinates);

        // Previously introduced controls remain unlocked, including after reconnect.
        RestoreHidden();
        if (step != null)
            _unlocked.UnionWith(step.Show);
        _showAll = _allRequested || step == null || _unlocked.Contains(SWTutorialHudElement.All);
        _showSlots = _showAll || _unlocked.Contains(SWTutorialHudElement.Slots);

        if (_showAll)
            return;

        HideOthers(screen);
    }

    private void UpdatePrompt(SWTutorialStateEvent state)
    {
        if (_prompt == null)
            return;
        _prompt.SetText(state.Prompt is { } goal ? FormatPrompt(goal) : null,
            state.Note is { } note ? FormatPrompt(note) : null);
    }

    /// <summary>
    /// A step or a beat further on is the only reward the student needs: a sound, a tick and a green flash.
    /// </summary>
    private void NoteProgress(SWTutorialStateEvent state)
    {
        if (_state is not { Active: true } previous || previous.Attempt != state.Attempt || !state.Active)
        {
            _beatStarted = _timing.RealTime;
            _moveKeysDone = false;
            _moveKeysFrom = null;
            return;
        }

        var sameStep = previous.Step == state.Step;
        if (sameStep && previous.Beat == state.Beat)
            return;

        _beatStarted = _timing.RealTime;
        _moveKeysDone = _moveKeysDone || !sameStep;

        // A window the step has finished with would cover the world the next line points into.
        if (previous.Highlight != state.Highlight && _highlightTarget != null)
            CloseWindowOf(_highlightTarget);

        // Going back a beat, such as dropping the item, is not a success.
        if (sameStep && state.Beat < previous.Beat)
            return;

        _focusOverlay?.Reward();
        _prompt?.Done();
        EntityManager.System<AudioSystem>().PlayGlobal(RewardSound, Filter.Local(), false);
    }

    private static void CloseWindowOf(Control control)
    {
        for (Control? current = control; current != null; current = current.Parent)
        {
            if (current is BaseWindow window)
            {
                window.Close();
                return;
            }
        }
    }

    private SWTutorialBeat? CurrentBeat()
    {
        return CurrentStep() is { } step && _state is { } state && state.Beat >= 0 && state.Beat < step.Beats.Count
            ? step.Beats[state.Beat]
            : null;
    }

    private void UpdateUrgency()
    {
        var waited = _timing.RealTime - _beatStarted - StuckAfter;
        var urgency = CurrentBeat() is { Wait: true } ? 0f : Math.Clamp((float) (waited / StuckRamp), 0f, 1f);
        var reduced = _cfg.GetCVar(CCVars.ReducedMotion);
        if (_focusOverlay != null)
        {
            _focusOverlay.Urgency = urgency;
            _focusOverlay.ReducedMotion = reduced;
        }
        if (_highlightBox != null)
        {
            _highlightBox.Urgency = urgency;
            _highlightBox.ReducedMotion = reduced;
        }
    }

    private void UpdateMoveKeys()
    {
        var wanted = !_moveKeysDone && CurrentBeat() is { MoveKeys: true }
                     && _player.LocalEntity is { } body && EntityManager.HasComponent<TransformComponent>(body);
        if (!wanted)
        {
            RemoveMoveKeys();
            return;
        }

        var xform = EntityManager.GetComponent<TransformComponent>(_player.LocalEntity!.Value);
        var transform = EntityManager.System<SharedTransformSystem>();
        _moveKeysFrom ??= xform.Coordinates;
        if (!transform.InRange(xform.Coordinates, _moveKeysFrom.Value, MovedDistance))
        {
            _moveKeysDone = true;
            RemoveMoveKeys();
            return;
        }

        if (_moveKeys == null)
        {
            _moveKeys = new SWTutorialMoveKeys(
                PlainKeyName(EngineKeyFunctions.MoveUp),
                PlainKeyName(EngineKeyFunctions.MoveLeft),
                PlainKeyName(EngineKeyFunctions.MoveDown),
                PlainKeyName(EngineKeyFunctions.MoveRight));
            UIManager.PopupRoot.AddChild(_moveKeys);
        }

        var screen = _eye.CoordinatesToScreen(xform.Coordinates).Position;
        _moveKeys.Place(screen - new Vector2(0f, MoveKeysAboveCharacter * UIManager.DefaultUIScale));
    }

    private void RemoveMoveKeys()
    {
        _moveKeys?.Orphan();
        _moveKeys = null;
    }

    /// <summary>
    /// The keys are named as the player's keyboard shows them: on a Russian layout W reads Ц.
    /// </summary>
    private string FormatPrompt(LocId prompt)
    {
        return Loc.GetString(prompt,
            ("up", KeyName(EngineKeyFunctions.MoveUp)),
            ("left", KeyName(EngineKeyFunctions.MoveLeft)),
            ("down", KeyName(EngineKeyFunctions.MoveDown)),
            ("right", KeyName(EngineKeyFunctions.MoveRight)),
            ("wield", KeyName(ContentKeyFunctions.UseItemInHand)),
            ("swap", KeyName(ContentKeyFunctions.SwapHands)),
            ("drop", KeyName(ContentKeyFunctions.Drop)),
            ("chat", KeyName(ContentKeyFunctions.FocusChat)));
    }

    /// <summary>
    /// In a frame, because a lone Я reads as a word.
    /// </summary>
    private string KeyName(BoundKeyFunction function)
    {
        var key = _input.GetKeyFunctionButtonString(function);
        return string.IsNullOrWhiteSpace(key) ? Loc.GetString("sw-tutorial-key-unbound") : $"[{key}]";
    }

    private string PlainKeyName(BoundKeyFunction function)
    {
        var key = _input.GetKeyFunctionButtonString(function);
        return string.IsNullOrWhiteSpace(key) ? "?" : key;
    }

    private void RemoveFromScreen()
    {
        RestoreHidden();
        _prompt?.Orphan();
        _prompt = null;
        _corner?.Orphan();
        _corner = null;
        RemoveMoveKeys();
        if (_focusOverlay != null)
        {
            _overlays.RemoveOverlay(_focusOverlay);
            _focusOverlay = null;
        }
        RemoveHighlight();
        _screen = null;
        _hidden.Clear();
        _showAll = false;
    }

    private void HideOthers(Control parent)
    {
        // Only what the step needs stays on screen. Escape, the guidebook and admin help keep their keys.
        foreach (var child in parent.Children)
        {
            var locked = child switch
            {
                HotbarGui => !_unlocked.Contains(SWTutorialHudElement.Hands) && !_showSlots,
                ActionsBar => !_unlocked.Contains(SWTutorialHudElement.Actions),
                InventoryGui => !_unlocked.Contains(SWTutorialHudElement.Inventory),
                ChatBox => !_unlocked.Contains(SWTutorialHudElement.Chat),
                AlertsUI => !_unlocked.Contains(SWTutorialHudElement.Alerts),
                GameTopMenuBar => true,
                _ => false,
            };
            if (locked)
                Hide(child);
            else if (child is HotbarGui && !_showSlots)
                HideSlots(child);
            else if (child is ActionsBar)
                HideOtherActions(child);
            else
                HideOthers(child);
        }
    }

    /// <summary>
    /// The bar holds every action of the body and of what it carries, and only the one the step points at is on screen.
    /// </summary>
    private void HideOtherActions(Control bar)
    {
        if (CurrentStep() is not { } step)
            return;

        foreach (var button in FindAll<ActionButton>(bar))
        {
            if (button.Action is not { } action)
                continue;

            var id = EntityManager.GetComponentOrNull<MetaDataComponent>(action)?.EntityPrototype?.ID;
            if (id == null || !step.ShowActions.Contains(id))
            {
                Hide(button);
            }
            else if (_hidden.Remove(button))
            {
                // The bar reuses its buttons, so this one may have held another action before.
                button.Visible = true;
            }
        }
    }

    private void SendHelp(bool retry)
    {
        if (_state is { Active: true } state)
            EntityManager.EntityNetManager.SendSystemNetworkMessage(new SWTutorialHelpEvent(state.Attempt, state.Revision, retry));
    }

    private void HideSlots(Control parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is ItemSlotButtonContainer)
                Hide(child);
            else
                HideSlots(child);
        }
    }

    private void Hide(Control control)
    {
        if (!control.Visible)
            return;

        control.Visible = false;

        // Starts visible until its controller says otherwise, so bringing it back would show it to a living student.
        if (control is not GhostGui)
            _hidden.Add(control);
    }

    private void RestoreHidden()
    {
        foreach (var control in _hidden)
        {
            control.Visible = true;
        }

        _hidden.Clear();
    }

    private void UpdateHighlight()
    {
        var target = _state?.Highlight switch
        {
            SWTutorialHighlight.CombatAction => FindCombatButton(),
            SWTutorialHighlight.PocketSlot => FindPocketSlot(),
            SWTutorialHighlight.ReturnButton => _prompt?.ReturnControl,
            SWTutorialHighlight.ActiveHand => FindHand(true),
            SWTutorialHighlight.OtherHand => FindHand(false),
            SWTutorialHighlight.ChatInput => _screen == null ? null : FindAll<ChatBox>(_screen).FirstOrDefault()?.ChatInput,
            SWTutorialHighlight.ApcBreaker => FindNamed(UIManager.WindowRoot, "BreakerButton"),
            SWTutorialHighlight.LatheMode => FindNamed(UIManager.WindowRoot, "ModeOption"),
            _ => null,
        };

        if (target is not { VisibleInTree: true })
        {
            RemoveHighlight();
            return;
        }

        if (target != _highlightTarget)
        {
            RemoveHighlight();
            _highlightTarget = target;

            // These have text right above them, and free room to the left.
            _highlightBox = new SWTutorialHighlightBox
            {
                ArrowFromLeft = _state?.Highlight is SWTutorialHighlight.LatheMode or SWTutorialHighlight.ChatInput,
            };
            UIManager.PopupRoot.AddChild(_highlightBox);
        }

        _highlightBox!.Follow(target);
    }

    private void RemoveHighlight()
    {
        _highlightBox?.Orphan();
        _highlightBox = null;
        _highlightTarget = null;
    }

    private Control? FindCombatButton()
    {
        if (_screen?.GetWidget<ActionsBar>() is not { } bar || CurrentStep() is not { } step)
            return null;

        return FindAll<ActionButton>(bar).FirstOrDefault(button =>
            button.Action is { } action
            && EntityManager.GetComponentOrNull<MetaDataComponent>(action)?.EntityPrototype?.ID is { } id
            && step.ShowActions.Contains(id));
    }

    private Control? FindHand(bool active)
    {
        return _screen?.GetWidget<HotbarGui>() is { } hotbar
            ? FindAll<HandButton>(hotbar).FirstOrDefault(hand => hand.Highlight == active)
            : null;
    }

    private static Control? FindNamed(Control parent, string name)
    {
        return FindAll<Control>(parent).FirstOrDefault(control => control.Name == name);
    }

    private Control? FindPocketSlot()
    {
        return _screen?.GetWidget<HotbarGui>() is { } hotbar
            ? FindAll<SlotControl>(hotbar).FirstOrDefault(slot => slot.SlotName == "pocket1")
            : null;
    }

    private SWTutorialStepPrototype? CurrentStep()
    {
        return _state?.Step is { } id && _proto.TryIndex(id, out var step) ? step : null;
    }

    private static IEnumerable<T> FindAll<T>(Control parent) where T : Control
    {
        foreach (var child in parent.Children)
        {
            if (child is T found)
                yield return found;

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }
}
