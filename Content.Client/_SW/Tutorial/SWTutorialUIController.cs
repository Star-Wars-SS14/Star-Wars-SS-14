using System.Linq;
using Content.Client.Guidebook.Controls;
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
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Content.Client.UserInterface.Systems.Inventory.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._SW.Tutorial;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// The client side of the tutorial: the choice and finish screens, the lobby button, the prompt, the highlight, and
/// the list of what the current step shows.
/// </summary>
public sealed class SWTutorialUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>
{
    /// <summary>
    /// Just above the hotbar, so the prompt stays in one place when the hotbar comes on.
    /// </summary>
    private const float PromptAboveBottom = 72;

    private static readonly TimeSpan GuidebookCheckTime = TimeSpan.FromSeconds(3);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private SWTutorialChoicePopup? _choicePopup;
    private SWTutorialFinishPopup? _finishPopup;
    private SWTutorialPrompt? _prompt;
    private SWTutorialHighlightBox? _highlightBox;
    private Button? _lobbyButton;

    private bool _active;
    private bool _offerPending;

    /// <summary>
    /// This player was offered the tutorial or went through it, in this launch of the game or an earlier one. The
    /// guidebook upstream opens by itself for an hour of play would only cover the lobby button. It still opens by hand.
    /// </summary>
    private bool _skipGuidebook;

    /// <summary>
    /// Until when to look for a guidebook to close. Upstream opens it when play times arrive, a moment after the lobby
    /// does, so one check on entry is too early; a guidebook opened by hand after that stays open.
    /// </summary>
    private TimeSpan? _closeGuidebookUntil;
    private SWTutorialFinishedEvent? _finishPending;
    private SWTutorialStateEvent? _state;

    private InGameScreen? _screen;

    /// <summary>
    /// What stays visible. Everything else is hidden, so a widget added by an upstream merge stays hidden too.
    /// </summary>
    private readonly HashSet<Control> _shown = new();

    /// <summary>
    /// The containers that lead from the screen down to the controls in <see cref="_shown"/>.
    /// </summary>
    private readonly HashSet<Control> _shownParents = new();

    private readonly List<Control> _hidden = new();
    private Control? _highlightTarget;
    private bool _showAll;
    private bool _showSlots;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<SWTutorialOfferEvent>(OnOffer);
        SubscribeNetworkEvent<SWTutorialSeenEvent>(OnSeen);
        SubscribeNetworkEvent<SWTutorialStateEvent>(OnState);
        SubscribeNetworkEvent<SWTutorialFinishedEvent>(OnFinished);

        var gameplayLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayLoad.OnScreenLoad += OnScreenLoad;
        gameplayLoad.OnScreenUnload += RemoveFromScreen;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        UpdateGuidebook();

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

        FilterActions();
        UpdateHighlight();

