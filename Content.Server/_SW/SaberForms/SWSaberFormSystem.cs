using Content.Shared._SW.ForceAbilities;
using Content.Shared._SW.ForceAbilities.SWMeleeWeaponSystem;
using Content.Shared._SW.SaberForms;
using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.SaberForms;

public sealed class SWSaberFormSystem : EntitySystem
{
    private const SWSaberFormsUiKey Key = SWSaberFormsUiKey.Key;

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedSWSaberFormSystem _forms = default!;
    [Dependency] private readonly SWSharedMeleeWeaponSystem _guard = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSaberFormComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWSaberFormComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SWSaberFormComponent, SWSaberFormsActionEvent>(OnOpen);
        SubscribeLocalEvent<SWSaberFormComponent, SWSaberFormSelectMessage>(OnSelect);
        SubscribeLocalEvent<SWSaberFormComponent, SWSaberFormWeaponLostEvent>(OnWeaponLost);
        SubscribeLocalEvent<SWSaberFormComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<BoundUserInterfaceMessageAttempt>(OnUiMessageAttempt);
    }

    private void OnMapInit(Entity<SWSaberFormComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _ui.SetUi(ent.Owner, Key, new InterfaceData("SWSaberFormsBoundUserInterface", interactionRange: 0));
    }

    private void OnShutdown(Entity<SWSaberFormComponent> ent, ref ComponentShutdown args)
    {
        CloseMenu(ent);
        if (ent.Comp.ActionEntity is { } action)
        {
            _actions.RemoveAction(ent.Owner, action);
            QueueDel(action);
        }
    }

    private bool TryGetEligibleWeapon(EntityUid user, out EntityUid weapon)
    {
        weapon = default;
        if (!_hands.TryGetActiveItem(user, out var held) || !_forms.TryGetActiveForm(user, held.Value, out _))
            return false;

        weapon = held.Value;
        return true;
    }

    private bool IsBusy(EntityUid weapon)
    {
        return !TryComp<MeleeWeaponComponent>(weapon, out var melee) ||
            melee.NextAttack > _timing.CurTime ||
            TryComp<SWWeaponBlockComponent>(weapon, out var guard) && _guard.IsBlocking((weapon, guard));
    }

    private void OnOpen(Entity<SWSaberFormComponent> ent, ref SWSaberFormsActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner)
            return;

        if (!TryGetEligibleWeapon(ent.Owner, out var weapon))
        {
            _popup.PopupEntity(Loc.GetString("sw-saber-forms-unavailable"), ent.Owner, ent.Owner);
            return;
        }

        if (IsBusy(weapon))
        {
            _popup.PopupEntity(Loc.GetString("sw-saber-forms-busy"), ent.Owner, ent.Owner);
            return;
        }

        ent.Comp.MenuWeapon = weapon;
        UpdateState(ent, weapon);
        if (!_ui.TryOpenUi(ent.Owner, Key, ent.Owner))
            CloseMenu(ent);

        args.Handled = true;
    }

    private void OnUiMessageAttempt(BoundUserInterfaceMessageAttempt args)
    {
        if (!Equals(args.UiKey, Key))
            return;

        // The engine also accepts requests to open an interface. Only the action
        // above may authorize the actor's current saber for this menu instance.
        if (args.Actor != args.Target ||
            !TryComp<SWSaberFormComponent>(args.Target, out var forms) ||
            forms.MenuWeapon == null)
        {
            args.Cancel();
        }
    }

    private void OnSelect(Entity<SWSaberFormComponent> ent, ref SWSaberFormSelectMessage args)
    {
        if (args.Actor != ent.Owner || !Equals(args.UiKey, Key) || !_ui.IsUiOpen(ent.Owner, Key, ent.Owner))
            return;

        if (!TryGetEligibleWeapon(ent.Owner, out var weapon) || ent.Comp.MenuWeapon != weapon)
        {
            CloseMenu(ent);
            return;
        }

        if (args.Weapon != GetNetEntity(weapon) ||
            !ent.Comp.AllowedForms.Contains(args.Form) ||
            !_prototypes.TryIndex(args.Form, out _) ||
            ent.Comp.ActiveForm == args.Form)
        {
            return;
        }

        if (IsBusy(weapon))
        {
            _popup.PopupEntity(Loc.GetString("sw-saber-forms-busy"), ent.Owner, ent.Owner);
            return;
        }

        _forms.ClearRiposte(ent);
        ent.Comp.ActiveForm = args.Form;
        Dirty(ent);
        UpdateState(ent, weapon);
    }

    private void OnWeaponLost(Entity<SWSaberFormComponent> ent, ref SWSaberFormWeaponLostEvent args)
    {
        if (ent.Comp.MenuWeapon == args.Weapon)
            CloseMenu(ent);
    }

    private void OnClosed(Entity<SWSaberFormComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.Actor == ent.Owner && Equals(args.UiKey, Key))
            ent.Comp.MenuWeapon = null;
    }

    private void CloseMenu(Entity<SWSaberFormComponent> ent)
    {
        ent.Comp.MenuWeapon = null;
        _ui.CloseUi(ent.Owner, Key);
        _ui.SetUiState(ent.Owner, Key, null);
    }

    private void UpdateState(Entity<SWSaberFormComponent> ent, EntityUid weapon)
    {
        _ui.SetUiState(ent.Owner, Key, new SWSaberFormsUiState(GetNetEntity(weapon), ent.Comp.ActiveForm, new(ent.Comp.AllowedForms)));
    }
}
