using Content.Client.Actions;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._SW.Droids;
using Content.Shared.Actions.Components;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Droids;

public sealed class SWDroidCommandMenuSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    private SimpleRadialMenu? _menu;

    private static readonly string[] OrderActions =
    {
        "ActionSWDroidOrderFollow",
        "ActionSWDroidOrderStay",
        "ActionSWDroidOrderGoTo",
        "ActionSWDroidOrderAttack"
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidOpenCommandMenuActionEvent>(OnOpenCommandMenu);
    }

    private void OnOpenCommandMenu(SWDroidOpenCommandMenuActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleMenu();
    }

    private void ToggleMenu()
    {
        if (_menu != null)
        {
            CloseMenu();
            return;
        }

        _menu = new SimpleRadialMenu();
        _menu.OnClose += CloseMenu;
        _menu.SetButtons(CreateButtons());
        _menu.OpenOverMouseScreenPosition();
    }

    private IEnumerable<RadialMenuOptionBase> CreateButtons()
    {
        if (_player.LocalEntity is not { } player ||
            !TryComp<ActionsComponent>(player, out var actions))
        {
            return Array.Empty<RadialMenuOptionBase>();
        }

        var buttons = new List<RadialMenuOptionBase>();
        foreach (var orderAction in OrderActions)
        {
            if (!TryFindAction(actions, orderAction, out var action))
                continue;

            buttons.Add(new RadialMenuActionOption<EntityUid>(HandleOrderAction, action.Owner)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(action.Comp.Icon),
                ToolTip = GetActionName(action.Owner)
            });
        }

        return buttons;
    }

    private bool TryFindAction(ActionsComponent actions, string prototypeId, out Entity<ActionComponent> action)
    {
        foreach (var actionUid in actions.Actions)
        {
            if (_actions.GetAction(actionUid) is not { } actionEntity ||
                !TryComp(actionUid, out MetaDataComponent? metadata) ||
                metadata.EntityPrototype?.ID != prototypeId)
            {
                continue;
            }

            action = actionEntity;
            return true;
        }

        action = default;
        return false;
    }

    private string GetActionName(EntityUid action)
    {
        return TryComp(action, out MetaDataComponent? metadata)
            ? metadata.EntityName
            : string.Empty;
    }

    private void HandleOrderAction(EntityUid action)
    {
        if (_actions.GetAction(action) is not { } actionEntity)
            return;

        if (HasComp<TargetActionComponent>(action))
        {
            _ui.GetUIController<ActionUIController>().TryToggleTargeting(action);
            return;
        }

        _actions.TriggerAction(actionEntity);
    }

    private void CloseMenu()
    {
        if (_menu == null)
            return;

        _menu.OnClose -= CloseMenu;
        _menu.Dispose();
        _menu = null;
    }
}