        // The chat is hidden but its keys still focus it, which would swallow the movement keys.
        if (UIManager.KeyboardFocused is { VisibleInTree: false } focused)
            UIManager.ReleaseKeyboardFocus(focused);
    }

    public void OnStateEntered(LobbyState state)
    {
        _active = false;
        _state = null;
        _closeGuidebookUntil = _timing.RealTime + GuidebookCheckTime;

        if (state.Lobby is not { } lobby)
            return;

        _lobbyButton = new Button
        {
            Text = Loc.GetString("sw-tutorial-lobby-button"),
            StyleClasses = { "ButtonBig" },
        };
        _lobbyButton.OnPressed += _ => OpenChoice(false);

        // LobbyGui.xaml stays untouched, the button goes next to the ready button.
        lobby.ReadyButton.Parent?.AddChild(_lobbyButton);
        _lobbyButton.SetPositionInParent(lobby.ReadyButton.GetPositionInParent());
    }

    public void OnStateExited(LobbyState state)
    {
        // The lobby keeps its screen between visits, so a button left there would pile up.
        _lobbyButton?.Orphan();
        _lobbyButton = null;
        CloseChoice();
        CloseFinish();
    }

    private void UpdateGuidebook()
    {
        if (_closeGuidebookUntil is not { } until || _lobbyButton == null)
            return;

        if (_timing.RealTime > until)
        {
            _closeGuidebookUntil = null;
            return;
        }

        if (!_skipGuidebook)
            return;

        _closeGuidebookUntil = null;
        foreach (var window in UIManager.WindowRoot.Children.OfType<GuidebookWindow>().ToArray())
        {
            window.Close();
        }
    }

    private void OnOffer(SWTutorialOfferEvent ev, EntitySessionEventArgs args)
    {
        _offerPending = true;
        SkipGuidebook();
    }

    private void OnSeen(SWTutorialSeenEvent ev, EntitySessionEventArgs args)
    {
        SkipGuidebook();
    }

    private void SkipGuidebook()
    {
        _skipGuidebook = true;
        _closeGuidebookUntil = _timing.RealTime + GuidebookCheckTime;
    }

    private void OnFinished(SWTutorialFinishedEvent ev, EntitySessionEventArgs args)
    {
        _finishPending = ev;
    }

    private void OnState(SWTutorialStateEvent ev, EntitySessionEventArgs args)
    {
        _active = ev.Active;
        _state = ev;
        _skipGuidebook |= ev.Active;

        if (!_active)
        {
            RestoreHidden();
            RemoveFromScreen();
            return;
        }

        if (UIManager.ActiveScreen is InGameScreen)
            OnScreenLoad();
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
        CloseChoice();

        if (choice != null)
            EntityManager.EntityNetManager.SendSystemNetworkMessage(new SWTutorialChoiceEvent(choice.Value));
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
            _prompt.OnExitPressed += OnExitPressed;
            _prompt.OnReturnPressed += OnReturnPressed;
            screen.AddWidget(_prompt);
            LayoutContainer.SetAnchorPreset(_prompt, LayoutContainer.LayoutPreset.CenterBottom);
            LayoutContainer.SetGrowHorizontal(_prompt, LayoutContainer.GrowDirection.Both);
            LayoutContainer.SetGrowVertical(_prompt, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetMarginBottom(_prompt, -PromptAboveBottom);
        }

        ApplyStep(screen);
    }

    private void ApplyStep(InGameScreen screen)
    {
        var state = _state!;
        var step = state.Step is { } id ? _proto.Index(id) : null;

        _prompt!.SetText(
            state.Prompt is { } prompt ? FormatPrompt(prompt) : null,
            state.Big,
            state.Note is { } note ? FormatPrompt(note) : null);
        _prompt.SetReturnButton(step?.ReturnButton ?? false);

        // A step can show less than the one before it, so hidden and shown are worked out anew each time.
        RestoreHidden();
        _showAll = step?.Show.Contains(SWTutorialHudElement.All) ?? false;
        _showSlots = _showAll || (step?.Show.Contains(SWTutorialHudElement.Slots) ?? false);

        if (_showAll)
            return;

        CollectShown(screen, step);
        HideOthers(screen);
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
            ("swap", KeyName(ContentKeyFunctions.SwapHands)));
    }

    /// <summary>
    /// In a frame, because a lone Я reads as a word.
    /// </summary>
    private string KeyName(BoundKeyFunction function)
    {
        return $"[{_input.GetKeyFunctionButtonString(function)}]";
    }

    private void RemoveFromScreen()
    {
        _prompt?.Orphan();
        _prompt = null;
        RemoveHighlight();
        _screen = null;
        _shown.Clear();
        _shownParents.Clear();
        _hidden.Clear();
        _showAll = false;
    }

    private void CollectShown(InGameScreen screen, SWTutorialStepPrototype? step)
    {
        _shown.Clear();
        _shownParents.Clear();

        if (_prompt != null)
            AddShown(_prompt, screen);
        if (screen.GetWidget<MainViewport>() is { } viewport)
            AddShown(viewport, screen);
        if (screen.GetWidget<GameTopMenuBar>() is { } menu)
        {
            AddShown(menu.EscapeButton, screen);
            AddShown(menu.AHelpButton, screen);
        }

        if (step == null)
            return;

        foreach (var element in step.Show)
        {
            UIWidget? widget = element switch
            {
                SWTutorialHudElement.Hands or SWTutorialHudElement.Slots => screen.GetWidget<HotbarGui>(),
                SWTutorialHudElement.Actions => screen.GetWidget<ActionsBar>(),
                SWTutorialHudElement.Alerts => screen.GetWidget<AlertsUI>(),
                SWTutorialHudElement.Chat => (UIWidget?) screen.GetWidget<ResizableChatBox>() ?? screen.GetWidget<ChatBox>(),
                SWTutorialHudElement.Inventory => screen.GetWidget<InventoryGui>(),
                _ => null,
            };

            if (widget != null)
                AddShown(widget, screen);
        }
    }

    private void AddShown(Control control, InGameScreen screen)
    {
        _shown.Add(control);
        for (var parent = control.Parent; parent != null && parent != screen; parent = parent.Parent)
        {
            _shownParents.Add(parent);
        }
    }

    private void HideOthers(Control parent)
    {
        foreach (var child in parent.Children)
        {
            if (_shown.Contains(child))
            {
                if (!_showSlots && child is HotbarGui)
                    HideSlots(child);

                continue;
            }

            if (_shownParents.Contains(child))
            {
                HideOthers(child);
                continue;
            }

            Hide(child);
        }
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

    /// <summary>
    /// A step can leave one action button on the bar. The bar rebuilds itself, so this runs every frame.
    /// </summary>
    private void FilterActions()
    {
        if (_screen?.GetWidget<ActionsBar>() is not { Visible: true } bar || CurrentStep() is not { } step)
            return;

        foreach (var button in FindAll<ActionButton>(bar))
        {
            if (_showAll || step.ShowActions.Count == 0)
            {
                button.Visible = true;
                continue;
            }

            var id = button.Action is { } action ? EntityManager.GetComponentOrNull<MetaDataComponent>(action)?.EntityPrototype?.ID : null;
            button.Visible = id != null && step.ShowActions.Contains(id);
        }
    }

    private void UpdateHighlight()
    {
        var target = _state?.Highlight switch
        {
            SWTutorialHighlight.CombatAction => FindCombatButton(),
            SWTutorialHighlight.PocketSlot => FindPocketSlot(),
            SWTutorialHighlight.ReturnButton => _prompt?.ReturnControl,
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
            _highlightBox = new SWTutorialHighlightBox();
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

    private Control? FindPocketSlot()
    {
        return _screen?.GetWidget<HotbarGui>() is { } hotbar
            ? FindAll<SlotControl>(hotbar).FirstOrDefault(slot => slot.SlotName == "pocket1")
            : null;
    }

    private SWTutorialStepPrototype? CurrentStep()
    {
        return _state?.Step is { } id ? _proto.Index(id) : null;
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
